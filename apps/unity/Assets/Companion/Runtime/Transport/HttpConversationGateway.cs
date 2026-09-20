using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.Transport
{
    public sealed class HttpConversationGateway : IConversationGateway
    {
        private const string Prefix = "/preview/unity/v1";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly HttpClient _client;
        private readonly Uri _baseUri;
        private readonly string _token;
        private readonly Func<string> _tokenProvider;
        private readonly Func<ReadOnlyMemory<byte>, AudioReadyDescriptor, CancellationToken, Task<ValidatedPcm>> _validator;
        private readonly object _identityGate = new object();
        private OperationKey? _active;
        private AudioReadyDescriptor _allowedAudio;
        /// <summary>QA diagnostic after validated audio response headers, before reading its bounded body.</summary>
        public event Action<TurnKey> AudioDownloadStarted;

        public HttpConversationGateway(Uri baseUri, string bearerToken, Func<ReadOnlyMemory<byte>, AudioReadyDescriptor, CancellationToken, Task<ValidatedPcm>> validateWav, Func<string> tokenProvider = null)
        {
            if (baseUri == null || baseUri.Scheme != "http" || baseUri.Host != "127.0.0.1" || baseUri.Port != 8000 || baseUri.AbsolutePath != "/" || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) || !string.IsNullOrEmpty(baseUri.UserInfo))
                throw new ArgumentException("U01 permits only http://127.0.0.1:8000.", nameof(baseUri));
            _baseUri = baseUri; _token = bearerToken; _validator = validateWav ?? throw new ArgumentNullException(nameof(validateWav)); _tokenProvider = tokenProvider;
            _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public async Task<PreviewCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            using (var time = Deadline(cancellationToken, 15))
            using (var request = Request(HttpMethod.Get, Prefix + "/capabilities", null))
            using (var response = await Send(request, time.Token, null))
            {
                await CheckStatus(response, null, time.Token);
                var root = Object(await ReadJson(response, 65536, time.Token));
                Fields(root, "protocol", "chat_mode", "tts_mode", "asr_mode", "chat", "tts", "asr", "character_ids", "voices", "limits");
                if (String(root, "protocol") != PreviewProtocol.Name) throw Protocol();
                var chat = Object(root["chat"]); var tts = Object(root["tts"]); var asr = Object(root["asr"]);
                Fields(chat, "configured", "available"); Fields(tts, "configured", "available"); Fields(asr, "configured", "available");
                var chars = new List<string>();
                foreach (var id in List(root, "character_ids")) { if (!(id is string value) || UnicodeLength(value) < 1 || UnicodeLength(value) > 64 || chars.Contains(value)) throw Protocol(); chars.Add(value); }
                if (chars.Count == 0 || chars.Count > 128) throw Protocol();
                var voices = new List<VoiceOption>(); var seen = new HashSet<string>();
                foreach (var item in List(root, "voices"))
                {
                    var voice = Object(item); Fields(voice, "id", "display_name", "mode");
                    string id = String(voice, "id", 64); if (!seen.Add(id)) throw Protocol();
                    Speech(String(voice, "mode")); voices.Add(new VoiceOption(id, String(voice, "display_name", 128)));
                }
                var limits = Object(root["limits"]);
                string[] names = { "max_input_characters", "max_reply_characters", "max_history_messages", "max_context_characters", "max_request_bytes", "max_event_bytes", "max_output_wav_bytes", "max_output_seconds", "max_capture_wav_bytes", "max_capture_seconds" };
                int[] expected = { 2000, 400, 6, 12000, 65536, 65536, 8388608, 120, 1048576, 30 };
                Fields(limits, names);
                for (int i = 0; i < names.Length; i++) if (Number(limits, names[i], expected[i], expected[i]) != expected[i]) throw Protocol();
                return new PreviewCapabilities(PreviewProtocol.Name,
                    new ServiceCapability(Mode(String(root, "chat_mode")), Bool(chat, "configured"), Bool(chat, "available")),
                    new SpeechCapability(Speech(String(root, "tts_mode")), Bool(tts, "configured"), Bool(tts, "available")),
                    new ServiceCapability(Mode(String(root, "asr_mode")), Bool(asr, "configured"), Bool(asr, "available")), chars, voices,
                    new PreviewLimits(2000, 400, 6, 12000, 65536, 65536, 8388608, 120, 1048576, 30));
            }
        }

        public async Task ConsumeTurnAsync(TurnSubmission submission, Func<PreviewEvent, CancellationToken, Task> onEvent, CancellationToken cancellationToken)
        {
            ValidateSubmission(submission);
            lock (_identityGate) { _active = submission.Operation; _allowedAudio = null; }
            var history = new List<object>();
            foreach (var row in submission.History) history.Add(Map("role", row.Role == MessageRole.User ? "user" : "assistant", "text", row.Text));
            var payload = Map("protocol", PreviewProtocol.Name, "request_id", submission.Operation.RequestId.ToString("D"), "generation", submission.Operation.Generation,
                "conversation_id", submission.Operation.ConversationId.ToString("D"), "character_id", submission.CharacterId, "text", submission.Text, "history", history,
                "generate_audio", submission.GenerateAudio, "voice_id", submission.VoiceId);
            byte[] body = Utf8.GetBytes(StrictJson.Encode(payload));
            if (body.Length > 65536) throw Error("invalid_request", submission.Operation);
            using (var time = Deadline(cancellationToken, 90))
            using (var request = Request(HttpMethod.Post, Prefix + "/turns", body))
            using (var response = await Send(request, time.Token, submission.Operation))
            {
                await CheckStatus(response, submission.Operation, time.Token);
                if (response.Content.Headers.ContentType?.MediaType != "application/x-ndjson") throw Protocol(submission.Operation);
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var decoder = new EventDecoder(submission);
                    var line = new MemoryStream(1024);
                    byte[] buffer = new byte[4096]; int total = 0;
                    try
                    {
                        while (true)
                        {
                            int count;
                            using (var idle = Deadline(time.Token, 30)) count = await ReadWithCancellationAsync(stream, buffer, idle.Token);
                            if (count == 0) break;
                            total += count; if (total > 1024 * 1024) throw Protocol(submission.Operation);
                            for (int i = 0; i < count; i++)
                            {
                                if (buffer[i] != 10)
                                {
                                    if (line.Length >= 65536) throw Protocol(submission.Operation);
                                    line.WriteByte(buffer[i]); continue;
                                }
                                if (line.Length == 0) throw Protocol(submission.Operation);
                                string raw = Utf8.GetString(line.GetBuffer(), 0, (int)line.Length); line.SetLength(0);
                                PreviewEvent parsed = decoder.Accept(raw);
                                if (parsed == null) continue;
                                time.Token.ThrowIfCancellationRequested();
                                lock (_identityGate)
                                {
                                    if (_active != submission.Operation) throw new OperationCanceledException(time.Token);
                                    if (parsed is AudioReadyEvent audio) _allowedAudio = audio.Audio;
                                }
                                await onEvent(parsed, time.Token);
                            }
                        }
                        if (line.Length != 0 || !decoder.Terminal) throw Protocol(submission.Operation);
                    }
                    catch (DecoderFallbackException) { throw Protocol(submission.Operation); }
                    finally { line.Dispose(); Array.Clear(buffer, 0, buffer.Length); }
                }
            }
        }

        public async Task<ValidatedPcm> DownloadAudioAsync(AudioReadyDescriptor audio, CancellationToken cancellationToken)
        {
            VerifyAudio(audio);
            using (var time = Deadline(cancellationToken, 15))
            using (var request = Request(HttpMethod.Get, audio.Path, null))
            using (var response = await Send(request, time.Token, audio.Turn.Operation))
            {
                await CheckStatus(response, audio.Turn.Operation, time.Token);
                if (response.Content.Headers.ContentType?.MediaType != "audio/wav") throw Error("invalid_audio", audio.Turn.Operation);
                if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw Error("invalid_audio", audio.Turn.Operation);
                time.Token.ThrowIfCancellationRequested(); VerifyAudio(audio);
                AudioDownloadStarted?.Invoke(audio.Turn);
                byte[] wav = await ReadBytes(response, 8 * 1024 * 1024, time.Token);
                ValidatedPcm pcm = null;
                try
                {
                    pcm = await _validator(wav, audio, time.Token);
                    time.Token.ThrowIfCancellationRequested(); VerifyAudio(audio);
                    if (pcm == null || pcm.IsDisposed || pcm.TotalSamples != audio.TotalSamples) throw Error("invalid_audio", audio.Turn.Operation);
                    var result = pcm; pcm = null; return result;
                }
                finally { pcm?.Dispose(); Array.Clear(wav, 0, wav.Length); }
            }
        }

        public async Task<CancelResult> CancelAsync(OperationKey operation, CancellationToken cancellationToken)
        {
            lock (_identityGate) if (_active == operation) { _active = null; _allowedAudio = null; }
            using (var time = Deadline(cancellationToken, 5))
            using (var request = Request(HttpMethod.Post, Prefix + "/requests/" + operation.RequestId.ToString("D") + "/cancel", Utf8.GetBytes(StrictJson.Encode(Map("generation", operation.Generation)))))
            using (var response = await Send(request, time.Token, operation))
            {
                await CheckStatus(response, operation, time.Token);
                var result = await ReadJson(response, 65536, time.Token); Fields(result, "request_id", "cancelled");
                if (Uuid(result, "request_id") != operation.RequestId || !Bool(result, "cancelled")) throw Protocol(operation);
                return new CancelResult(operation.RequestId, true);
            }
        }

        public Task<TranscriptionResult> TranscribeAsync(OperationKey operation, CapturedPcm recording, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Error("microphone_unavailable", operation);
        }

        private void VerifyAudio(AudioReadyDescriptor audio)
        {
            if (audio == null || audio.Path != Prefix + "/turns/" + audio.Turn.TurnId.ToString("D") + "/audio.wav" || audio.SampleRate != 24000 || audio.Channels != 1 || audio.Codec != "pcm_s16le" || audio.TotalSamples < 1 || audio.TotalSamples > 2880000) throw Error("invalid_audio", audio?.Turn.Operation);
            lock (_identityGate)
                if (_active != audio.Turn.Operation || _allowedAudio == null || _allowedAudio.Turn != audio.Turn || _allowedAudio.Path != audio.Path || _allowedAudio.TotalSamples != audio.TotalSamples)
                    throw new OperationCanceledException("Stale audio operation.");
        }

        private HttpRequestMessage Request(HttpMethod method, string path, byte[] body)
        {
            string token = _tokenProvider != null ? _tokenProvider() : _token;
            if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.IndexOfAny(new[] { '\r', '\n', ' ' }) >= 0) throw Error("unauthorized", null);
            var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) { request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); }
            return request;
        }

        private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken token, OperationKey? operation)
        {
            try { return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); }
            catch (HttpRequestException) { throw Error("backend_unavailable", operation); }
        }
        private async Task CheckStatus(HttpResponseMessage response, OperationKey? operation, CancellationToken token)
        {
            if ((int)response.StatusCode == 200) return;
            if (response.IsSuccessStatusCode) throw Protocol(operation);
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw Protocol(operation);
            var root = await ReadJson(response, 65536, token);
            Fields(root, "code", "message", "retryable", "request_id");
            string code = String(root, "code", 64); String(root, "message", 2048); bool retryable = Bool(root, "retryable");
            if (root["request_id"] != null && (!operation.HasValue || Uuid(root, "request_id") != operation.Value.RequestId)) throw Protocol(operation);
            throw Error(code, operation, retryable);
        }
        private static async Task<Dictionary<string, object>> ReadJson(HttpResponseMessage response, int max, CancellationToken token)
        {
            if (response.Content.Headers.ContentType?.MediaType != "application/json") throw Protocol();
            try { return StrictJson.Parse(Utf8.GetString(await ReadBytes(response, max, token))); }
            catch (DecoderFallbackException) { throw Protocol(); }
        }
        private static async Task<byte[]> ReadBytes(HttpResponseMessage response, int max, CancellationToken token)
        {
            if (response.Content.Headers.ContentLength > max) throw Protocol();
            using (var input = await response.Content.ReadAsStreamAsync())
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = await ReadWithCancellationAsync(input, buffer, token)) != 0)
                { if (output.Length + count > max) throw Protocol(); output.Write(buffer, 0, count); }
                token.ThrowIfCancellationRequested(); return output.ToArray();
            }
        }
        private static async Task<int> ReadWithCancellationAsync(Stream input, byte[] buffer, CancellationToken token)
        {
            // Some Unity/Mono HTTP streams do not interrupt a pending read on token cancellation.
            // Closing only this response stream guarantees idle/total deadlines and disconnect cleanup.
            using (token.Register(() => { try { input.Dispose(); } catch { } }))
            {
                try { return await input.ReadAsync(buffer, 0, buffer.Length, token); }
                catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            }
        }
        private static CancellationTokenSource Deadline(CancellationToken token, int seconds) { var source = CancellationTokenSource.CreateLinkedTokenSource(token); source.CancelAfter(TimeSpan.FromSeconds(seconds)); return source; }
        private static void ValidateSubmission(TurnSubmission s)
        {
            if (s == null || s.Operation.RequestId == Guid.Empty || s.Operation.ConversationId == Guid.Empty || s.Operation.RequestId.ToString("D")[14] != '4' || string.IsNullOrWhiteSpace(s.Text) || UnicodeLength(s.Text) > 2000 || s.CharacterId.Length < 1 || UnicodeLength(s.CharacterId) > 64 || (s.VoiceId != null && (s.VoiceId.Length < 1 || UnicodeLength(s.VoiceId) > 64))) throw Error("invalid_request", s?.Operation);
            int total = UnicodeLength(s.Text);
            foreach (var row in s.History)
            { if ((row.Role != MessageRole.User && row.Role != MessageRole.Assistant) || string.IsNullOrEmpty(row.Text) || UnicodeLength(row.Text) > 2000) throw Error("invalid_request", s.Operation); total += UnicodeLength(row.Text); }
            if (total > 12000) throw Error("invalid_request", s.Operation);
        }

        internal static int UnicodeLength(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            { if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || ++i >= text.Length || !char.IsLowSurrogate(text[i]))) throw Protocol(); count++; }
            return count;
        }
        internal static Dictionary<string, object> Map(params object[] values)
        { var map = new Dictionary<string, object>(); for (int i = 0; i < values.Length; i += 2) map.Add((string)values[i], values[i + 1]); return map; }
        internal static Dictionary<string, object> Object(object value) => value as Dictionary<string, object> ?? throw Protocol();
        internal static void Fields(Dictionary<string, object> map, params string[] names)
        { if (map.Count != names.Length) throw Protocol(); foreach (string name in names) if (!map.ContainsKey(name)) throw Protocol(); }
        internal static string String(Dictionary<string, object> map, string key, int max = 128)
        { if (!(map[key] is string s) || UnicodeLength(s) < 1 || UnicodeLength(s) > max) throw Protocol(); return s; }
        internal static bool Bool(Dictionary<string, object> map, string key) => map[key] is bool b ? b : throw Protocol();
        internal static long Number(Dictionary<string, object> map, string key, long min, long max)
        { if (!(map[key] is long n) || n < min || n > max) throw Protocol(); return n; }
        internal static Guid Uuid(Dictionary<string, object> map, string key)
        { string text = String(map, key, 36); if (!Guid.TryParseExact(text, "D", out Guid id) || id == Guid.Empty || text != id.ToString("D")) throw Protocol(); return id; }
        internal static List<object> List(Dictionary<string, object> map, string key) => map[key] as List<object> ?? throw Protocol();
        internal static PreviewMode Mode(string mode) => mode == "fixture" ? PreviewMode.Fixture : mode == "cloud" ? PreviewMode.Cloud : throw Protocol();
        internal static SpeechMode Speech(string mode) => mode == "fixture" ? SpeechMode.Fixture : mode == "cloud" ? SpeechMode.Cloud : mode == "system" ? SpeechMode.System : throw Protocol();
        internal static InvalidDataException Protocol(OperationKey? operation = null) => Error("protocol_error", operation);
        internal static InvalidDataException Error(string code, OperationKey? operation, bool retryable = false)
        {
            string message;
            switch (code)
            {
                case "unauthorized": message = "本机访问令牌缺失或失效，请启动后台后重试连接。"; break;
                case "backend_unavailable": message = "无法连接本机后台，请启动后台后重试。"; retryable = true; break;
                case "provider_unavailable": message = "服务未配置或暂不可用，请检查后台配置。"; break;
                case "provider_timeout": message = "后台响应超时，请手动重新发送。"; break;
                case "budget_exceeded": message = "已达到调用预算，请检查后台额度。"; break;
                case "rate_limited": message = "后台忙或已达到资源上限，请稍后手动重试。"; break;
                case "invalid_audio": message = "测试音频损坏或格式不受支持。"; break;
                case "resource_expired": message = "音频资源已过期，请手动重新发送。"; break;
                case "duplicate_request": message = "请求已经提交，未重复发起生成。"; break;
                case "request_cancelled": case "cancelled": message = "该请求已经停止。"; break;
                case "invalid_request": message = "发送内容不符合本机预览限制。"; break;
                case "microphone_unavailable": message = "此版本尚未启用语音输入。"; break;
                default: message = "后台响应无效或不完整，请检查后台后手动重试。"; break;
            }
            var error = new InvalidDataException("Preview operation failed: " + code);
            error.Data["preview_error"] = new PreviewError(code, message, retryable, operation);
            return error;
        }
        public void Dispose() { lock (_identityGate) { _active = null; _allowedAudio = null; } _client.Dispose(); }
    }

    /// <summary>Wire state machine runs before publishing any typed event.</summary>
    internal sealed class EventDecoder
    {
        private readonly TurnSubmission _request;
        private readonly Dictionary<uint, string> _seen = new Dictionary<uint, string>();
        private TurnKey? _turn;
        private uint _next;
        private int _stage;
        private string _delta = "";
        internal bool Terminal { get; private set; }
        internal EventDecoder(TurnSubmission request) { _request = request; }
        internal PreviewEvent Accept(string raw)
        {
            var e = StrictJson.Parse(raw);
            HttpConversationGateway.Fields(e, "protocol", "request_id", "generation", "conversation_id", "turn_id", "seq", "type", "payload");
            if (HttpConversationGateway.String(e, "protocol") != PreviewProtocol.Name || HttpConversationGateway.Uuid(e, "request_id") != _request.Operation.RequestId || HttpConversationGateway.Uuid(e, "conversation_id") != _request.Operation.ConversationId || HttpConversationGateway.Number(e, "generation", 0, uint.MaxValue) != _request.Operation.Generation) throw Bad();
            var turn = new TurnKey(_request.Operation, HttpConversationGateway.Uuid(e, "turn_id"));
            if (_turn.HasValue && _turn.Value != turn) throw Bad();
            uint seq = (uint)HttpConversationGateway.Number(e, "seq", 0, uint.MaxValue);
            if (_seen.TryGetValue(seq, out string previous)) { if (previous != raw) throw Bad(); return null; }
            if (Terminal || seq != _next || _seen.Count >= 1024) throw Bad();
            string type = HttpConversationGateway.String(e, "type");
            var p = HttpConversationGateway.Object(e["payload"]);
            PreviewEvent parsed;
            switch (type)
            {
                case "turn.accepted":
                    if (_stage != 0 || seq != 0) throw Bad();
                    HttpConversationGateway.Fields(p, "mode");
                    parsed = new TurnAcceptedEvent(turn, seq, HttpConversationGateway.Mode(HttpConversationGateway.String(p, "mode"))); _stage = 1; break;
                case "text.delta":
                    if (_stage != 1) throw Bad(); HttpConversationGateway.Fields(p, "text");
                    string delta = HttpConversationGateway.String(p, "text", 400); _delta += delta;
                    if (HttpConversationGateway.UnicodeLength(_delta) > 400) throw Bad();
                    parsed = new TextDeltaEvent(turn, seq, delta); break;
                case "text.completed":
                    if (_stage != 1) throw Bad(); HttpConversationGateway.Fields(p, "text", "emotion");
                    string full = HttpConversationGateway.String(p, "text", 400);
                    string emotion = HttpConversationGateway.String(p, "emotion");
                    Emotion mood;
                    switch (emotion) { case "neutral": mood = Emotion.Neutral; break; case "happy": mood = Emotion.Happy; break; case "sad": mood = Emotion.Sad; break; case "surprised": mood = Emotion.Surprised; break; case "thinking": mood = Emotion.Thinking; break; default: throw Bad(); }
                    parsed = new TextCompletedEvent(turn, seq, full, mood); _stage = 2; break;
                case "audio.ready":
                    if (_stage != 2 || !_request.GenerateAudio) throw Bad();
                    HttpConversationGateway.Fields(p, "path", "sample_rate", "channels", "codec", "total_samples");
                    string path = HttpConversationGateway.String(p, "path", 160);
                    if (path != "/preview/unity/v1/turns/" + turn.TurnId.ToString("D") + "/audio.wav" || HttpConversationGateway.Number(p, "sample_rate", 24000, 24000) != 24000 || HttpConversationGateway.Number(p, "channels", 1, 1) != 1 || HttpConversationGateway.String(p, "codec") != "pcm_s16le") throw Bad();
                    parsed = new AudioReadyEvent(new AudioReadyDescriptor(turn, path, 24000, 1, "pcm_s16le", HttpConversationGateway.Number(p, "total_samples", 1, 2880000)), seq); _stage = 3; break;
                case "audio.skipped":
                    if (_stage != 2 || _request.GenerateAudio) throw Bad(); HttpConversationGateway.Fields(p, "reason");
                    if (HttpConversationGateway.String(p, "reason") != "user_disabled") throw Bad();
                    parsed = new AudioSkippedEvent(turn, seq, AudioSkipReason.UserDisabled); _stage = 3; break;
                case "generation.completed":
                    if (_stage != 3) throw Bad(); HttpConversationGateway.Fields(p);
                    parsed = new GenerationCompletedEvent(turn, seq); Terminal = true; break;
                case "turn.cancelled":
                    if (_stage == 0) throw Bad(); HttpConversationGateway.Fields(p);
                    parsed = new TurnCancelledEvent(turn, seq); Terminal = true; break;
                case "error":
                    if (_stage == 0) throw Bad(); HttpConversationGateway.Fields(p, "code", "message", "retryable");
                    string code = HttpConversationGateway.String(p, "code", 64); HttpConversationGateway.String(p, "message", 2048);
                    var error = (PreviewError)HttpConversationGateway.Error(code, _request.Operation, HttpConversationGateway.Bool(p, "retryable")).Data["preview_error"];
                    parsed = new TurnErrorEvent(turn, seq, error); Terminal = true; break;
                default: throw Bad();
            }
            _turn = turn; _seen.Add(seq, raw); _next++; return parsed;
        }
        private InvalidDataException Bad() => HttpConversationGateway.Protocol(_request.Operation);
    }
}
