using System;
using System.Diagnostics;
using AICompanion.Preview.Contracts;
using UnityEngine;

namespace AICompanion.Preview.Audio
{
    /// <summary>One Unity output source, bounded latest-level exchange, no allocation in the audio callback.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class UnityAudioPlayer : MonoBehaviour, IAudioPlayer
    {
        private readonly object _gate = new object();
        private AudioSource _source;
        private AudioClip _clip;
        private ValidatedPcm _owner;
        private OperationKey? _armed;
        private TurnKey? _turn;
        private bool _running, _callbackStarted, _startedPublished, _disposed;
        private float _volume = 0.8f, _latestRms;
        private long _outputFrames, _played, _total, _ticks, _sampleStart;
        private int _sampleCount;
        private int _outputRate;
        private double _nextPublish, _startDeadline;
        private long _lastNonzeroOutputTicks, _lastOutputTicks;
        private int _outputBlockFrames;
        public long LastNonzeroOutputTicks { get { lock (_gate) return _lastNonzeroOutputTicks; } }
        public long LastOutputTicks { get { lock (_gate) return _lastOutputTicks; } }
        public int OutputBlockFrames { get { lock (_gate) return _outputBlockFrames; } }
        public event Action<PlaybackStarted> PlaybackStarted;
        public event Action<PlaybackEnded> PlaybackEnded;
        public event Action<PlaybackProgress> ProgressChanged;
        public event Action<AudioLevelSample> PostVolumeLevel;
        public PlaybackSnapshot Snapshot { get { lock (_gate) return new PlaybackSnapshot(_turn, _running, _played, _total, 24000, _ticks, _volume); } }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0;
            _source.volume = 1;
            _outputRate = AudioSettings.outputSampleRate;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        public void Arm(OperationKey operation)
        {
            if (operation.RequestId == Guid.Empty || operation.ConversationId == Guid.Empty) throw new ArgumentException("Invalid operation.");
            Stop(StopReason.Replaced);
            lock (_gate)
            {
                _armed = operation; _turn = null; _played = _total = _outputFrames = 0;
                _sampleStart = 0; _sampleCount = 0; _ticks = Stopwatch.GetTimestamp();
            }
        }

        public PlayResult Play(ValidatedPcm pcm, TurnKey turn)
        {
            lock (_gate)
            {
                if (_disposed || !_armed.HasValue || _armed.Value != turn.Operation || turn.TurnId == Guid.Empty || _running || pcm == null || pcm.IsDisposed)
                    return new PlayResult(false, new PreviewError("stale_operation", "这段声音已失效。", false, turn.Operation));
            }
            AudioClip clip = null;
            try
            {
                var source = pcm.Samples.Span;
                var floats = new float[source.Length];
                for (int i = 0; i < source.Length; i++) floats[i] = source[i] / 32768f;
                clip = AudioClip.Create("U01 validated test audio", floats.Length, 1, 24000, false);
                bool loaded = clip.SetData(floats, 0);
                Array.Clear(floats, 0, floats.Length);
                if (!loaded) throw new InvalidOperationException();
                lock (_gate)
                {
                    _owner = pcm;
                    _clip = clip;
                    _turn = turn;
                    _total = pcm.TotalSamples;
                    _played = _outputFrames = 0;
                    _callbackStarted = _startedPublished = false;
                    _latestRms = 0;
                    _ticks = Stopwatch.GetTimestamp();
                    _running = true;
                }
                _source.clip = clip;
                _startDeadline = Time.realtimeSinceStartupAsDouble + 5;
                _source.Play();
                return new PlayResult(true, null);
            }
            catch
            {
                // An unsuccessful Play never takes ownership.
                lock (_gate) { _owner = null; _clip = null; _running = false; }
                if (_source != null) { _source.Stop(); _source.clip = null; }
                if (clip != null) Destroy(clip);
                return new PlayResult(false, new PreviewError("playback_unavailable", "无法打开系统播放设备。", true, turn.Operation));
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_gate)
            {
                if (!_running || channels <= 0) { Array.Clear(data, 0, data.Length); return; }
                double square = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    float value = data[i] * _volume;
                    data[i] = value;
                    square += value * value;
                }
                _callbackStarted = true;
                _sampleStart = _played;
                _outputBlockFrames = data.Length / channels;
                _outputFrames += _outputBlockFrames;
                _played = Math.Min(_total, _outputFrames * 24000 / Math.Max(1, _outputRate));
                _sampleCount = (int)(_played - _sampleStart);
                _latestRms = _volume == 0 ? 0 : (float)Math.Min(1, Math.Sqrt(square / Math.Max(1, data.Length)));
                _ticks = Stopwatch.GetTimestamp();
                _lastOutputTicks = _ticks;
                if (square > 0) _lastNonzeroOutputTicks = _ticks;
            }
        }

