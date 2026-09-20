using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;
using UnityEngine;

namespace AICompanion.Preview.History
{
    /// <summary>A bounded, atomic local store. It never stores credentials or audio.</summary>
    public sealed class JsonHistoryStore : IHistoryStore
    {
        public const int MaxConversations = 20;
        public const int MaxMessages = 80;
        private const int MaximumStoreBytes = 16 * 1024 * 1024;
        private readonly string path;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private Store data;
        private bool loaded;
        private Guid activeConversation;
        private int queuedOperations;
        private bool disposed;
        public string RecoveryMessage { get; private set; }
        public HistoryCapacitySnapshot Capacity { get; private set; } =
            new HistoryCapacitySnapshot(0, MaxConversations, MaxMessages, HistoryRetentionPolicy.EvictOldestInactive, 0);
        public event Action<HistoryCapacitySnapshot> CapacityChanged;

        [Serializable] private sealed class Store
        {
            public int schemaVersion = 1;
            public string revision = "0";
            public string nextGeneration = "1";
            public List<Conversation> conversations = new List<Conversation>();
        }
        [Serializable] private sealed class Conversation
        {
            public string id, generation, title, createdAtUtc, updatedAtUtc;
            public List<Record> records = new List<Record>();
        }
        [Serializable] private sealed class Record
        {
            public string requestId, conversationId, turnId, text, updatedAtUtc;
            public uint generation;
            public int role, deliveryKind, deliveryState, mode, ttsMode;
            public long playedSamples, totalSamples;
            public string errorCode, errorMessage;
            public bool errorRetryable;
        }
        [Serializable] private sealed class ExportDocument
        {
            public int schemaVersion = 1;
            public string storeRevision, exportedAtUtc;
            public Conversation conversation;
        }

        public JsonHistoryStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("History directory is required.", nameof(directory));
            path = Path.Combine(Path.GetFullPath(directory), "conversations.v1.json");
        }

