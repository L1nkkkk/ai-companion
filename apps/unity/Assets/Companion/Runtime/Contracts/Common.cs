using System;
using System.Collections.Generic;

namespace AICompanion.Preview.Contracts
{
    public readonly struct OperationKey : IEquatable<OperationKey>
    {
        public Guid RequestId { get; }
        public uint Generation { get; }
        public Guid ConversationId { get; }

        public OperationKey(Guid requestId, uint generation, Guid conversationId)
        {
            RequestId = requestId;
            Generation = generation;
            ConversationId = conversationId;
        }

        public bool Equals(OperationKey other) => RequestId.Equals(other.RequestId) && Generation.Equals(other.Generation) && ConversationId.Equals(other.ConversationId);
        public override bool Equals(object obj) => obj is OperationKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + RequestId.GetHashCode();
                hash = hash * 31 + Generation.GetHashCode();
                hash = hash * 31 + ConversationId.GetHashCode();
                return hash;
            }
        }
        public static bool operator ==(OperationKey left, OperationKey right) => left.Equals(right);
        public static bool operator !=(OperationKey left, OperationKey right) => !left.Equals(right);
    }

    public readonly struct TurnKey : IEquatable<TurnKey>
    {
        public OperationKey Operation { get; }
        public Guid TurnId { get; }

        public TurnKey(OperationKey operation, Guid turnId)
        {
            Operation = operation;
            TurnId = turnId;
        }

        public bool Equals(TurnKey other) => Operation.Equals(other.Operation) && TurnId.Equals(other.TurnId);
        public override bool Equals(object obj) => obj is TurnKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Operation.GetHashCode();
                hash = hash * 31 + TurnId.GetHashCode();
                return hash;
            }
        }
        public static bool operator ==(TurnKey left, TurnKey right) => left.Equals(right);
        public static bool operator !=(TurnKey left, TurnKey right) => !left.Equals(right);
    }

    public readonly struct DraftKey : IEquatable<DraftKey>
    {
        public OperationKey Operation { get; }
        public ulong DraftRevision { get; }

        public DraftKey(OperationKey operation, ulong draftRevision)
        {
            Operation = operation;
            DraftRevision = draftRevision;
        }

        public bool Equals(DraftKey other) => Operation.Equals(other.Operation) && DraftRevision.Equals(other.DraftRevision);
        public override bool Equals(object obj) => obj is DraftKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Operation.GetHashCode();
                hash = hash * 31 + DraftRevision.GetHashCode();
                return hash;
            }
        }
        public static bool operator ==(DraftKey left, DraftKey right) => left.Equals(right);
        public static bool operator !=(DraftKey left, DraftKey right) => !left.Equals(right);
    }

    public readonly struct HistoryWriteToken : IEquatable<HistoryWriteToken>
    {
        public Guid ConversationId { get; }
        public ulong StorageGeneration { get; }

        public HistoryWriteToken(Guid conversationId, ulong storageGeneration)
        {
            ConversationId = conversationId;
            StorageGeneration = storageGeneration;
        }

        public bool Equals(HistoryWriteToken other) => ConversationId.Equals(other.ConversationId) && StorageGeneration.Equals(other.StorageGeneration);
        public override bool Equals(object obj) => obj is HistoryWriteToken other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + ConversationId.GetHashCode();
                hash = hash * 31 + StorageGeneration.GetHashCode();
                return hash;
            }
        }
        public static bool operator ==(HistoryWriteToken left, HistoryWriteToken right) => left.Equals(right);
        public static bool operator !=(HistoryWriteToken left, HistoryWriteToken right) => !left.Equals(right);
    }

    public enum SessionPhase { Offline, Ready, Recording, Transcribing, Thinking, PreparingSpeech, Speaking, Stopping, Error }
    public enum PreviewMode { Unknown, Fixture, Cloud }
    public enum SpeechMode { Unknown, Fixture, System, Cloud }
    public enum DeliveryKind { Text, Audio }
    public enum DeliveryState { Generating, Generated, Displayed, Played, Interrupted, Failed }
    public enum Emotion { Neutral, Happy, Sad, Surprised, Thinking }
    public enum StopReason { User, Replaced, NewConversation, SelectConversation, DeleteHistory, WindowClosing, Failure }
    public enum PlaybackEndReason { Completed, Stopped, Failed }
    public enum MessageRole { User, Assistant }
    public enum SettingsSaveState { Saved, Pending, Failed }
    public enum VoiceOptionsState { NotLoaded, Loading, Ready, Unavailable, Failed }
    public enum HistoryRetentionPolicy { EvictOldestInactive }
    public enum AvatarPlaybackState { Idle, Speaking, Stopped, Failed }
    public enum AvatarActionStatus { Applied, Unsupported, StaleOperation, Unavailable }
    public enum AudioSkipReason { UserDisabled }

    public sealed class PreviewError
    {
        public string Code { get; }
        public string Message { get; }
        public bool Retryable { get; }
        public OperationKey? Operation { get; }

        public PreviewError(string code, string message, bool retryable, OperationKey? operation)
        {
            Code = ContractCopy.Required(code, nameof(code));
            Message = ContractCopy.Required(message, nameof(message));
            Retryable = retryable;
            Operation = operation;
        }
    }

    // Local commands do not create a cloud operation.
    public sealed class LocalCommandResult
    {
        public bool Accepted { get; }
        public PreviewError Error { get; }
        public LocalCommandResult(bool accepted, PreviewError error)
        {
            if (accepted == (error != null)) throw new ArgumentException("Result must contain either acceptance or an error.");
            Accepted = accepted;
            Error = error;
        }
    }

    public sealed class LocalResult<T> where T : class
    {
        public bool Succeeded { get; }
        public T Value { get; }
        public PreviewError Error { get; }
        public LocalResult(bool succeeded, T value, PreviewError error)
        {
            if (succeeded ? value == null || error != null : value != null || error == null)
                throw new ArgumentException("Result must contain exactly one value or error.");
            Succeeded = succeeded;
            Value = value;
            Error = error;
        }
    }

    internal static class ContractCopy
    {
        internal static string Required(string value, string parameter)
        {
            return value ?? throw new ArgumentNullException(parameter);
        }

        internal static IReadOnlyList<T> List<T>(IEnumerable<T> source, int maximum)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new List<T>();
            foreach (T item in source)
            {
                if (copy.Count == maximum) throw new ArgumentException("Collection exceeds the contract capacity.", nameof(source));
                if (ReferenceEquals(item, null)) throw new ArgumentException("Collection cannot contain null.", nameof(source));
                copy.Add(item);
            }
            return copy.AsReadOnly();
        }
    }
}
