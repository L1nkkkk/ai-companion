using System;
using System.Collections.Generic;
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
            CheckDialogCancellation();
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

#if UNITY_EDITOR_WIN
        private static void CheckDialogCancellation()
        {
            var windows = new DialogWindows();
            var dialog = new HistoryFileExport.DialogWindow(windows);
            IntPtr child = new IntPtr(10), root = new IntPtr(20), popup = new IntPtr(30), nested = new IntPtr(40);
            dialog.Opened(child, root);
            var request = Task.Run(() => { dialog.RequestClose(); dialog.RequestClose(); });
            WaitCompleted(request); request.GetAwaiter().GetResult();
            Check(windows.Posts.Count == 1 && windows.Posts[0].Window == child && windows.Posts[0].Message == HistoryFileExport.DialogWindow.CancelMessage,
                "cross-thread cancellation only queues one private STA message");
            Check(windows.Queries == 0, "cancellation callback never queries native windows from the caller thread");
            windows.Posts.Clear();
            windows.Popups[root] = popup; windows.Popups[popup] = nested;
            windows.Buttons[(nested, 7)] = new IntPtr(47);
            windows.Disabled.Add(root); windows.Disabled.Add(popup);
            dialog.ProcessClose();
            Check(windows.Posts.Count == 1 && windows.Posts[0].Window == nested && windows.Posts[0].Message == 0x0111 && windows.Posts[0].Command == 7,
                "overwrite cancellation only posts No to the deepest modal window");
            dialog.ProcessClose();
            Check(windows.Posts.Count == 1, "pending modal command is not duplicated or followed by Close");
            windows.Popups[popup] = popup; windows.Disabled.Remove(popup);
            windows.Buttons[(popup, 2)] = new IntPtr(32);
            dialog.ProcessClose();
            Check(windows.Posts.Count == 2 && windows.Posts[1].Window == popup && windows.Posts[1].Message == 0x0111 && windows.Posts[1].Command == 2,
                "next STA pump cancels the remaining popup after deeper modal unwinds");
            windows.Popups[root] = root;
            dialog.ProcessClose();
            Check(windows.Posts.Count == 2, "save parent is not cancelled while native modal still disables it");
            windows.Disabled.Remove(root);
            dialog.ProcessClose();
            Check(windows.Posts.Count == 3 && windows.Posts[2].Window == root && windows.Posts[2].Message == 0x0111 && windows.Posts[2].Command == 2,
                "enabled save parent receives only IDCANCEL after nested dialogs leave");
            dialog.ProcessClose();
            Check(windows.Posts.Count == 3, "save parent cancellation is single-shot until destruction");
            int queries = windows.Queries;
            var wrongThread = Task.Run(() => dialog.ProcessClose());
            WaitCompleted(wrongThread);
            Check(wrongThread.IsFaulted && wrongThread.Exception.InnerException is InvalidOperationException && windows.Queries == queries,
                "dialog queries reject the wrong thread before touching any window");
            dialog.Closed(); dialog.RequestClose(); dialog.ProcessClose();
            Check(windows.Posts.Count == 3, "destroyed native dialog cannot receive another cancellation");

            windows = new DialogWindows(); dialog = new HistoryFileExport.DialogWindow(windows); dialog.Opened(child, root);
            windows.Popups[root] = popup;
            dialog.ProcessClose();
            Check(windows.Posts.Count == 1 && windows.Posts[0].Window == popup && windows.Posts[0].Message == 0x0010,
                "popup without No or Cancel receives exactly one Close and no parent command");
            dialog.Closed();
            windows = new DialogWindows(); dialog = new HistoryFileExport.DialogWindow(windows); dialog.Opened(child, root);
            windows.Popups[root] = popup; windows.Buttons[(popup, 7)] = new IntPtr(37); windows.Disabled.Add(new IntPtr(37));
            windows.Buttons[(popup, 2)] = new IntPtr(32);
            dialog.ProcessClose();
            Check(windows.Posts.Count == 1 && windows.Posts[0].Command == 2, "disabled No falls back to an enabled Cancel button");
            dialog.Closed();
            windows = new DialogWindows(); dialog = new HistoryFileExport.DialogWindow(windows); dialog.Opened(child, root);
            windows.PostSucceeds = false; dialog.RequestClose();
            windows.PostSucceeds = true; dialog.RequestClose();
            Check(windows.Posts.Count == 2, "failed private wakeup can be retried");
            dialog.ProcessClose();
            Check(windows.Posts.Count == 3 && windows.Posts[2].Command == 2, "retried cancellation still follows the native IDCANCEL path");
            dialog.Closed();
            windows = new DialogWindows(); dialog = new HistoryFileExport.DialogWindow(windows); dialog.Opened(child, root);
            windows.Popups[root] = popup; windows.Hidden.Add(popup);
            dialog.ProcessClose();
            Check(windows.Posts.Count == 1 && windows.Posts[0].Window == root, "hidden stale popup does not prevent cancellation of the enabled save window");
            dialog.Closed();
            windows = new DialogWindows(); dialog = new HistoryFileExport.DialogWindow(windows); dialog.Opened(child, root);
            windows.Popups[root] = popup; windows.TaskDialogs.Add(popup); windows.Disabled.Add(root);
            dialog.ProcessClose();
            Check(windows.Posts.Count == 1 && windows.Posts[0].Window == popup && windows.Posts[0].Message == 0x466 && windows.Posts[0].Command == 7,
                "owned DirectUI overwrite prompt receives TaskDialog No without native button IDs");
            dialog.ProcessClose();
            Check(windows.Posts.Count == 1, "TaskDialog No is not followed by another command while its modal loop exits");
            windows.Popups[root] = root; windows.Disabled.Remove(root); dialog.ProcessClose();
            Check(windows.Posts.Count == 2 && windows.Posts[1].Window == root && windows.Posts[1].Message == 0x111 && windows.Posts[1].Command == 2,
                "save parent cancels only after owned TaskDialog leaves");
            dialog.Closed();
        }

        // Window policy checks deliberately do not create native dialogs or claim native-loop QA.
        private sealed class DialogWindows : HistoryFileExport.IDialogWindows
        {
            private readonly int thread = Thread.CurrentThread.ManagedThreadId;
            public readonly Dictionary<IntPtr, IntPtr> Popups = new Dictionary<IntPtr, IntPtr>();
            public readonly Dictionary<(IntPtr, int), IntPtr> Buttons = new Dictionary<(IntPtr, int), IntPtr>();
            public readonly HashSet<IntPtr> Disabled = new HashSet<IntPtr>();
            public readonly HashSet<IntPtr> Hidden = new HashSet<IntPtr>();
            public readonly HashSet<IntPtr> TaskDialogs = new HashSet<IntPtr>();
            public readonly List<(IntPtr Window, uint Message, ulong Command)> Posts = new List<(IntPtr, uint, ulong)>();
            public int Queries;
            public bool PostSucceeds = true;
            private void Query() { if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Unexpected cross-thread native query."); Queries++; }
            public IntPtr OwnedPopup(IntPtr window) { Query(); return Popups.TryGetValue(window, out var popup) ? popup : window; }
            public bool TaskDialog(IntPtr window) { Query(); return TaskDialogs.Contains(window); }
            public IntPtr Button(IntPtr window, int id) { Query(); return Buttons.TryGetValue((window, id), out var button) ? button : IntPtr.Zero; }
            public bool Enabled(IntPtr window) { Query(); return !Disabled.Contains(window); }
            public bool Visible(IntPtr window) { Query(); return !Hidden.Contains(window); }
            public bool Post(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam) { Posts.Add((window, message, wParam.ToUInt64())); return PostSucceeds; }
        }
#endif

        private static void WaitCompleted(Task task)
        {
            // Worker checks do not capture Unity's context or open a native dialog.
            if (!SpinWait.SpinUntil(() => task.IsCompleted, 5000)) throw new TimeoutException("Export worker did not settle.");
        }
        private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception("UI check failed: " + name); }
    }
}
