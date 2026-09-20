using System;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.UI
{
    public enum HistoryExportStage { Stopping, Stopped, Capturing, DialogOpen, Saving, Saved, Cancelled, Failed, Finished }

    /// <summary>Content-free observation; timestamps share Stopwatch's monotonic clock.</summary>
    public sealed class HistoryExportProgress
    {
        public HistoryExportStage Stage { get; }
        public Guid ConversationId { get; }
        public SessionPhase TriggerPhase { get; }
        public OperationKey? TriggerOperation { get; }
        public long TimestampTicks { get; }

        internal HistoryExportProgress(HistoryExportStage stage, Guid conversationId, SessionPhase phase, OperationKey? operation, long ticks)
        { Stage = stage; ConversationId = conversationId; TriggerPhase = phase; TriggerOperation = operation; TimestampTicks = ticks; }
    }
}