        public Task<LocalResult<ConversationPage>> ListAsync(ConversationListQuery query, CancellationToken ct)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return RunResult(() =>
            {
                var revision = Number(data.revision);
                int offset = 0;
                if (query.Cursor != null)
                {
                    var parts = query.Cursor.Split(':');
                    if (query.Cursor.Length > 512 || parts.Length != 2 ||
                        !ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var cursorRevision) ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset) ||
                        cursorRevision != revision || offset < 0 || offset > data.conversations.Count)
                        return Fail<ConversationPage>("history_cursor_expired", "历史列表已经变化，请刷新列表。");
                }
                var ordered = data.conversations.OrderByDescending(c => c.updatedAtUtc, StringComparer.Ordinal).ThenBy(c => c.id, StringComparer.Ordinal).ToList();
                var page = ordered.Skip(offset).Take(query.PageSize).Select(c => new ConversationSummary(
                    Guid.Parse(c.id), c.title, Date(c.createdAtUtc), Date(c.updatedAtUtc), c.records.Count)).ToArray();
                int end = offset + page.Length;
                var next = end < ordered.Count ? revision.ToString(CultureInfo.InvariantCulture) + ":" + end.ToString(CultureInfo.InvariantCulture) : null;
                return Ok(new ConversationPage(page, next, revision));
            }, ct);
        }

        public Task<ConversationHistory> CreateAsync(Guid conversationId, CancellationToken ct) => Run(() =>
        {
            RequireId(conversationId);
            var existing = Find(conversationId);
            if (existing != null) { activeConversation = conversationId; return Snapshot(existing); }
            var next = Copy();
            if (next.conversations.Count == MaxConversations)
            {
                var oldest = next.conversations.Where(c => c.id != activeConversation.ToString("D"))
                    .OrderBy(c => c.updatedAtUtc, StringComparer.Ordinal).First();
                next.conversations.Remove(oldest);
            }
            ulong generation = Number(next.nextGeneration);
            next.nextGeneration = checked(generation + 1).ToString(CultureInfo.InvariantCulture);
            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var created = new Conversation { id = conversationId.ToString("D"), generation = generation.ToString(CultureInfo.InvariantCulture), title = "新的对话", createdAtUtc = now, updatedAtUtc = now };
            next.conversations.Add(created);
            Commit(next, ct);
            activeConversation = conversationId;
            return Snapshot(created);
        }, ct);

        public Task<ConversationHistory> LoadAsync(Guid conversationId, CancellationToken ct) => Run(() =>
        {
            RequireId(conversationId);
            var conversation = Find(conversationId);
            if (conversation == null) throw new FileNotFoundException("The conversation no longer exists.");
            activeConversation = conversationId;
            return Snapshot(conversation);
        }, ct);

        public Task<HistoryWriteResult> AppendOrUpdateAsync(HistoryWriteToken writeToken, HistoryTurn turn, CancellationToken ct) => Run(() =>
        {
            var conversation = Find(writeToken.ConversationId);
            if (conversation == null || Number(conversation.generation) != writeToken.StorageGeneration)
                return Rejected("history_stale_write", "会话已删除或替换，旧回复不会重新写入。");
            try { ValidateTurn(turn, writeToken.ConversationId); }
            catch (ArgumentException) { return Rejected("invalid_history_record", "这条记录未满足历史格式要求。"); }
            var next = Copy();
            var changed = next.conversations.Find(c => c.id == conversation.id);
            var record = FromTurn(turn);
            var index = changed.records.FindIndex(r => r.requestId == record.requestId && r.generation == record.generation && r.role == record.role);
            if (index >= 0)
            {
                var previous = changed.records[index];
                if (Date(record.updatedAtUtc) < Date(previous.updatedAtUtc) ||
                    (IsFinal(previous.deliveryState) && (record.deliveryState != previous.deliveryState || record.text != previous.text || record.playedSamples != previous.playedSamples)))
                    return Rejected("history_stale_write", "已封存的记录不能被迟到回复覆盖。");
                changed.records[index] = record;
            }
            else changed.records.Add(record);
            while (changed.records.Count > MaxMessages)
            {
                var oldest = changed.records.FirstOrDefault(r => r.requestId != record.requestId || r.generation != record.generation);
                if (oldest == null) return Rejected("history_capacity", "当前会话的记录已达到容量限制。");
                changed.records.RemoveAll(r => r.requestId == oldest.requestId && r.generation == oldest.generation);
            }
            if (turn.Role == MessageRole.User && (changed.title == "新的对话" || changed.records.Count == 1))
                changed.title = TruncateScalars(turn.Text.Replace('\r', ' ').Replace('\n', ' '), 80);
            changed.updatedAtUtc = turn.UpdatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            Commit(next, ct);
            return new HistoryWriteResult(true, Number(data.revision), null);
        }, ct);

        public Task<LocalResult<HistoryExport>> ExportAsync(Guid conversationId, CancellationToken ct) => RunResult(() =>
        {
            var found = Find(conversationId);
            if (found == null) return Fail<HistoryExport>("history_not_found", "该会话已不存在。");
            var now = DateTimeOffset.UtcNow;
            var document = new ExportDocument { storeRevision = data.revision, exportedAtUtc = now.ToString("O", CultureInfo.InvariantCulture), conversation = found };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(document, true));
            if (bytes.Length > 2 * 1024 * 1024) return Fail<HistoryExport>("history_export_too_large", "导出内容超过 2 MiB 限制。");
            return Ok(new HistoryExport(1, conversationId, Number(data.revision), now,
                "conversation-" + conversationId.ToString("N") + ".json", "application/json", bytes));
        }, ct);

        public Task DeleteConversationAsync(Guid conversationId, CancellationToken ct) => Run(() =>
        {
            RequireId(conversationId);
            var next = Copy();
            if (next.conversations.RemoveAll(c => c.id == conversationId.ToString("D")) > 0) Commit(next, ct);
            RemoveRecoveryCopy();
            if (activeConversation == conversationId) activeConversation = Guid.Empty;
            return true;
        }, ct);

        public Task ClearAllAsync(CancellationToken ct) => Run(() =>
        {
            var next = Copy();
            next.conversations.Clear();
            // Generation is retained across clearing, so recreating an ID cannot validate an old token.
            Commit(next, ct);
            RemoveRecoveryCopy();
            activeConversation = Guid.Empty;
            return true;
        }, ct);

        private async Task<T> Run<T>(Func<T> operation, CancellationToken ct)
        {
            if (Interlocked.Increment(ref queuedOperations) > 64)
            {
                Interlocked.Decrement(ref queuedOperations);
                throw new IOException("History operation queue is full.");
            }
            bool entered = false;
            ulong previous = 0;
            try
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                entered = true;
                previous = Capacity.StoreRevision;
                if (disposed) throw new ObjectDisposedException(nameof(JsonHistoryStore));
                return await Task.Run(() => { ct.ThrowIfCancellationRequested(); EnsureLoaded(ct); return operation(); }, ct).ConfigureAwait(false);
            }
            finally
            {
                var changed = Capacity;
                if (entered) gate.Release();
                Interlocked.Decrement(ref queuedOperations);
                if (entered && !disposed && previous != changed.StoreRevision) CapacityChanged?.Invoke(changed);
            }
        }

        private async Task<LocalResult<T>> RunResult<T>(Func<LocalResult<T>> operation, CancellationToken ct) where T : class
        {
            try { return await Run(operation, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { return Fail<T>("storage_unavailable", "本机历史暂不可用，请检查磁盘空间与目录权限。"); }
        }

        private void EnsureLoaded(CancellationToken ct)
        {
            if (loaded) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path)) { data = new Store(); SetCapacity(); loaded = true; return; }
            try
            {
                if (new FileInfo(path).Length > MaximumStoreBytes) throw new FormatException();
                var loaded = JsonUtility.FromJson<Store>(File.ReadAllText(path, new UTF8Encoding(false, true)));
                ValidateStore(loaded);
                data = loaded;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
            {
                // Preserve one malformed input for local recovery, never include contents in logs.
                RemoveRecoveryCopy();
                File.Move(path, path + ".corrupt");
                data = new Store();
                RecoveryMessage = "历史文件损坏，已隔离并恢复为空列表。";
                Commit(Copy(), ct);
            }
            bool interrupted = false;
            foreach (var conversation in data.conversations)
            foreach (var record in conversation.records)
                if (record.role == (int)MessageRole.Assistant &&
                    (record.deliveryState == (int)DeliveryState.Generating || record.deliveryState == (int)DeliveryState.Generated))
                {
                    record.deliveryState = (int)DeliveryState.Interrupted;
                    record.updatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    interrupted = true;
                }
            if (interrupted)
            {
                RecoveryMessage = "上次退出前未完成的回复已标记为中断。";
                Commit(Copy(), ct);
            }
            SetCapacity();
            // A crash before the atomic replacement can leave a partial staging file. Never read it.
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            loaded = true;
        }

        private void Commit(Store next, CancellationToken ct)
        {
            next.revision = checked(Number(data.revision) + 1).ToString(CultureInfo.InvariantCulture);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(next));
            if (bytes.Length > MaximumStoreBytes) throw new IOException("History store capacity exceeded.");
            ct.ThrowIfCancellationRequested();
            string temporary = path + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                ct.ThrowIfCancellationRequested();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                // Mono's File.Replace failed after reopening an existing store on the
                // target Windows setup. Use same-volume Windows atomic rename directly;
                // do not emulate replacement by deleting the committed file first.
                if (!MoveFileEx(temporary, path, 0x1 | 0x8))
                    throw new IOException("Atomic history replacement failed (Windows error " + Marshal.GetLastWin32Error() + ").");
#else
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
#endif
                data = next;
                SetCapacity();
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);
#endif

        private static void ValidateStore(Store store)
        {
            if (store == null || store.schemaVersion != 1 || store.conversations == null || store.conversations.Count > MaxConversations) throw new FormatException();
            Number(store.revision);
            ulong nextGeneration = Number(store.nextGeneration);
            var ids = new HashSet<Guid>();
            var generations = new HashSet<ulong>();
            foreach (var conversation in store.conversations)
            {
                Guid id = Guid.Parse(conversation.id); RequireId(id);
                ulong generation = Number(conversation.generation);
                if (!ids.Add(id) || !generations.Add(generation) || generation == 0 || generation >= nextGeneration ||
                    conversation.records == null || conversation.records.Count > MaxMessages ||
                    conversation.title == null || Scalars(conversation.title) > 80) throw new FormatException();
                Date(conversation.createdAtUtc); Date(conversation.updatedAtUtc);
                var keys = new HashSet<string>();
                foreach (var record in conversation.records)
                {
                    if (record == null || !keys.Add(record.requestId + ":" + record.generation + ":" + record.role)) throw new FormatException();
                    ValidateTurn(ToTurn(record), id);
                }
            }
        }

        private static void ValidateTurn(HistoryTurn turn, Guid conversationId)
        {
            if (turn == null || turn.Operation.RequestId == Guid.Empty || turn.Operation.ConversationId != conversationId ||
                conversationId == Guid.Empty || turn.TurnId == Guid.Empty ||
                !Enum.IsDefined(typeof(MessageRole), turn.Role) || !Enum.IsDefined(typeof(DeliveryKind), turn.DeliveryKind) ||
                !Enum.IsDefined(typeof(DeliveryState), turn.DeliveryState) || !Enum.IsDefined(typeof(PreviewMode), turn.Mode) ||
                !Enum.IsDefined(typeof(SpeechMode), turn.TtsMode) ||
                Scalars(turn.Text) > (turn.Role == MessageRole.User ? 2000 : 400) ||
                turn.PlayedSamples < 0 || turn.TotalSamples < turn.PlayedSamples || turn.TotalSamples > 24000L * 120 ||
                (turn.DeliveryState == DeliveryState.Failed) != (turn.Error != null) ||
                (turn.DeliveryKind == DeliveryKind.Text && (turn.PlayedSamples != 0 || turn.TotalSamples != 0 || turn.DeliveryState == DeliveryState.Played)) ||
                (turn.DeliveryKind == DeliveryKind.Audio && turn.DeliveryState == DeliveryState.Displayed) ||
                (turn.DeliveryState == DeliveryState.Played && (turn.TotalSamples == 0 || turn.PlayedSamples != turn.TotalSamples)))
                throw new ArgumentException("Invalid history record.");
            if (turn.Error != null && (turn.Error.Code.Length > 128 || turn.Error.Message.Length > 1024)) throw new ArgumentException("Invalid history error.");
        }

        private Store Copy() => new Store
        {
            revision = data.revision, nextGeneration = data.nextGeneration,
            conversations = data.conversations.Select(c => new Conversation { id = c.id, generation = c.generation, title = c.title,
                createdAtUtc = c.createdAtUtc, updatedAtUtc = c.updatedAtUtc, records = new List<Record>(c.records) }).ToList()
        };
        private Conversation Find(Guid id) => data.conversations.Find(c => c.id == id.ToString("D"));
        private ConversationHistory Snapshot(Conversation c) => new ConversationHistory(1, Guid.Parse(c.id),
            new HistoryWriteToken(Guid.Parse(c.id), Number(c.generation)), c.title, Date(c.createdAtUtc), Date(c.updatedAtUtc), c.records.Select(ToTurn).ToArray());
        private void SetCapacity() => Capacity = new HistoryCapacitySnapshot(data.conversations.Count, MaxConversations, MaxMessages, HistoryRetentionPolicy.EvictOldestInactive, Number(data.revision));
        private void RemoveRecoveryCopy() { if (File.Exists(path + ".corrupt")) File.Delete(path + ".corrupt"); }
        private HistoryWriteResult Rejected(string code, string message) => new HistoryWriteResult(false, Number(data.revision), new PreviewError(code, message, false, null));
        private static bool IsFinal(int state) => state >= (int)DeliveryState.Displayed;
        private static LocalResult<T> Ok<T>(T value) where T : class => new LocalResult<T>(true, value, null);
        private static LocalResult<T> Fail<T>(string code, string text) where T : class => new LocalResult<T>(false, null, new PreviewError(code, text, false, null));
        private static ulong Number(string text) => ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        private static DateTimeOffset Date(string text) => DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        private static void RequireId(Guid id) { if (id == Guid.Empty) throw new ArgumentException("Empty conversation ID."); }
        private static int Scalars(string text)
        {
            if (text == null) throw new ArgumentException("Null text.");
            int count = 0;
            for (int i = 0; i < text.Length; i++, count++)
            {
                if (char.IsHighSurrogate(text[i])) { if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new ArgumentException("Invalid Unicode."); }
                else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("Invalid Unicode.");
            }
            return count;
        }
        private static string TruncateScalars(string text, int maximum)
        {
            int i = 0, count = 0;
            while (i < text.Length && count++ < maximum) { if (char.IsHighSurrogate(text[i])) i++; i++; }
            return text.Substring(0, i);
        }
        private static Record FromTurn(HistoryTurn t) => new Record
        {
            requestId = t.Operation.RequestId.ToString("D"), conversationId = t.Operation.ConversationId.ToString("D"), generation = t.Operation.Generation,
            turnId = t.TurnId?.ToString("D"), role = (int)t.Role, text = t.Text, deliveryKind = (int)t.DeliveryKind, deliveryState = (int)t.DeliveryState,
            playedSamples = t.PlayedSamples, totalSamples = t.TotalSamples, updatedAtUtc = t.UpdatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            mode = (int)t.Mode, ttsMode = (int)t.TtsMode, errorCode = t.Error?.Code, errorMessage = t.Error?.Message, errorRetryable = t.Error?.Retryable ?? false
        };
        private static HistoryTurn ToTurn(Record r)
        {
            var operation = new OperationKey(Guid.Parse(r.requestId), r.generation, Guid.Parse(r.conversationId));
            var error = string.IsNullOrEmpty(r.errorCode) ? null : new PreviewError(r.errorCode, r.errorMessage ?? "", r.errorRetryable, operation);
            return new HistoryTurn(operation, string.IsNullOrEmpty(r.turnId) ? (Guid?)null : Guid.Parse(r.turnId), (MessageRole)r.role, r.text,
                (DeliveryKind)r.deliveryKind, (DeliveryState)r.deliveryState, r.playedSamples, r.totalSamples, Date(r.updatedAtUtc), (PreviewMode)r.mode, (SpeechMode)r.ttsMode, error);
        }
        public void Dispose() { disposed = true; CapacityChanged = null; }
    }
}
