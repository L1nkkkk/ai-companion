using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AICompanion.Preview.UI
{
    /// <summary>Uses TMP's native editing/IME path; Return is handled once after editing.</summary>
    public sealed class ChatInputField : TMP_InputField
    {
        public event Action SendRequested;
        private int lastCompositionFrame = int.MinValue;
        private readonly Event editingEvent = new Event();
        private readonly ReturnKeyGate returnGate = new ReturnKeyGate();
        private bool wasComposing;

        protected override void Awake()
        {
            base.Awake();
            lineType = LineType.MultiLineNewline;
            onValidateInput = (value, index, character) => character == '\v' ? '\n' : character;
        }

        public override void OnUpdateSelected(BaseEventData eventData)
        {
            if (!isFocused) return;
            bool composing = !string.IsNullOrEmpty(Input.compositionString);
            if (composing) lastCompositionFrame = Time.frameCount;
            bool compositionEnded = wasComposing && !composing;
            bool send = false;
            // Use the same native Event queue as TMP 3.0.6. A modifier can be pressed and
            // released between frames, so Input.GetKey cannot describe an earlier Return.
            while (Event.PopEvent(editingEvent))
            {
                if (editingEvent.keyCode == KeyCode.Return || editingEvent.keyCode == KeyCode.KeypadEnter)
                {
                    var decision = returnGate.Observe(editingEvent.rawType, editingEvent.modifiers, composing,
                        (long)Time.frameCount - lastCompositionFrame, Time.frameCount);
                    if (decision == ReturnKeyAction.Newline) { Append('\n'); RefreshEditingGeometry(); }
                    else if (decision == ReturnKeyAction.Send) send = true;
                    continue;
                }
                if (editingEvent.rawType == EventType.KeyDown)
                {
                    // Windows also queues a character-only event for the same Return.
                    // Physical Return above owns this action; clipboard paste still uses Append(string).
                    if (EnterPolicy.IsReturnCharacter(editingEvent.character)) continue;
                    // Match TMP's protection for the navigation event which ends IME.
                    if (compositionEnded && editingEvent.character == 0 && editingEvent.modifiers == EventModifiers.None) continue;
                    if (KeyPressed(editingEvent) == EditState.Finish) DeactivateInputField();
                    RefreshEditingGeometry();
                }
                else if ((editingEvent.rawType == EventType.ValidateCommand || editingEvent.rawType == EventType.ExecuteCommand) &&
                    editingEvent.commandName == "SelectAll") { SelectAll(); RefreshEditingGeometry(); }
            }
            wasComposing = composing;
            if (send && string.IsNullOrEmpty(Input.compositionString)) SendRequested?.Invoke();
            eventData.Use();
        }

        private void RefreshEditingGeometry()
        {
            // TMP updates its character/line information after each editing event, so a
            // subsequent Backspace or navigation event in the same queue sees current indices.
            UpdateLabel();
            if (textComponent != null) textComponent.ForceMeshUpdate();
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            returnGate.Reset();
            wasComposing = false;
            lastCompositionFrame = int.MinValue;
            base.OnDeselect(eventData);
        }
    }

    public enum ReturnKeyAction { None, Newline, Send }

    public sealed class ReturnKeyGate
    {
        private bool held;
        private int lastSendFrame = -1;
        public ReturnKeyAction Observe(EventType type, EventModifiers modifiers, bool composing, long framesSinceComposition, int frame)
        {
            if (type == EventType.KeyUp) { held = false; return ReturnKeyAction.None; }
            if (type != EventType.KeyDown || held) return ReturnKeyAction.None;
            held = true;
            if (composing || framesSinceComposition <= 2) return ReturnKeyAction.None;
            if ((modifiers & EventModifiers.Shift) != 0) return ReturnKeyAction.Newline;
            if (lastSendFrame == frame) return ReturnKeyAction.None;
            lastSendFrame = frame;
            return ReturnKeyAction.Send;
        }
        public void Reset() { held = false; }
    }

    public static class EnterPolicy
    {
        public static bool IsReturnCharacter(char value) => value == '\r' || value == '\n' || value == '\v' || value == '\u0003';

        // A short frame guard also catches the Return that ended composition just before TMP polls.
        public static bool ShouldSubmit(bool returnPressed, bool shift, bool composing, long framesSinceComposition) =>
            returnPressed && !shift && !composing && framesSinceComposition > 2;

        public static int CountScalars(string value)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++, count++)
            {
                if (char.IsHighSurrogate(value[i]))
                { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return int.MaxValue; }
                else if (char.IsLowSurrogate(value[i])) return int.MaxValue;
            }
            return count;
        }
    }
}
