using System;

namespace AICompanion.Preview.Contracts
{
    // Construct only after validating the wire envelope. No dictionary or provider object crosses this boundary.
    public abstract class PreviewEvent
    {
        public string Protocol => PreviewProtocol.Name;
        public OperationKey Operation => Turn.Operation;
        public TurnKey Turn { get; }
        public uint Sequence { get; }
        public abstract string Type { get; }

        private protected PreviewEvent(TurnKey turn, uint sequence)
        {
            Turn = turn;
            Sequence = sequence;
        }
    }

    public sealed class TurnAcceptedEvent : PreviewEvent
    {
        public override string Type => "turn.accepted";
        public PreviewMode Mode { get; }
        public TurnAcceptedEvent(TurnKey turn, uint sequence, PreviewMode mode)
            : base(turn, sequence)
        {
            Mode = mode;
        }
    }

    public sealed class TextDeltaEvent : PreviewEvent
    {
        public override string Type => "text.delta";
        public string Text { get; }
        public TextDeltaEvent(TurnKey turn, uint sequence, string text)
            : base(turn, sequence)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }
    }

    public sealed class TextCompletedEvent : PreviewEvent
    {
        public override string Type => "text.completed";
        public string Text { get; }
        public Emotion Emotion { get; }
        public TextCompletedEvent(TurnKey turn, uint sequence, string text, Emotion emotion)
            : base(turn, sequence)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Emotion = emotion;
        }
    }

    public sealed class AudioSkippedEvent : PreviewEvent
    {
        public override string Type => "audio.skipped";
        public AudioSkipReason Reason { get; }
        public AudioSkippedEvent(TurnKey turn, uint sequence, AudioSkipReason reason)
            : base(turn, sequence)
        {
            Reason = reason;
        }
    }

    public sealed class GenerationCompletedEvent : PreviewEvent
    {
        public override string Type => "generation.completed";

        public GenerationCompletedEvent(TurnKey turn, uint sequence)
            : base(turn, sequence)
        {

        }
    }

    public sealed class TurnCancelledEvent : PreviewEvent
    {
        public override string Type => "turn.cancelled";

        public TurnCancelledEvent(TurnKey turn, uint sequence)
            : base(turn, sequence)
        {

        }
    }

    public sealed class TurnErrorEvent : PreviewEvent
    {
        public override string Type => "error";
        public PreviewError Error { get; }
        public TurnErrorEvent(TurnKey turn, uint sequence, PreviewError error)
            : base(turn, sequence)
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }
    }

    public sealed class AudioReadyEvent : PreviewEvent
    {
        public override string Type => "audio.ready";
        public AudioReadyDescriptor Audio { get; }

        public AudioReadyEvent(AudioReadyDescriptor audio, uint sequence)
            : base((audio ?? throw new ArgumentNullException(nameof(audio))).Turn, sequence)
        {
            Audio = audio;
        }
    }
}
