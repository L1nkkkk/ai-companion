using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AICompanion.Preview.UI
{
    /// <summary>A desktop view that knows only the frozen Session boundary.</summary>
    public sealed class DesktopChatView : MonoBehaviour
    {
        private static readonly Color Background = new Color32(19, 24, 34, 255);
        private static readonly Color Surface = new Color32(29, 37, 50, 255);
        private static readonly Color Muted = new Color32(150, 163, 181, 255);
        private static readonly Color Ink = new Color32(235, 241, 247, 255);
        private static readonly Color Accent = new Color32(132, 226, 203, 255);
        private static readonly Color Warning = new Color32(242, 195, 125, 255);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly List<MessageRow> rows = new List<MessageRow>(80);
        private ISessionController session;
        private TMP_FontAsset font;
        private string characterId;
        private Action<HistoryExport> exportSink;
        private RectTransform canvas, messageContent, modal, modalContent;
        private ScrollRect scroll;
        private ChatInputField input;
        private TextMeshProUGUI state, mode, footer, counter, volumeLabel, autoReadLabel, muteLabel, emptyLabel, speechMode;
        private Slider volume;
        private Button send, stop, mute, autoRead;
        private SessionSnapshot snapshot;
        private Guid shownConversation;
        private bool localBusy, initialized, syncing;
        private int lastSendFrame = -1, historyVersion;
        private float lastAudibleVolume = .8f;
        private ulong shownRevision;
        private string modalKind;

        private sealed class MessageRow
        {
            public RectTransform Root;
            public TextMeshProUGUI Heading, Body;
            public Image Background;
            public HistoryTurn LastMessage;
        }

        public void Initialize(ISessionController controller, TMP_FontAsset chineseFont, string character = "mao", Action<HistoryExport> onExport = null)
        {
            if (initialized) throw new InvalidOperationException("DesktopChatView is already initialized.");
            session = controller ?? throw new ArgumentNullException(nameof(controller));
            font = chineseFont != null ? chineseFont : throw new ArgumentNullException(nameof(chineseFont));
            characterId = character;
            exportSink = onExport;
            Build();
            session.SnapshotChanged += Render;
            session.DraftUpdated += ApplyDraft;
            initialized = true;
            Render(session.Snapshot);
        }

        private void Build()
        {
            var root = new GameObject("Desktop Companion UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<RectTransform>();
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            scaler.matchWidthOrHeight = .5f;
            if (EventSystem.current == null)
                new GameObject("Desktop Event System", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);

            var header = Panel("Header", canvas, Background);
            Place(header, 0, 1, 1, 1, 0, -84, 0, 0);
            Label("Brand", header, "SAKI", 29, Accent, 28, 22, 180, 42, true);
            Label("Edition", header, "桌面陪伴 / 开发预览", 14, Muted, 146, 26, 300, 34);
            ButtonAt(header, "本机历史", () => OpenHistory(), 1, 1, -304, -61, 124, 39);
            ButtonAt(header, "设置", () => OpenSettings(), 1, 1, -166, -61, 108, 39);

            var leftFoot = Panel("Character caption", canvas, new Color(Background.r, Background.g, Background.b, .96f));
            Place(leftFoot, 0, 0, .42f, 0, 0, 0, 0, 126);
            Label("Character", leftFoot, "Mao  /  演示角色", 23, Ink, 30, 76, 400, 38, true);
            Label("Character detail", leftFoot, "真实 Live2D · 随实际输出音量开合口型", 14, Muted, 30, 47, 430, 25);
            Label("Greeting hint", leftFoot, "点击角色打招呼", 13, Accent, 30, 26, 430, 23);

            var chat = Panel("Conversation", canvas, Background);
            Place(chat, .42f, 0, 1, 1, 0, 0, 0, -84);
            Label("Chat title", chat, "把今天，说给我听。", 25, Ink, 26, -53, 510, 38, true, true);
            mode = Label("Service mode", chat, "未就绪 · 等待本机后台", 14, Warning, 27, -86, 590, 26, false, true);
            state = Label("Status", chat, "正在连接", 14, Accent, 27, -117, 580, 24, false, true);
            state.enableWordWrapping = false;
            state.overflowMode = TextOverflowModes.Ellipsis;

            var newConversation = ButtonAt(chat, "＋ 新会话", () => RunLocal(() => session.NewConversationAsync(lifetime.Token), "已开始新的会话。"), 1, 1, -168, -53, 141, 38);
            var scrollHost = Panel("Messages", chat, new Color(0, 0, 0, 0));
            Place(scrollHost, 0, 0, 1, 1, 25, 230, -25, -150);
            scroll = Scroll(scrollHost, out messageContent);
            emptyLabel = Label("Empty conversation", scrollHost,
                "留一点时间给自己。\n\n输入一句话，开始这段对话。\n当前演示使用固定回复与测试音频。", 21, Muted, 24, 0, 0, 0);
            Place(emptyLabel.rectTransform, 0, 0, 1, 1, 24, 0, -24, 0);
            emptyLabel.alignment = TextAlignmentOptions.Center;

            var composer = Panel("Composer", chat, Surface);
            Place(composer, 0, 0, 1, 0, 25, 76, -25, 215);
            var field = Panel("Text input", composer, new Color32(35, 45, 60, 255));
            Place(field, 0, 0, 1, 1, 12, 52, -12, -12);
            // TMP creates its caret renderer and scrolling hooks during OnEnable. Wire
            // the text and viewport while inactive so its first OnEnable sees both.
            field.gameObject.SetActive(false);
            input = field.gameObject.AddComponent<ChatInputField>();
            var textArea = Rect("Text viewport", field);
            Stretch(textArea, 12, 8, -12, -8);
            textArea.gameObject.AddComponent<RectMask2D>();
            var text = Label("Draft", textArea, "", 18, Ink, 0, 0, 0, 0);
            Stretch(text.rectTransform, 0, 0, 0, 0);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.overflowMode = TextOverflowModes.Overflow;
            var placeholder = Label("Placeholder", textArea, "想聊些什么？", 18, Muted, 0, 0, 0, 0);
            Stretch(placeholder.rectTransform, 0, 0, 0, 0);
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            input.textViewport = textArea;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = font;
            input.pointSize = 18;
            input.richText = false;
            input.characterLimit = 4000;
            input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, .35f);
            input.caretColor = Accent;
            input.customCaretColor = true;
            input.onValueChanged.AddListener(DraftEdited);
            input.SendRequested += Submit;
            field.gameObject.SetActive(true);
            Label("Keyboard hint", composer, "Enter 发送 · Shift+Enter 换行", 12, Muted, 14, 15, 320, 23);
            counter = Label("Length", composer, "0 / 2000", 12, Muted, 0, 15, 95, 23);
            Place(counter.rectTransform, 1, 0, 1, 0, -226, 15, -131, 38);
            send = ButtonAt(composer, "发送", Submit, 1, 0, -118, 12, 104, 32, true);

            stop = ButtonAt(chat, "停止 / Esc", () => session.Cancel(StopReason.User), 0, 0, 26, 21, 136, 38);
            autoRead = ButtonAt(chat, "自动朗读：开", ToggleAutoRead, 0, 0, 173, 21, 148, 38);
            autoReadLabel = autoRead.GetComponentInChildren<TextMeshProUGUI>();
            speechMode = Label("Speech mode", chat, "语音输入后续开放", 12, Muted, 335, 26, 320, 28);

            var audioPanel = Panel("Volume", canvas, new Color32(27, 35, 48, 255));
            Place(audioPanel, 0, 0, .42f, 0, 24, 128, -24, 184);
            mute = ButtonAt(audioPanel, "静音", ToggleMute, 0, .5f, 9, -18, 67, 36);
            muteLabel = mute.GetComponentInChildren<TextMeshProUGUI>();
            volumeLabel = Label("Volume percent", audioPanel, "80%", 13, Ink, 0, 16, 60, 24);
            Place(volumeLabel.rectTransform, 1, 0, 1, 0, -55, 16, -6, 40);
            var sliderRoot = Rect("Volume slider", audioPanel);
            Place(sliderRoot, 0, .5f, 1, .5f, 90, -14, -68, 14);
            volume = sliderRoot.gameObject.AddComponent<Slider>();
            var rail = Panel("Track", sliderRoot, new Color32(55, 67, 85, 255));
            Place(rail, 0, .5f, 1, .5f, 0, -3, 0, 3);
            var fill = Panel("Fill", rail, Accent); Stretch(fill, 0, 0, 0, 0);
            var handleArea = Rect("Handle area", sliderRoot); Stretch(handleArea, 5, 0, -5, 0);
            var handle = Panel("Handle", handleArea, Ink);
            // Slider stretches the handle's vertical anchors across its 28px area.
            // A -8px inset produces a 20px thumb instead of an unintended 48px block.
            handle.sizeDelta = new Vector2(12, -8);
            volume.fillRect = fill;
            volume.handleRect = handle;
            volume.targetGraphic = handle.GetComponent<Image>();
            volume.minValue = 0; volume.maxValue = 1;
            volume.onValueChanged.AddListener(v => { if (!syncing) ShowResult(session.SetVolume(v)); });

            footer = Label("Feedback", canvas, "本机历史仅保存在这台电脑；语音输入尚未开放。", 12, Muted, 27, 4, 480, 20);
            footer.enableWordWrapping = false;
            footer.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void Render(SessionSnapshot value)
        {
            if (!this || !initialized && input == null) return;
            snapshot = value;
            syncing = true;
            bool conversationChanged = shownConversation != value.ConversationId;
            shownConversation = value.ConversationId;
            if (conversationChanged || !input.isFocused)
                input.SetTextWithoutNotify(value.DraftText);
            volume.SetValueWithoutNotify(value.Settings.Volume01);
            if (value.Settings.Volume01 > 0) lastAudibleVolume = value.Settings.Volume01;
            volumeLabel.text = Mathf.RoundToInt(value.Settings.Volume01 * 100) + "%";
            muteLabel.text = value.Settings.Volume01 == 0 ? "恢复" : "静音";
            autoReadLabel.text = value.Settings.AutoRead ? "自动朗读：开" : "自动朗读：关";
            mode.text = ModeText(value.Mode) + "  ·  声音：" + SpeechText(value.TtsMode);
            mode.color = value.Mode == PreviewMode.Cloud ? Accent : Warning;
            state.text = PhaseText(value.Phase);
            if (value.Error != null) state.text += "  ·  " + value.Error.Message;
            state.color = value.Error == null ? Accent : Warning;
            stop.interactable = value.Operation.HasValue || value.Phase == SessionPhase.Recording || value.Phase == SessionPhase.Transcribing;
            speechMode.text = !value.Settings.AutoRead ? "纯文字模式 · 不请求声音" :
                value.TtsMode == SpeechMode.Fixture ? "测试音频不对应回复正文" :
                value.TtsMode == SpeechMode.System ? "使用系统语音朗读下次回复" :
                value.TtsMode == SpeechMode.Cloud ? "使用云端声音朗读下次回复" : "声音未就绪，请刷新后台状态";
            if (value.Settings.SaveState == SettingsSaveState.Failed)
                Feedback(value.Settings.SaveError?.Message ?? "设置暂未保存，请检查磁盘权限。", true);
            UpdateCounter();
            RenderMessages(value, conversationChanged);
            if (modalKind == "history" && shownRevision != value.HistoryCapacity.StoreRevision)
            { shownRevision = value.HistoryCapacity.StoreRevision; RefreshHistory(); }
            syncing = false;
        }

        private void RenderMessages(SessionSnapshot value, bool forceScroll)
        {
            bool changed = forceScroll;
            bool nearBottom = messageContent.rect.height <= scroll.viewport.rect.height + 1 || scroll.verticalNormalizedPosition < .08f;
            for (int i = 0; i < value.Messages.Count; i++)
            {
                if (i == rows.Count) rows.Add(CreateMessage());
                var row = rows[i]; var message = value.Messages[i];
                row.Root.gameObject.SetActive(true);
                var previous = row.LastMessage;
                if (ReferenceEquals(previous, message) || previous != null && previous.Operation == message.Operation &&
                    previous.Role == message.Role && previous.DeliveryState == message.DeliveryState && previous.Text == message.Text &&
                    previous.Error?.Code == message.Error?.Code && previous.Error?.Message == message.Error?.Message) continue;
                row.LastMessage = message;
                bool user = message.Role == MessageRole.User;
                row.Heading.text = user ? "你" : "SAKI   ·   " + DeliveryText(message.DeliveryState) + (message.Mode == PreviewMode.Fixture ? "   ·   演示" : "");
                row.Heading.color = user ? Accent : Muted;
                row.Body.text = string.IsNullOrEmpty(message.Text) ? "正在等待回复…" : message.Text;
                if (message.Error != null) row.Body.text += "\n\n" + message.Error.Message;
                row.Background.color = user ? new Color32(31, 55, 62, 255) : Surface;
                changed = true;
            }
            for (int i = value.Messages.Count; i < rows.Count; i++)
            { if (rows[i].Root.gameObject.activeSelf) { rows[i].Root.gameObject.SetActive(false); changed = true; } }
            emptyLabel.gameObject.SetActive(value.Messages.Count == 0);
            if (changed && (nearBottom || forceScroll))
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0;
            }
        }

        private MessageRow CreateMessage()
        {
            var root = Panel("Message", messageContent, Surface);
            var group = root.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(16, 16, 13, 15);
            group.spacing = 8;
            group.childControlWidth = true; group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            var heading = Label("Sender", root, "", 12, Muted, 0, 0, 0, 0);
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 21;
            var body = Label("Message text", root, "", 18, Ink, 0, 0, 0, 0);
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;
            return new MessageRow { Root = root, Heading = heading, Body = body, Background = root.GetComponent<Image>() };
        }

        private void DraftEdited(string text)
        {
            if (syncing || session == null) return;
            session.NotifyDraftEdited(text);
            UpdateCounter();
        }
        private void ApplyDraft(DraftUpdate update)
        { if (!this) return; input.SetTextWithoutNotify(update.Text); UpdateCounter(); input.ActivateInputField(); }
        private void UpdateCounter()
        {
            int count = EnterPolicy.CountScalars(input.text);
            counter.text = count <= 4000 ? count + " / 2000" : "无效字符";
            counter.color = count > 2000 ? Warning : Muted;
            send.interactable = !localBusy && count > 0 && count <= 2000 && !string.IsNullOrWhiteSpace(input.text) && snapshot != null && snapshot.Mode != PreviewMode.Unknown;
        }
        private void Submit()
        {
            if (session == null || !send.interactable || lastSendFrame == Time.frameCount || !string.IsNullOrEmpty(Input.compositionString)) return;
            lastSendFrame = Time.frameCount;
            var settings = session.Snapshot.Settings;
            var result = session.SubmitText(new SubmitTextCommand(input.text.Trim(), characterId,
                settings.AutoRead ? settings.VoiceId : null, settings.AutoRead));
            if (!result.Accepted) { Feedback(result.Error.Message, true); return; }
            input.SetTextWithoutNotify("");
            session.NotifyDraftEdited("");
            UpdateCounter();
            input.ActivateInputField();
        }
        private void ToggleAutoRead()
        {
            var s = session.Snapshot.Settings;
            ShowResult(session.UpdateSettings(new UpdateSettingsCommand(!s.AutoRead, s.VoiceId, s.ContinuePlaybackOnFocusLost, s.MicrophoneDeviceId)));
        }
        private void ToggleMute() => ShowResult(session.SetVolume(snapshot.Settings.Volume01 == 0 ? lastAudibleVolume : 0));
        private void ShowResult(LocalCommandResult result) { if (!result.Accepted) Feedback(result.Error.Message, true); }
        private void Update()
        {
            if (!initialized) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            { session.Cancel(StopReason.User); if (modal != null) CloseModal(); }
            if (Input.GetKeyDown(KeyCode.Tab) && string.IsNullOrEmpty(Input.compositionString))
            {
                var options = new List<Selectable>();
                foreach (var selectable in Selectable.allSelectablesArray)
                    if (selectable.IsActive() && selectable.IsInteractable() && selectable.navigation.mode != Navigation.Mode.None &&
                        (modal == null ? selectable.transform.IsChildOf(canvas) : selectable.transform.IsChildOf(modal)))
                        options.Add(selectable);
                if (options.Count == 0) return;
                var current = EventSystem.current.currentSelectedGameObject;
                int index = options.FindIndex(s => s.gameObject == current);
                int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                index = (index + direction + options.Count) % options.Count;
                options[index].Select();
                if (options[index] == input) input.ActivateInputField();
            }
        }

        private void OpenHistory()
        {
            OpenModal("本机历史", "history");
            Label("Retention", modal, "最多 20 个会话，每个 80 条消息；满额清理最旧的非当前会话。", 13, Muted, 28, -87, 650, 38, false, true);
            var list = Rect("History list", modal); Place(list, 0, 0, 1, 1, 25, 82, -25, -135);
            Scroll(list, out modalContent);
            ButtonAt(modal, "清除全部历史", ConfirmClear, 0, 0, 27, 24, 175, 38);
            ButtonAt(modal, "刷新列表", RefreshHistory, 1, 0, -158, 24, 131, 38);
            RefreshHistory();
        }
        private async void RefreshHistory()
        {
            if (modalKind != "history" || modalContent == null) return;
            int version = ++historyVersion;
            try
            {
                var result = await session.ListConversationsAsync(new ConversationListQuery(20, null), lifetime.Token);
                if (!this || lifetime.IsCancellationRequested || version != historyVersion || modalKind != "history") return;
                ClearChildren(modalContent);
                if (!result.Succeeded) { ModalLine(result.Error.Message, Warning); return; }
                if (result.Value.Items.Count == 0) { ModalLine("这里还没有会话。", Muted); return; }
                foreach (var item in result.Value.Items)
                {
                    var row = Panel("Conversation item", modalContent, Surface);
                    row.gameObject.AddComponent<LayoutElement>().preferredHeight = 95;
                    string prefix = item.ConversationId == snapshot.ConversationId ? "当前 · " : "";
                    var title = Label("Title", row, prefix + item.Title, 16, Ink, 14, 47, 430, 32);
                    title.overflowMode = TextOverflowModes.Ellipsis;
                    Label("Metadata", row, item.UpdatedAtUtc.ToLocalTime().ToString("MM月dd日 HH:mm") + "  ·  " + item.MessageCount + " 条", 12, Muted, 14, 17, 300, 24);
                    Guid id = item.ConversationId;
                    ButtonAt(row, "打开", () => { CloseModal(); RunLocal(() => session.SelectConversationAsync(id, lifetime.Token), "已打开本机会话。"); }, 1, 0, -241, 13, 67, 32);
                    ButtonAt(row, "导出", () => Export(id), 1, 0, -164, 13, 67, 32);
                    ButtonAt(row, "删除", () => ConfirmDelete(id), 1, 0, -87, 13, 67, 32);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (this) Feedback("无法读取本机历史，请检查磁盘权限。", true); }
        }

        private void ConfirmDelete(Guid id)
        {
            OpenModal("删除这个会话？", "confirm");
            Label("Explanation", modal, "删除后无法恢复。当前回复会先停止，旧回复不会重新写回。", 18, Ink, 28, -144, 640, 70, false, true);
            ButtonAt(modal, "保留会话", OpenHistory, 0, 0, 28, 28, 156, 42);
            ButtonAt(modal, "确认删除", () => { CloseModal(); RunLocal(() => session.DeleteConversationAsync(id, lifetime.Token), "会话已删除。"); }, 1, 0, -187, 28, 159, 42, true);
        }
        private void ConfirmClear()
        {
            OpenModal("清除全部本机历史？", "confirm");
            Label("Explanation", modal, "全部聊天记录将从这台电脑删除，设置会保留。\n此操作无法恢复。已手工导出的文件需自行删除。", 18, Ink, 28, -156, 640, 90, false, true);
            ButtonAt(modal, "保留历史", OpenHistory, 0, 0, 28, 28, 156, 42);
            ButtonAt(modal, "确认清除", () => { CloseModal(); RunLocal(() => session.ClearHistoryAsync(lifetime.Token), "全部本机历史已清除，设置已保留。"); }, 1, 0, -187, 28, 159, 42, true);
        }

        private async void Export(Guid id)
        {
            if (localBusy) return;
            localBusy = true; UpdateCounter();
            try
            {
                var result = await session.ExportConversationAsync(id, lifetime.Token);
                if (!this || lifetime.IsCancellationRequested) return;
                if (!result.Succeeded) { Feedback(result.Error.Message, true); return; }
                if (exportSink != null) { exportSink(result.Value); Feedback("会话已导出。", false); }
                else
                {
                    string path = HistoryFileExport.Save(result.Value);
                    if (path != null) Feedback("会话已导出到所选文件。", false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Never log the selected path, conversation content, or an exception message
                // that could contain either; type and numeric HRESULT are enough to diagnose ABI failures.
                Debug.LogWarning("HISTORY_EXPORT_FAILED type=" + ex.GetType().Name + " hresult=" + ex.HResult);
                if (this) Feedback("无法导出，请检查目标目录与磁盘空间。", true);
            }
            finally { localBusy = false; if (this && input != null) UpdateCounter(); }
        }

        private void OpenSettings()
        {
            OpenModal("声音与本机设置", "settings");
            var s = session.Snapshot.Settings;
            Label("Output", modal, "播放设备：" + s.PlaybackDeviceLabel, 18, Ink, 28, -127, 640, 36, false, true);
            Label("Output note", modal, "设备通过 Windows 系统声音设置切换。", 13, Muted, 28, -162, 640, 30, false, true);
            ButtonAt(modal, s.AutoRead ? "自动朗读：开启" : "自动朗读：关闭", () => { ToggleAutoRead(); OpenSettings(); }, 0, 1, 28, -226, 280, 42);
            ButtonAt(modal, s.ContinuePlaybackOnFocusLost ? "失焦后继续播放：开" : "失焦后继续播放：关", () =>
            {
                var current = session.Snapshot.Settings;
                ShowResult(session.UpdateSettings(new UpdateSettingsCommand(current.AutoRead, current.VoiceId, !current.ContinuePlaybackOnFocusLost, current.MicrophoneDeviceId)));
                OpenSettings();
            }, 0, 1, 28, -284, 350, 42);
            string voiceName = "默认音色";
            if (s.VoiceId != null)
            {
                voiceName = s.VoiceId + "（已保存，待确认）";
                foreach (var voice in snapshot.VoiceOptions.Items) if (voice.Id == s.VoiceId) voiceName = voice.DisplayName;
            }
            Label("Voice", modal, "声音：" + voiceName, 16, Ink, 28, -342, 625, 42, false, true);
            ButtonAt(modal, "选择声音", OpenVoiceOptions, 0, 1, 28, -394, 185, 38);
            ButtonAt(modal, "刷新后台状态", RefreshCapabilities, 0, 1, 229, -394, 205, 38);
            Label("Privacy", modal, "麦克风：尚未开放，不会自动录音。\n历史保存在本机，与声音设置分开存储。\n本机历史不等于长期记忆。", 14, Muted, 28, -500, 640, 88, false, true);
            string saved = s.SaveState == SettingsSaveState.Saved ? "设置已保存" : s.SaveState == SettingsSaveState.Pending ? "正在保存设置…" : "设置保存失败";
            Label("Save state", modal, saved, 13, s.SaveState == SettingsSaveState.Failed ? Warning : Accent, 28, 28, 640, 30);
        }
        private void OpenVoiceOptions()
        {
            OpenModal("选择下次回复的声音", "voices");
            var list = Rect("Voice choices", modal); Place(list, 0, 0, 1, 1, 28, 28, -28, -99);
            Scroll(list, out modalContent);
            VoiceButton(null, "后台默认声音");
            if (snapshot.VoiceOptions.State == VoiceOptionsState.Ready)
                foreach (var voice in snapshot.VoiceOptions.Items) VoiceButton(voice.Id, voice.DisplayName);
            else ModalLine("声音列表暂不可用，请在设置中刷新后台状态。", Warning);
        }
        private void VoiceButton(string id, string title)
        {
            var b = CreateButton(modalContent, title, () =>
            {
                var s = session.Snapshot.Settings;
                ShowResult(session.UpdateSettings(new UpdateSettingsCommand(s.AutoRead, id, s.ContinuePlaybackOnFocusLost, s.MicrophoneDeviceId)));
                OpenSettings();
            }, false);
            b.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
        }
        private async void RefreshCapabilities()
        {
            if (localBusy) return;
            localBusy = true;
            try
            {
                var result = await session.RefreshVoiceOptionsAsync(lifetime.Token);
                if (!this || lifetime.IsCancellationRequested) return;
                Feedback(result.Succeeded ? "后台状态已刷新。" : result.Error.Message, !result.Succeeded);
                if (modalKind == "settings") OpenSettings();
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (this) Feedback("后台暂不可用，请检查启动器。", true); }
            finally { localBusy = false; if (this) UpdateCounter(); }
        }

        private async void RunLocal(Func<Task> operation, string success)
        {
            if (localBusy) return;
            localBusy = true; UpdateCounter();
            try { await operation(); if (this && !lifetime.IsCancellationRequested) Feedback(success, false); }
            catch (OperationCanceledException) { }
            catch (Exception) { if (this) Feedback("操作未完成，请检查本机存储后重试。", true); }
            finally { localBusy = false; if (this && input != null) UpdateCounter(); }
        }
        public void ShowNotice(string text, bool warning = false) { if (footer != null) Feedback(text, warning); }
        private void Feedback(string text, bool warning) { footer.text = text; footer.color = warning ? Warning : Muted; }
        private void OpenModal(string title, string kind)
        {
            CloseModal();
            var shade = Panel("Modal shade", canvas, new Color(0, 0, 0, .68f)); Stretch(shade, 0, 0, 0, 0);
            var shadeButton = shade.gameObject.AddComponent<Button>();
            shadeButton.navigation = new Navigation { mode = Navigation.Mode.None };
            shadeButton.onClick.AddListener(CloseModal);
            modal = Panel("Dialog", shade, Background);
            Place(modal, .5f, .5f, .5f, .5f, -350, -295, 350, 295);
            // A blocker on the dialog keeps clicks inside it from reaching the shade button.
            var blocker = modal.gameObject.AddComponent<Button>();
            blocker.navigation = new Navigation { mode = Navigation.Mode.None };
            blocker.onClick.AddListener(() => { });
            modalKind = kind;
            Label("Dialog title", modal, title, 24, Ink, 28, -65, 590, 42, true, true);
            ButtonAt(modal, "关闭", CloseModal, 1, 1, -98, -63, 70, 36);
        }
        private void CloseModal()
        {
            historyVersion++;
            if (modal != null) { modal.parent.gameObject.SetActive(false); Destroy(modal.parent.gameObject); }
            modal = null; modalContent = null; modalKind = null;
        }
        private void ModalLine(string text, Color color)
        {
            var label = Label("Information", modalContent, text, 16, color, 0, 0, 0, 0);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
        }

        private RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
            return obj.GetComponent<RectTransform>();
        }
        private RectTransform Panel(string name, Transform parent, Color color)
        { var rect = Rect(name, parent); rect.gameObject.AddComponent<Image>().color = color; return rect; }
        private TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, float x, float y, float width, float height, bool bold = false, bool top = false)
        {
            var rect = Rect(name, parent);
            Place(rect, 0, top ? 1 : 0, 0, top ? 1 : 0, x, y, x + width, y + height);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = size; label.color = color;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.richText = false; label.raycastTarget = false;
            label.enableWordWrapping = true;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }
        private Button CreateButton(Transform parent, string text, Action action, bool primary)
        {
            var rect = Panel(text, parent, primary ? Accent : new Color32(44, 55, 72, 255));
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(.75f, .75f, .75f);
            colors.disabledColor = new Color(.6f, .6f, .6f, .65f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            var label = Label("Label", rect, text, 14, primary ? Background : Ink, 0, 0, 0, 0);
            Stretch(label.rectTransform, 5, 0, -5, 0);
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }
        private Button ButtonAt(Transform parent, string text, Action action, float anchorX, float anchorY, float x, float y, float width, float height, bool primary = false)
        {
            var button = CreateButton(parent, text, action, primary);
            Place(button.GetComponent<RectTransform>(), anchorX, anchorY, anchorX, anchorY, x, y, x + width, y + height);
            return button;
        }
        private ScrollRect Scroll(RectTransform host, out RectTransform content)
        {
            var viewport = Panel("Viewport", host, new Color(0, 0, 0, 0)); Stretch(viewport, 0, 0, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", viewport);
            Place(content, 0, 1, 1, 1, 0, 0, 0, 0); content.pivot = new Vector2(.5f, 1);
            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = 12; group.padding = new RectOffset(0, 0, 0, 12);
            group.childControlWidth = true; group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var value = host.gameObject.AddComponent<ScrollRect>();
            value.viewport = viewport; value.content = content; value.horizontal = false; value.vertical = true;
            value.movementType = ScrollRect.MovementType.Clamped; value.scrollSensitivity = 35;
            return value;
        }
        private static void Place(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top)
        { rect.anchorMin = new Vector2(minX, minY); rect.anchorMax = new Vector2(maxX, maxY); rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(right, top); }
        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top) => Place(rect, 0, 0, 1, 1, left, bottom, right, top);
        private static void ClearChildren(Transform parent)
        { for (int i = parent.childCount - 1; i >= 0; i--) { parent.GetChild(i).gameObject.SetActive(false); Destroy(parent.GetChild(i).gameObject); } }
        private static string ModeText(PreviewMode value) => value == PreviewMode.Fixture ? "演示模式 · 固定回复" : value == PreviewMode.Cloud ? "云端对话" : "未就绪 · 等待本机后台";
        private static string SpeechText(SpeechMode value) => value == SpeechMode.Fixture ? "测试音频" : value == SpeechMode.System ? "系统语音" : value == SpeechMode.Cloud ? "云端朗读" : "未就绪";
        private static string DeliveryText(DeliveryState value)
        {
            switch (value)
            {
                case DeliveryState.Generating: return "生成中";
                case DeliveryState.Generated: return "已生成 · 尚未播完";
                case DeliveryState.Displayed: return "已显示";
                case DeliveryState.Played: return "已播完";
                case DeliveryState.Interrupted: return "已中断";
                default: return "失败";
            }
        }
        private static string PhaseText(SessionPhase value)
        {
            switch (value)
            {
                case SessionPhase.Ready: return "准备好了，随时开始";
                case SessionPhase.Thinking: return "正在生成回复…";
                case SessionPhase.PreparingSpeech: return "文字已生成，正在准备声音…";
                case SessionPhase.Speaking: return "正在播放 · 可以随时停止";
                case SessionPhase.Stopping: return "已在本机停止，正在清理…";
                case SessionPhase.Recording: return "正在录音";
                case SessionPhase.Transcribing: return "正在转写，请等待确认";
                case SessionPhase.Error: return "暂时遇到问题";
                default: return "后台未连接 · 在设置中刷新状态";
            }
        }
        private void OnDestroy()
        {
            lifetime.Cancel();
            if (session != null) { session.SnapshotChanged -= Render; session.DraftUpdated -= ApplyDraft; }
            if (input != null) input.SendRequested -= Submit;
            lifetime.Dispose();
        }
    }
}
