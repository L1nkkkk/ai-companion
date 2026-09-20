using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;
using AICompanion.Preview.History;
using UnityEngine;

namespace AICompanion.Preview.Tests
{
    /// <summary>Real disk and Unity JSON checks runnable without adding a package dependency.</summary>
    public static class HistoryChecks
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static int assertions;
        public static void Run()
        {
            assertions = 0;
            string directory = Path.Combine(Path.GetTempPath(), "saki-history-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            // The batch executeMethod thread owns UnitySynchronizationContext; run this
            // disk/JsonUtility-only suite outside it so waiting cannot deadlock its awaits.
            try { Task.Run(() => Verify(directory)).GetAwaiter().GetResult(); }
            finally { Directory.Delete(directory, true); }
            Debug.Log("U01_HISTORY_CHECKS_PASSED assertions=" + assertions);
        }

        private static async Task Verify(string directory)
        {
            var id = Guid.NewGuid();
            var operation = new OperationKey(Guid.NewGuid(), 1, id);
            HistoryWriteToken oldToken;
            using (var store = new JsonHistoryStore(directory))
            {
                var history = await store.CreateAsync(id, None);
                oldToken = history.WriteToken;
                Check(history.SchemaVersion == 1 && history.Records.Count == 0, "new versioned conversation");
                var user = Turn(operation, MessageRole.User, DeliveryKind.Text, DeliveryState.Displayed, "你好，世界！");
                Check((await store.AppendOrUpdateAsync(history.WriteToken, user, None)).Accepted, "user is stored");
                Check((await store.AppendOrUpdateAsync(history.WriteToken, Turn(operation, MessageRole.Assistant, DeliveryKind.Audio, DeliveryState.Generated, "测试回复"), None)).Accepted, "generated audio is not played");
                var export = await store.ExportAsync(id, None);
                Check(export.Succeeded && export.Value.MediaType == "application/json" && export.Value.Utf8Json.Length < 2 * 1024 * 1024, "bounded export");
                Check(Encoding.UTF8.GetString(export.Value.Utf8Json.ToArray()).Contains("你好，世界！"), "Unicode export");
                Check(!export.Value.SuggestedFileName.Contains("/"), "safe export basename");
                var invalid = new HistoryTurn(operation, null, MessageRole.Assistant, "invalid", DeliveryKind.Audio, DeliveryState.Played, 10, 100,
                    DateTimeOffset.UtcNow, PreviewMode.Fixture, SpeechMode.Fixture, null);
                Check(!(await store.AppendOrUpdateAsync(history.WriteToken, invalid, None)).Accepted, "partial playback cannot be marked played");
                string committedPath = Path.Combine(directory, "conversations.v1.json");
                string committed = File.ReadAllText(committedPath);
                bool writeFailed = false;
                using (var locked = new FileStream(committedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        await store.AppendOrUpdateAsync(history.WriteToken,
                            Turn(new OperationKey(Guid.NewGuid(), 2, id), MessageRole.User, DeliveryKind.Text, DeliveryState.Displayed, "文件占用测试"), None);
                    }
                    catch (IOException) { writeFailed = true; }
                }
                Check(writeFailed && File.ReadAllText(committedPath) == committed, "failed atomic replacement preserves committed file");
                Check((await store.LoadAsync(id, None)).Records.Count == 2, "failed write does not change in-memory index");
            }
            using (var store = new JsonHistoryStore(directory))
            {
                var history = await store.LoadAsync(id, None);
                Check(history.Records.Count == 2 && history.Records[1].DeliveryState == DeliveryState.Interrupted, "unfinished reply recovers as interrupted");
                Check(!string.IsNullOrEmpty(store.RecoveryMessage), "recovery is disclosed");
                Check(!(await store.AppendOrUpdateAsync(history.WriteToken, Turn(operation, MessageRole.Assistant, DeliveryKind.Audio, DeliveryState.Generated, "迟到"), None)).Accepted, "terminal records reject late regression");
                await store.DeleteConversationAsync(id, None);
                Check(!(await store.AppendOrUpdateAsync(oldToken, Turn(operation, MessageRole.User, DeliveryKind.Text, DeliveryState.Displayed, "旧回调"), None)).Accepted, "delete prevents resurrection");
                var recreated = await store.CreateAsync(id, None);
                Check(recreated.WriteToken.StorageGeneration > oldToken.StorageGeneration, "recreate increments storage generation");
                Check(!(await store.AppendOrUpdateAsync(oldToken, Turn(operation, MessageRole.User, DeliveryKind.Text, DeliveryState.Displayed, "旧回调"), None)).Accepted, "old token cannot write recreated id");
                var beforeClear = recreated.WriteToken;
                await store.ClearAllAsync(None);
                Check(store.Capacity.ConversationCount == 0, "clear is persisted");
                Check(!(await store.AppendOrUpdateAsync(beforeClear, Turn(operation, MessageRole.User, DeliveryKind.Text, DeliveryState.Displayed, "迟到"), None)).Accepted, "clear invalidates tokens");

                // Keep one selected conversation while adding the 21st, checking retention and cursors.
                var ids = new Guid[20];
                for (int i = 0; i < 20; i++) { ids[i] = Guid.NewGuid(); await store.CreateAsync(ids[i], None); }
                await store.LoadAsync(ids[0], None);
                var page = (await store.ListAsync(new ConversationListQuery(3, null), None)).Value;
                Check(page.Items.Count == 3 && page.NextCursor != null, "bounded pagination");
                var nextPage = await store.ListAsync(new ConversationListQuery(3, page.NextCursor), None);
                Check(nextPage.Succeeded && !page.Items.Select(x => x.ConversationId).Intersect(nextPage.Value.Items.Select(x => x.ConversationId)).Any(), "pagination does not repeat rows");
                await store.CreateAsync(Guid.NewGuid(), None);
                Check(store.Capacity.ConversationCount == 20 && (await store.LoadAsync(ids[0], None)).ConversationId == ids[0], "capacity preserves active conversation");
                Check(!(await store.ListAsync(new ConversationListQuery(3, page.NextCursor), None)).Succeeded, "stale cursor rejected");
                Check(!(await store.ListAsync(new ConversationListQuery(3, "not-a-cursor"), None)).Succeeded, "malformed cursor rejected");

                var capped = await store.LoadAsync(ids[0], None);
                for (uint i = 1; i <= 45; i++)
                {
                    var op = new OperationKey(Guid.NewGuid(), i, ids[0]);
                    await store.AppendOrUpdateAsync(capped.WriteToken, Turn(op, MessageRole.User, DeliveryKind.Text, DeliveryState.Displayed, "问题" + i), None);
                    await store.AppendOrUpdateAsync(capped.WriteToken, Turn(op, MessageRole.Assistant, DeliveryKind.Text, DeliveryState.Displayed, "回答" + i), None);
                }
                capped = await store.LoadAsync(ids[0], None);
                Check(capped.Records.Count == 80, "bounded message count");
                Check(capped.Records.GroupBy(t => t.Operation).All(g => g.Count() == 2), "message eviction preserves pairs");
                var completed = new OperationKey(Guid.NewGuid(), 99, ids[0]);
                var played = new HistoryTurn(completed, Guid.NewGuid(), MessageRole.Assistant, "播放完成", DeliveryKind.Audio, DeliveryState.Played, 24000, 24000,
                    DateTimeOffset.UtcNow, PreviewMode.Fixture, SpeechMode.Fixture, null);
                Check((await store.AppendOrUpdateAsync(capped.WriteToken, played, None)).Accepted, "actual completed playback can be stored");
                var failure = new HistoryTurn(new OperationKey(Guid.NewGuid(), 100, ids[0]), null, MessageRole.Assistant, "保留失败文字", DeliveryKind.Audio, DeliveryState.Failed, 0, 0,
                    DateTimeOffset.UtcNow, PreviewMode.Fixture, SpeechMode.Fixture, new PreviewError("invalid_audio", "声音校验失败", false, null));
                Check((await store.AppendOrUpdateAsync(capped.WriteToken, failure, None)).Accepted, "failed TTS preserves text and error");
                var canceled = new CancellationTokenSource(); canceled.Cancel();
                bool didCancel = false;
                try { await store.CreateAsync(Guid.NewGuid(), canceled.Token); }
                catch (OperationCanceledException) { didCancel = true; }
                Check(didCancel, "cancelled storage operation does not commit");
                canceled.Dispose();
            }
            // Single-file atomic replacement never recovers from an old backup that could resurrect deleted data.
            File.WriteAllText(Path.Combine(directory, "conversations.v1.json.tmp"), "partial staged write");
            using (var store = new JsonHistoryStore(directory))
            {
                Check((await store.ListAsync(new ConversationListQuery(20, null), None)).Value.Items.Count == 20, "partial staging file does not replace committed data");
                Check(!File.Exists(Path.Combine(directory, "conversations.v1.json.tmp")), "abandoned staging is cleaned");
                await store.ClearAllAsync(None);
            }
            File.WriteAllText(Path.Combine(directory, "conversations.v1.json"), "{not valid json", Encoding.UTF8);
            using (var store = new JsonHistoryStore(directory))
            {
                Check((await store.ListAsync(new ConversationListQuery(20, null), None)).Value.Items.Count == 0, "bad JSON recovers empty");
                Check(File.Exists(Path.Combine(directory, "conversations.v1.json.corrupt")), "bad JSON retained once for local recovery");
                await store.ClearAllAsync(None);
                Check(!File.Exists(Path.Combine(directory, "conversations.v1.json.corrupt")), "clear removes recovery data too");
            }
        }
        private static HistoryTurn Turn(OperationKey op, MessageRole role, DeliveryKind kind, DeliveryState state, string text) =>
            new HistoryTurn(op, null, role, text, kind, state, 0, 0, DateTimeOffset.UtcNow, PreviewMode.Fixture, SpeechMode.Fixture, null);
        private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception("History check failed: " + name); }
    }
}