        private void Update()
        {
            TurnKey turn;
            long played, total, ticks;
            long sampleStart;
            int sampleCount;
            float rms;
            bool started;
            lock (_gate)
            {
                if (!_running || !_turn.HasValue) return;
                turn = _turn.Value; played = _played; total = _total; ticks = _ticks; rms = _latestRms;
                sampleStart = _sampleStart; sampleCount = _sampleCount;
                started = _callbackStarted && !_startedPublished;
                if (started) _startedPublished = true;
            }
            if (started) PlaybackStarted?.Invoke(new PlaybackStarted(turn, total, 24000, ticks));
            if (Time.realtimeSinceStartupAsDouble >= _nextPublish)
            {
                _nextPublish = Time.realtimeSinceStartupAsDouble + 1.0 / 30;
                ProgressChanged?.Invoke(new PlaybackProgress(turn, played, total, 24000, ticks));
                PostVolumeLevel?.Invoke(new AudioLevelSample(turn, rms, sampleStart, sampleCount, ticks));
            }
            if (!_startedPublished && Time.realtimeSinceStartupAsDouble > _startDeadline)
            {
                Finish(PlaybackEndReason.Failed, new PreviewError("playback_unavailable", "系统播放设备未输出音频，请检查设备后重试。", true, turn.Operation));
                return;
            }
            if (_startedPublished && !_source.isPlaying)
            {
                bool completed = played >= total;
                Finish(completed ? PlaybackEndReason.Completed : PlaybackEndReason.Failed,
                    completed ? null : new PreviewError("playback_interrupted", "系统音频输出提前结束。", true, turn.Operation));
            }
        }

        public void SetVolume(float volume01)
        {
            if (float.IsNaN(volume01) || float.IsInfinity(volume01) || volume01 < 0 || volume01 > 1) throw new ArgumentOutOfRangeException(nameof(volume01));
            TurnKey? turn;
            lock (_gate) { _volume = volume01; if (volume01 == 0) _latestRms = 0; turn = _turn; }
            if (volume01 == 0 && turn.HasValue) PostVolumeLevel?.Invoke(new AudioLevelSample(turn.Value, 0, _played, 0, Stopwatch.GetTimestamp()));
        }

        public void Stop(StopReason reason)
        {
            lock (_gate) _armed = null;
            Finish(PlaybackEndReason.Stopped, null);
        }

        private void Finish(PlaybackEndReason reason, PreviewError error)
        {
            TurnKey? turn;
            ValidatedPcm owner;
            AudioClip clip;
            long played, total, ticks;
            lock (_gate)
            {
                // First silence the callback; local zero precedes network cancellation and any disposal.
                if (!_running) return;
                _running = false; _latestRms = 0; _armed = null;
                turn = _turn; owner = _owner; clip = _clip;
                _owner = null; _clip = null;
                played = _played; total = _total; ticks = _ticks = Stopwatch.GetTimestamp();
            }
            if (_source != null) { _source.Stop(); _source.clip = null; }
            if (turn.HasValue) PostVolumeLevel?.Invoke(new AudioLevelSample(turn.Value, 0, played, 0, ticks));
            owner?.Dispose();
            if (clip != null) Destroy(clip);
            if (turn.HasValue) PlaybackEnded?.Invoke(new PlaybackEnded(turn.Value, reason, played, total, 24000, ticks, error));
        }

        private void OnAudioConfigurationChanged(bool changed)
        {
            if (_running) Finish(PlaybackEndReason.Failed, new PreviewError("audio_device_changed", "音频设备已更改，请重新发送。", true, _turn?.Operation));
            _outputRate = AudioSettings.outputSampleRate;
        }
        public void Dispose()
        {
            if (_disposed) return;
            Stop(StopReason.WindowClosing);
            _disposed = true;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }
        private void OnDestroy() => Dispose();
    }
}
