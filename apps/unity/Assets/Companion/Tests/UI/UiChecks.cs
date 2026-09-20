using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.UI;
using UnityEngine;

namespace AICompanion.Preview.Tests
{
    public static class UiChecks
    {
        private static int assertions;
        public static void Run()
        {
            assertions = 0;
            Check(EnterPolicy.ShouldSubmit(true, false, false, 3), "plain Return submits");
            Check(!EnterPolicy.ShouldSubmit(true, true, false, 10), "Shift Return remains newline");
            Check(!EnterPolicy.ShouldSubmit(true, false, true, 0), "IME candidates do not send");
            Check(!EnterPolicy.ShouldSubmit(true, false, false, 1), "IME commit Return is guarded");
            Check(!EnterPolicy.ShouldSubmit(false, false, false, 20), "holding Return cannot repeat");
            Check(EnterPolicy.CountScalars("你好🦊\n") == 4, "Unicode scalar counting");
            Check(EnterPolicy.CountScalars("\ud800") == int.MaxValue, "unpaired surrogate rejected");
            Check(EnterPolicy.IsReturnCharacter('\r') && EnterPolicy.IsReturnCharacter('\n') && EnterPolicy.IsReturnCharacter('\v') && EnterPolicy.IsReturnCharacter('\u0003'), "native duplicate Return characters are suppressed");
            Check(!EnterPolicy.IsReturnCharacter('中') && !EnterPolicy.IsReturnCharacter('\t'), "ordinary text and Tab do not match duplicate Return characters");
            var gate = new ReturnKeyGate();
            Check(gate.Observe(EventType.KeyDown, EventModifiers.Shift, false, 20, 50) == ReturnKeyAction.Newline, "native Shift Return remains newline after frame modifier release");
            Check(gate.Observe(EventType.KeyUp, EventModifiers.None, false, 20, 50) == ReturnKeyAction.None, "same-frame native key up only releases latch");
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, false, 20, 51) == ReturnKeyAction.Send, "bare native Return sends once");
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, false, 20, 52) == ReturnKeyAction.None, "native autorepeat does not send again");
            gate.Observe(EventType.KeyUp, EventModifiers.None, false, 20, 52);
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, true, 0, 53) == ReturnKeyAction.None, "native IME confirmation is suppressed");
            gate.Observe(EventType.KeyUp, EventModifiers.None, false, 1, 54);
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, false, 1, 54) == ReturnKeyAction.None, "native post-composition Return is suppressed");
#if UNITY_EDITOR_WIN
            var nativeType = typeof(HistoryFileExport).GetNestedType("OpenFileName", BindingFlags.NonPublic);
            int nativeSize = Marshal.SizeOf(nativeType);
            Check(nativeSize == (IntPtr.Size == 8 ? 152 : 88), "OPENFILENAME matches Windows ABI");
            var memory = Marshal.AllocHGlobal(nativeSize);
            try
            {
                Marshal.StructureToPtr(Activator.CreateInstance(nativeType), memory, false);
                Check(Marshal.PtrToStructure(memory, nativeType) != null, "native dialog descriptor marshals on actual Unity Mono");
            }
            finally { Marshal.FreeHGlobal(memory); }
            CheckExportWorker();
            CheckExportFiles();
