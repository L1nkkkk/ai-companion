using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.UI
{
    /// <summary>One independent STA save window; no Unity APIs are called by its worker.</summary>
    public static class HistoryFileExport
    {
        private static int active;

        public static Task<string> SaveAsync(HistoryExport export, CancellationToken token, Action<HistoryExportStage> progress = null)
        {
            if (export == null || export.SchemaVersion != 1 || export.MediaType != "application/json" ||
                export.ConversationId == Guid.Empty || export.Utf8Json.Length > 2 * 1024 * 1024)
                throw new ArgumentException("Invalid history export.");
            string safeName = "conversation-" + export.ConversationId.ToString("N") + ".json";
            return RunOnStaAsync(cancel =>
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                string selected = SelectPath(safeName, cancel, progress);
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
        private static string SelectPath(string safeName, CancellationToken token, Action<HistoryExportStage> progress)
        {
            const int maximumFileCharacters = 32768;
            IntPtr fileBuffer = IntPtr.Zero, filter = IntPtr.Zero, title = IntPtr.Zero, extension = IntPtr.Zero;
            var window = new DialogWindow();
            HookProcedure hook = (child, message, wParam, lParam) =>
            {
                try
                {
                    if (message == 0x0110) // WM_INITDIALOG: Explorer hook receives its child window.
                    {
                        window.Opened(GetParent(child));
                        if (token.IsCancellationRequested) window.RequestClose();
                        else progress?.Invoke(HistoryExportStage.DialogOpen);
                    }
                    else if (message == 0x0002) window.Closed(); // WM_DESTROY
                }
                catch { window.RequestClose(); } // Never unwind through a native callback.
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
                // A nested overwrite/error prompt can temporarily disable the save window.
                // Repeated posts close it safely without SendMessage or a main-thread Join.
                using (var closer = new Timer(_ => { if (token.IsCancellationRequested) window.RequestClose(); }, null, 100, 100))
                {
                    token.ThrowIfCancellationRequested();
                    bool selected = GetSaveFileName(ref dialog);
                    int error = selected ? 0 : CommDlgExtendedError();
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

        private sealed class DialogWindow
        {
            private readonly object gate = new object();
            private IntPtr handle;
            public void Opened(IntPtr value) { lock (gate) handle = value; }
            public void Closed() { lock (gate) handle = IntPtr.Zero; }
            public void RequestClose()
            {
                lock (gate)
                {
                    if (handle == IntPtr.Zero) return;
                    IntPtr popup = GetLastActivePopup(handle);
                    for (int depth = 0; depth < 8 && popup != IntPtr.Zero && popup != handle; depth++)
                    {
                        // Cancellation must never answer Yes/OK to an overwrite prompt.
                        IntPtr no = GetDlgItem(popup, 7); // IDNO, otherwise IDCANCEL.
                        PostMessage(popup, 0x0111, new UIntPtr(no != IntPtr.Zero ? 7u : 2u), IntPtr.Zero);
                        PostMessage(popup, 0x0010, UIntPtr.Zero, IntPtr.Zero);
                        IntPtr next = GetLastActivePopup(popup);
                        if (next == popup) break;
                        popup = next;
                    }
                    PostMessage(handle, 0x0010, UIntPtr.Zero, IntPtr.Zero);
                }
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
        [DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveFileEx(string source, string destination, uint flags);
#endif
    }
}
