using System;
using System.Collections.Generic;

namespace AICompanion.Preview.Contracts
{
    public sealed class ConversationListQuery
    {
        public int PageSize { get; }
        public string Cursor { get; }

        public ConversationListQuery(int pageSize, string cursor)
        {
            PageSize = pageSize >= 1 && pageSize <= 20 ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));
            Cursor = cursor;
        }
    }

    public sealed class ConversationSummary
    {
        public Guid ConversationId { get; }
        public string Title { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public int MessageCount { get; }

        public ConversationSummary(Guid conversationId, string title, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc, int messageCount)
        {
            ConversationId = conversationId;
            Title = ContractCopy.Required(title, nameof(title));
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            MessageCount = messageCount;
        }
    }

    public sealed class ConversationPage
    {
        public IReadOnlyList<ConversationSummary> Items { get; }
        public string NextCursor { get; }
        public ulong StoreRevision { get; }

        public ConversationPage(IReadOnlyList<ConversationSummary> items, string nextCursor, ulong storeRevision)
        {
            Items = ContractCopy.List(items, 20);
            NextCursor = nextCursor;
            StoreRevision = storeRevision;
        }
    }

    public sealed class HistoryCapacitySnapshot
    {
        public int ConversationCount { get; }
        public int MaxConversations { get; }
        public int MaxMessagesPerConversation { get; }
        public HistoryRetentionPolicy RetentionPolicy { get; }
        public ulong StoreRevision { get; }

        public HistoryCapacitySnapshot(int conversationCount, int maxConversations, int maxMessagesPerConversation, HistoryRetentionPolicy retentionPolicy, ulong storeRevision)
        {
            ConversationCount = conversationCount;
            MaxConversations = maxConversations;
            MaxMessagesPerConversation = maxMessagesPerConversation;
            RetentionPolicy = retentionPolicy;
            StoreRevision = storeRevision;
        }
    }

    public sealed class HistoryTurn
    {
        public OperationKey Operation { get; }
        public Guid? TurnId { get; }
        public MessageRole Role { get; }
        public string Text { get; }
        public DeliveryKind DeliveryKind { get; }
        public DeliveryState DeliveryState { get; }
        public long PlayedSamples { get; }
        public long TotalSamples { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public PreviewMode Mode { get; }
        public SpeechMode TtsMode { get; }
        public PreviewError Error { get; }

        public HistoryTurn(OperationKey operation, Guid? turnId, MessageRole role, string text, DeliveryKind deliveryKind, DeliveryState deliveryState, long playedSamples, long totalSamples, DateTimeOffset updatedAtUtc, PreviewMode mode, SpeechMode ttsMode, PreviewError error)
        {
            Operation = operation;
            TurnId = turnId;
            Role = role;
            Text = ContractCopy.Required(text, nameof(text));
            DeliveryKind = deliveryKind;
            DeliveryState = deliveryState;
            PlayedSamples = playedSamples;
            TotalSamples = totalSamples;
            UpdatedAtUtc = updatedAtUtc;
            Mode = mode;
            TtsMode = ttsMode;
            Error = error;
        }
    }

    public sealed class ConversationHistory
    {
        public int SchemaVersion { get; }
        public Guid ConversationId { get; }
        public HistoryWriteToken WriteToken { get; }
        public string Title { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public IReadOnlyList<HistoryTurn> Records { get; }

        public ConversationHistory(int schemaVersion, Guid conversationId, HistoryWriteToken writeToken, string title, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc, IReadOnlyList<HistoryTurn> records)
        {
            SchemaVersion = schemaVersion;
            ConversationId = conversationId;
            WriteToken = writeToken;
            Title = ContractCopy.Required(title, nameof(title));
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            Records = ContractCopy.List(records, 80);
        }
    }

    public sealed class HistoryWriteResult
    {
        public bool Accepted { get; }
        public ulong StoreRevision { get; }
        public PreviewError Error { get; }

        public HistoryWriteResult(bool accepted, ulong storeRevision, PreviewError error)
        {
            Accepted = accepted;
            StoreRevision = storeRevision;
            Error = error;
        }
    }

    public sealed class HistoryExport
    {
        private readonly byte[] _utf8Json;
        public int SchemaVersion { get; }
        public Guid ConversationId { get; }
        public ulong StoreRevision { get; }
        public DateTimeOffset ExportedAtUtc { get; }
        public string SuggestedFileName { get; }
        public string MediaType { get; }
        public ReadOnlyMemory<byte> Utf8Json => _utf8Json;

        public HistoryExport(int schemaVersion, Guid conversationId, ulong storeRevision,
            DateTimeOffset exportedAtUtc, string suggestedFileName, string mediaType, ReadOnlyMemory<byte> utf8Json)
        {
            if (utf8Json.Length > 2 * 1024 * 1024) throw new ArgumentException("Export exceeds 2 MiB.", nameof(utf8Json));
            SchemaVersion = schemaVersion;
            ConversationId = conversationId;
            StoreRevision = storeRevision;
            ExportedAtUtc = exportedAtUtc;
            SuggestedFileName = ContractCopy.Required(suggestedFileName, nameof(suggestedFileName));
            MediaType = ContractCopy.Required(mediaType, nameof(mediaType));
            _utf8Json = utf8Json.ToArray();
        }
    }
}