#endif
            Debug.Log("U01_UI_CHECKS_PASSED assertions=" + assertions + "; actual Windows IME/DPI requires Player QA");
        }
        private static void CheckExportWorker()
        {
            int caller = Thread.CurrentThread.ManagedThreadId, worker = caller;
            bool background = false;
            ApartmentState apartment = ApartmentState.Unknown;
            using (var cancellation = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var task = HistoryFileExport.RunOnStaAsync(token =>
                {
                    worker = Thread.CurrentThread.ManagedThreadId;
                    background = Thread.CurrentThread.IsBackground;
                    apartment = Thread.CurrentThread.GetApartmentState();
                    entered.Set();
                    if (!release.Wait(5000)) throw new TimeoutException("Worker test release missing.");
                    token.ThrowIfCancellationRequested();
                    return "selected";
                }, cancellation.Token);
                try
                {
                    Check(entered.Wait(3000), "save work starts independently of caller continuation");
                    Check(worker != caller && apartment == ApartmentState.STA && background, "save work uses dedicated background STA");
                    Check(!task.IsCompleted, "waiting save work does not synchronously block the caller");
                    bool duplicateRejected = false;
                    try { HistoryFileExport.RunOnStaAsync(_ => null, CancellationToken.None); }
                    catch (InvalidOperationException) { duplicateRejected = true; }
                    Check(duplicateRejected, "second native operation is rejected while one is waiting");
                    cancellation.Cancel();
                    Check(!task.IsCompleted, "cancellation does not finish before worker native cleanup");
                    duplicateRejected = false;
                    try { HistoryFileExport.RunOnStaAsync(_ => null, CancellationToken.None); }
                    catch (InvalidOperationException) { duplicateRejected = true; }
                    Check(duplicateRejected, "single-flight remains held while cancelled native work exits");
                }
                finally { release.Set(); WaitCompleted(task); }
                Check(task.IsCanceled, "worker cancellation completes as cancellation");
            }
            var saved = HistoryFileExport.RunOnStaAsync(_ => "selected.json", CancellationToken.None);
            WaitCompleted(saved);
            Check(saved.GetAwaiter().GetResult() == "selected.json", "next export can save after cancellation cleanup");
            var failed = HistoryFileExport.RunOnStaAsync(_ => throw new IOException("Controlled test failure."), CancellationToken.None);
            WaitCompleted(failed);
            Check(failed.IsFaulted && failed.Exception.InnerException is IOException, "worker failure reaches caller task");
            var afterFailure = HistoryFileExport.RunOnStaAsync(_ => null, CancellationToken.None);
            WaitCompleted(afterFailure);
            Check(afterFailure.GetAwaiter().GetResult() == null, "failure releases guard and native user-cancel remains null");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool called = false;
                var task = HistoryFileExport.RunOnStaAsync(_ => { called = true; return null; }, cancelled.Token);
                Check(task.IsCanceled && !called, "already-cancelled export never starts dialog work");
            }
        }

        private static void CheckExportFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "saki-ui-export-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string selected = Path.Combine(directory, "selected.json");
            try
            {
                byte[] first = Encoding.UTF8.GetBytes("{\"value\":\"测试\"}");
                HistoryFileExport.WriteSelectedFile(selected, first, CancellationToken.None);
                Check(File.ReadAllText(selected) == Encoding.UTF8.GetString(first), "selected file stores exact UTF8 export bytes");
                byte[] replacement = Encoding.UTF8.GetBytes("{\"replacement\":true}");
                HistoryFileExport.WriteSelectedFile(selected, replacement, CancellationToken.None);
                Check(File.ReadAllText(selected) == Encoding.UTF8.GetString(replacement), "confirmed target can be replaced atomically");
                using (var fileLock = new FileStream(selected, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool refused = false;
                    try { HistoryFileExport.WriteSelectedFile(selected, first, CancellationToken.None); }
                    catch (IOException) { refused = true; }
                    Check(refused, "real Windows file lock refuses replacement");
                    Check(File.ReadAllText(selected) == Encoding.UTF8.GetString(replacement), "failed replacement preserves existing target");
                }
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel(); bool observed = false;
                    try { HistoryFileExport.WriteSelectedFile(selected, first, cancelled.Token); }
                    catch (OperationCanceledException) { observed = true; }
                    Check(observed && File.ReadAllText(selected) == Encoding.UTF8.GetString(replacement), "cancelled save preserves destination");
                }
                Check(Directory.GetFiles(directory, ".saki-export-*.tmp").Length == 0, "success/failure/cancel do not retain temporary exports");
            }
            finally
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static void WaitCompleted(Task task)
        {
            // Worker checks do not capture Unity's context or open a native dialog.
            if (!SpinWait.SpinUntil(() => task.IsCompleted, 5000)) throw new TimeoutException("Export worker did not settle.");
        }
        private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception("UI check failed: " + name); }
    }
}
