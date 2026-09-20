using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.UI
{
    /// <summary>One independent STA save window; no Unity APIs are called by its worker.</summary>
    public static class HistoryFileExport
    {
        private static int active;

        public static Task<string> SaveAsync(HistoryExport export, CancellationToken token, Action<HistoryExportStage> progress = null, Action<string> diagnostic = null)
        {
            if (export == null || export.SchemaVersion != 1 || export.MediaType != "application/json" ||
                export.ConversationId == Guid.Empty || export.Utf8Json.Length > 2 * 1024 * 1024)
                throw new ArgumentException("Invalid history export.");
            string safeName = "conversation-" + export.ConversationId.ToString("N") + ".json";
            return RunOnStaAsync(cancel =>
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                string selected = SelectPath(safeName, cancel, progress, diagnostic);
                cancel.ThrowIfCancellationRequested();
                if (selected == null) return null;
                progress?.Invoke(HistoryExportStage.Saving);
                WriteSelectedFile(selected, export.Utf8Json.ToArray(), cancel);
                return selected;
#else
                throw new PlatformNotSupportedException("History export currently requires Windows.");
#endif
            }, token);
        }

        // Prove threading/single-flight/cancellation without claiming a fake dialog is native UI QA.
        internal static Task<string> RunOnStaAsync(Func<CancellationToken, string> work, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled<string>(token);
            if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                throw new InvalidOperationException("A history save window is already active.");
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var thread = new Thread(() =>
                {
                    string selected = null;
                    Exception failure = null;
                    bool cancelled = false;
                    try { token.ThrowIfCancellationRequested(); selected = work(token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    catch (Exception ex) { failure = ex; }
                    finally { Volatile.Write(ref active, 0); }
                    // Native cleanup, not the cancellation request, releases the single-flight guard.
                    if (cancelled) completion.TrySetCanceled();
                    else if (failure != null) completion.TrySetException(failure);
                    else completion.TrySetResult(selected);
                }) { IsBackground = true, Name = "SAKI history export" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            catch { Volatile.Write(ref active, 0); throw; }
            return completion.Task;
        }

        internal static void WriteSelectedFile(string selected, byte[] bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string destination = Path.GetFullPath(selected);
            string temporary = Path.Combine(Path.GetDirectoryName(destination), ".saki-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                token.ThrowIfCancellationRequested();
                // Same-directory commit preserves an existing destination on write failure.
                // Cancellation after this commit point does not retract a successfully saved file.
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                if (!MoveFileEx(temporary, destination, 0x1 | 0x8))
                    // GetHRForLastWin32Error is not implemented by the frozen Unity Mono runtime.
                    throw new IOException("History export replacement failed.", unchecked((int)0x80070000) | (Marshal.GetLastWin32Error() & 0xffff));
#else
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
#endif
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static string SelectPath(string safeName, CancellationToken token, Action<HistoryExportStage> progress, Action<string> diagnostic)
        {
            const int maximumFileCharacters = 32768;
            IntPtr fileBuffer = IntPtr.Zero, filter = IntPtr.Zero, title = IntPtr.Zero, extension = IntPtr.Zero;
            var window = new DialogWindow(new NativeDialogWindows(), diagnostic);
            HookProcedure hook = (child, message, wParam, lParam) =>
            {
                try
                {
                    if (message == 0x0110) // WM_INITDIALOG: Explorer hook receives its child window.
                    {
                        window.Opened(child, GetParent(child));
                        if (token.IsCancellationRequested) window.RequestClose();
                        else progress?.Invoke(HistoryExportStage.DialogOpen);
                    }
                    else if (message == 0x0002) window.Closed(); // WM_DESTROY
                    else if (message == DialogWindow.CancelMessage)
                    {
                        window.ProcessClose();
                        return new UIntPtr(1);
                    }
                }
                catch (Exception ex) { window.Trace("hook_error type=" + ex.GetType().Name); window.RequestClose(); } // Never unwind through a native callback.
                return UIntPtr.Zero;
            };
            try
            {
                fileBuffer = Marshal.AllocHGlobal(maximumFileCharacters * 2);
                filter = Marshal.StringToHGlobalUni("JSON 聊天记录\0*.json\0\0");
                title = Marshal.StringToHGlobalUni("SAKI · 导出本机会话");
                extension = Marshal.StringToHGlobalUni("json");
                var characters = new char[maximumFileCharacters];
                safeName.CopyTo(0, characters, 0, safeName.Length);
                Marshal.Copy(characters, 0, fileBuffer, characters.Length);
                var dialog = new OpenFileName
                {
                    structSize = Marshal.SizeOf(typeof(OpenFileName)),
                    // A Unity HWND owner would disable its window while this modal call waits.
                    owner = IntPtr.Zero, filter = filter, filterIndex = 1,
                    file = fileBuffer, maxFile = maximumFileCharacters,
                    title = title, defaultExtension = extension,
                    hook = Marshal.GetFunctionPointerForDelegate(hook),
                    flags = 0x00080000 | 0x00000800 | 0x00000002 | 0x00000008 | 0x20 | 0x00800000 | 0x02000000
                };
                using (token.Register(window.RequestClose))
                // Only wake the STA hook here. It cancels one innermost dialog at a time.
                // No cross-thread window queries, SendMessage, or main-thread Join.
                using (var closer = new Timer(_ => { if (token.IsCancellationRequested) window.RequestClose(); }, null, 100, 100))
                {
                    token.ThrowIfCancellationRequested();
                    bool selected = GetSaveFileName(ref dialog);
                    int error = selected ? 0 : CommDlgExtendedError();
                    window.Trace("native_return selected=" + selected + " error=" + error);
                    window.Closed();
                    token.ThrowIfCancellationRequested();
                    if (!selected)
                    {
                        if (error != 0) throw new IOException("Native save dialog failed.", error);
                        return null;
                    }
                    // Preserve exactly the native-confirmed path; do not append another suffix.
                    return Path.GetFullPath(Marshal.PtrToStringUni(fileBuffer));
                }
            }
            finally
            {
                window.Closed();
                GC.KeepAlive(hook);
                if (fileBuffer != IntPtr.Zero) Marshal.FreeHGlobal(fileBuffer);
                if (filter != IntPtr.Zero) Marshal.FreeHGlobal(filter);
                if (title != IntPtr.Zero) Marshal.FreeHGlobal(title);
                if (extension != IntPtr.Zero) Marshal.FreeHGlobal(extension);
            }
        }

        internal interface IDialogWindows
        {
            IntPtr OwnedPopup(IntPtr window);
            bool TaskDialog(IntPtr window);
            IntPtr Button(IntPtr window, int id);
            bool Enabled(IntPtr window);
            bool Visible(IntPtr window);
            bool Post(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        }

        private sealed class NativeDialogWindows : IDialogWindows
        {
            public IntPtr OwnedPopup(IntPtr window)
            {
                // GetLastActivePopup may return the disabled save window itself after focus
                // switches to Unity. Enumerate only this STA's visible, directly owned dialogs.
                IntPtr found = IntPtr.Zero;
                EnumWindowProcedure callback = (candidate, _) =>
                {
                    if (GetWindow(candidate, 4) == window && IsWindowVisible(candidate) && DialogClass(candidate))
                    { found = candidate; return false; }
                    return true;
                };
                EnumThreadWindows(GetCurrentThreadId(), callback, IntPtr.Zero);
                GC.KeepAlive(callback);
                return found;
            }
            private static bool DialogClass(IntPtr window)
            {
                var name = new StringBuilder(32);
                return GetClassName(window, name, name.Capacity) != 0 && name.ToString() == "#32770";
            }
            public bool TaskDialog(IntPtr window) => DialogClass(window) && FindWindowEx(window, IntPtr.Zero, "DirectUIHWND", null) != IntPtr.Zero;
            public IntPtr Button(IntPtr window, int id) => GetDlgItem(window, id);
            public bool Enabled(IntPtr window) => IsWindowEnabled(window);
            public bool Visible(IntPtr window) => IsWindowVisible(window);
            public bool Post(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam) => PostMessage(window, message, wParam, lParam);
        }

        internal sealed class DialogWindow
        {
            internal const uint CancelMessage = 0x8541; // Private WM_APP message to the hook child.
            private readonly IDialogWindows windows;
            private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
            private IntPtr hook;
            private IntPtr handle;
            private IntPtr lastCancelledWindow;
            private int pending;
            private int requested, processed, traceCount;
            private string lastState;
            private readonly Action<string> diagnostic;
            public DialogWindow(IDialogWindows windows, Action<string> diagnostic = null) { this.windows = windows; this.diagnostic = diagnostic; }
            public void Trace(string value)
            {
                if (diagnostic == null || Interlocked.Increment(ref traceCount) > 64) return;
                try { diagnostic("ticks=" + System.Diagnostics.Stopwatch.GetTimestamp() + " thread=" + Thread.CurrentThread.ManagedThreadId + " " + value); }
                catch { } // Diagnostic observers must not unwind through any native callback.
            }
            public void Opened(IntPtr child, IntPtr owner)
            {
                OnDialogThread();
                handle = owner;
                Interlocked.Exchange(ref hook, child);
                Trace("opened child=" + child.ToInt64() + " root=" + owner.ToInt64());
            }
            public void Closed()
            {
                OnDialogThread();
                if (handle != IntPtr.Zero) Trace("closed");
                Interlocked.Exchange(ref hook, IntPtr.Zero);
                handle = lastCancelledWindow = IntPtr.Zero;
                Volatile.Write(ref pending, 0);
            }
            public void RequestClose()
            {
                // Called by Unity, the token callback, or the timer. Never query windows or
                // acquire a managed lock that an STA destruction callback would need.
                IntPtr child = Interlocked.CompareExchange(ref hook, IntPtr.Zero, IntPtr.Zero);
                bool first = Interlocked.Exchange(ref requested, 1) == 0;
                if (first) Trace("cancel_request child=" + child.ToInt64());
                if (child == IntPtr.Zero || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
                bool posted = windows.Post(child, CancelMessage, UIntPtr.Zero, IntPtr.Zero);
                if (first || !posted) Trace("wake_post ok=" + posted);
                if (!posted) Volatile.Write(ref pending, 0);
            }
            public void ProcessClose()
            {
                OnDialogThread();
                if (Interlocked.Exchange(ref processed, 1) == 0) Trace("wake_received");
                Volatile.Write(ref pending, 0);
                if (handle == IntPtr.Zero) return;
                IntPtr target = handle;
                for (int depth = 0; depth < 8; depth++)
                {
                    IntPtr popup = windows.OwnedPopup(target);
                    if (popup == IntPtr.Zero || popup == target || !windows.Visible(popup)) break;
                    target = popup;
                    if (depth == 7) return; // Never close a parent when a deeper modal is unresolved.
                }
                bool enabled = windows.Enabled(target), taskDialog = target != handle && windows.TaskDialog(target);
                if (diagnostic != null)
                {
                    string state = "target root=" + handle.ToInt64() + " window=" + target.ToInt64() + " enabled=" + enabled + " direct_ui=" + taskDialog;
                    if (state != lastState) { lastState = state; Trace(state); }
                }
                if (!enabled || target == lastCancelledWindow) return;
                IntPtr button = target != handle ? windows.Button(target, 7) : IntPtr.Zero; // IDNO
                uint command = 7;
                if (button == IntPtr.Zero || !windows.Enabled(button))
                { button = windows.Button(target, 2); command = 2; } // IDCANCEL
                bool posted;
                uint message = 0x0111;
                if (taskDialog && button == IntPtr.Zero)
                {
                    // Common-dialog overwrite prompts on Windows 11 are TaskDialogs with
                    // virtual button IDs. Never send Yes/OK or infer a default affirmative action.
                    message = 0x0400 + 102; command = 7; // TDM_CLICK_BUTTON / IDNO
                    posted = windows.Post(target, message, new UIntPtr(command), IntPtr.Zero);
                }
                else if (button != IntPtr.Zero)
                {
                    if (!windows.Enabled(button)) return;
                    posted = windows.Post(target, 0x0111, new UIntPtr(command), button);
                }
                else if (target == handle)
                    posted = windows.Post(target, 0x0111, new UIntPtr(2), IntPtr.Zero);
                else
                {
                    message = 0x0010; command = 0;
                    posted = windows.Post(target, 0x0010, UIntPtr.Zero, IntPtr.Zero);
                }
                Trace("cancel_post window=" + target.ToInt64() + " message=" + message + " command=" + command + " ok=" + posted);
                // One command per window. In particular, No is never followed by Close or a
                // parent Cancel in the same pump turn. Let that native modal loop unwind first.
                if (posted) lastCancelledWindow = target;
            }
            private void OnDialogThread()
            {
                if (Thread.CurrentThread.ManagedThreadId != threadId)
                    throw new InvalidOperationException("Dialog processing must stay on its STA thread.");
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr owner, instance;
            public IntPtr filter, customFilter;
            public int maxCustomFilter, filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDirectory, title;
            public int flags;
            public short fileOffset, fileExtension;
            public IntPtr defaultExtension;
            public IntPtr customData, hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reservedSize, flagsEx;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate UIntPtr HookProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileName(ref OpenFileName value);
        [DllImport("comdlg32.dll")] private static extern int CommDlgExtendedError();
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)] private delegate bool EnumWindowProcedure(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint threadId, EnumWindowProcedure callback, IntPtr parameter);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
        [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveFileEx(string source, string destination, uint flags);
#endif
    }
}
