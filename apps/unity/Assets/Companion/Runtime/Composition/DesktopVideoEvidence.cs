using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using AICompanion.Preview.Contracts;
using UnityEngine;

namespace AICompanion.Preview.Composition
{
    // Explicit fixture QA only: these are the actual rendered Player frames, never a
    // desktop/window capture. Native QPC is preserved for independent WASAPI alignment.
    public sealed class DesktopVideoEvidence : MonoBehaviour
    {
        private DesktopBootstrap app;
        private string directory;
        private StreamWriter index;
        private long frequency;
        private int captured;
        private int errors;
        private bool ended;
        private const int Fps = 15;
        private const double Duration = 60;

        [Serializable] private sealed class Source
        {
            public string capture_scope = "unity_player_backbuffer_only";
            public int target_pid;
            public bool fixture_only = true;
            public long qpc_frequency;
            public int target_fps = Fps;
            public string source_commit;
            public string timestamp_boundary = "Win32 QPC immediately before synchronous EndOfFrame backbuffer readback; not DWM presentation or physical display time";
        }
        [Serializable] private sealed class End
        {
            public long end_qpc_ticks;
            public bool completed;
            public int captured;
            public int dropped;
            public int readback_errors;
        }
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceCounter(out long counter);
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceFrequency(out long value);

        public void Initialize(DesktopBootstrap bootstrap, string evidenceDirectory)
        {
            app = bootstrap;
            string commit = DesktopBootstrap.Argument("-evidenceSourceSha");
            if (app.Session.Snapshot.Mode != PreviewMode.Fixture ||
                string.IsNullOrEmpty(DesktopBootstrap.Argument("-userDataPath")) ||
                string.IsNullOrEmpty(commit) || commit.Length != 40)
                throw new InvalidOperationException("Video evidence requires isolated fixture data and an explicit forty-character source SHA.");
            foreach (char c in commit) if (!Uri.IsHexDigit(c)) throw new ArgumentException("Invalid evidence SHA.");
            directory = Path.Combine(Path.GetFullPath(evidenceDirectory), "video-frames");
            if (Directory.Exists(directory)) throw new IOException("Video evidence directory must be new.");
            Directory.CreateDirectory(directory);
            if (!QueryPerformanceFrequency(out frequency) || frequency <= 0 || !QueryPerformanceCounter(out long initialTick) || initialTick <= 0)
                throw new InvalidOperationException("Native evidence clock is unavailable.");
            var source = new Source
            {
                target_pid = Process.GetCurrentProcess().Id,
                qpc_frequency = frequency,
                source_commit = commit
            };
            File.WriteAllText(Path.Combine(directory, "frame-source.json"), JsonUtility.ToJson(source, true));
            index = new StreamWriter(Path.Combine(directory, "frames.csv"));
            index.WriteLine("frame_index,file,capture_qpc_ticks,qpc_frequency,readback_done_qpc_ticks,write_done_qpc_ticks,width,height,request_id,generation,turn_id,mouth,volume,phase,status");
            StartCoroutine(Record());
        }

        private IEnumerator Record()
        {
            double began = Time.realtimeSinceStartupAsDouble;
            double next = began;
            while (!ended && Time.realtimeSinceStartupAsDouble - began < Duration)
            {
                yield return new WaitForEndOfFrame();
                if (ended || Time.realtimeSinceStartupAsDouble < next) continue;
                next = Time.realtimeSinceStartupAsDouble + 1d / Fps;
                if (!CaptureFrame()) { Finish(false); yield break; }
            }
            Finish(true);
        }

        private bool CaptureFrame()
        {
            Texture2D texture = null;
            QueryPerformanceCounter(out long captureTick);
            long readbackTick = captureTick;
            int width = Screen.width, height = Screen.height;
            string file = "frame-" + captured.ToString("D6", CultureInfo.InvariantCulture) + ".png";
            var state = app.Session.Snapshot;
            var operation = state.Operation;
            var turn = state.Turn;
            float mouth = app.Avatar.MouthValue;
            float volume = app.Player.Snapshot.Volume01;
            bool success = false;
            try
            {
                if (width > 2048 || height > 1440) throw new InvalidOperationException("Evidence frame exceeds its declared bound.");
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                QueryPerformanceCounter(out readbackTick);
                File.WriteAllBytes(Path.Combine(directory, file), texture.EncodeToPNG());
                success = true;
            }
            catch (Exception)
            {
                errors++;
                UnityEngine.Debug.LogWarning("DESKTOP_VIDEO_EVIDENCE_FAILED: incomplete frame evidence retained.");
            }
            finally
            {
                if (texture != null) Destroy(texture);
                QueryPerformanceCounter(out long writeTick);
                index.WriteLine(captured + "," + (success ? file : "") + "," + captureTick + "," + frequency + "," + readbackTick + "," + writeTick + "," +
                    width + "," + height + "," + operation?.RequestId + "," + operation?.Generation + "," + turn?.TurnId + "," +
                    mouth.ToString("R", CultureInfo.InvariantCulture) + "," + volume.ToString("R", CultureInfo.InvariantCulture) + "," + state.Phase + "," + (success ? "captured" : "error"));
                index.Flush();
                if (success) captured++;
            }
            return success;
        }

        private void Finish(bool completed)
        {
            if (ended || index == null) return;
            ended = true;
            QueryPerformanceCounter(out long tick);
            index.Dispose();
            File.WriteAllText(Path.Combine(directory, "capture-end.json"), JsonUtility.ToJson(new End
            {
                end_qpc_ticks = tick, completed = completed && errors == 0,
                captured = captured, dropped = 0, readback_errors = errors
            }, true));
        }
        private void OnApplicationQuit() { Finish(false); }
        private void OnDestroy() { Finish(false); }
    }
}
