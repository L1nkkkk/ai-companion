using System;
using System.IO;
using System.Runtime.InteropServices;
using AICompanion.Preview.Contracts;

namespace AICompanion.Preview.UI
{
    /// <summary>UI-owned save dialog; service DTOs never control filesystem paths.</summary>
    public static class HistoryFileExport
    {
        public static string Save(HistoryExport export)
        {
            if (export == null || export.SchemaVersion != 1 || export.MediaType != "application/json" ||
                export.ConversationId == Guid.Empty || export.Utf8Json.Length > 2 * 1024 * 1024)
                throw new ArgumentException("Invalid history export.");
            string safeName = "conversation-" + export.ConversationId.ToString("N") + ".json";
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            const int maximumFileCharacters = 32768;
            IntPtr fileBuffer = Marshal.AllocHGlobal(maximumFileCharacters * 2);
            IntPtr filter = Marshal.StringToHGlobalUni("JSON 聊天记录\0*.json\0\0");
            IntPtr title = Marshal.StringToHGlobalUni("导出本机会话");
            IntPtr extension = Marshal.StringToHGlobalUni("json");
            try
            {
            var characters = new char[maximumFileCharacters];
            safeName.CopyTo(0, characters, 0, safeName.Length);
            Marshal.Copy(characters, 0, fileBuffer, characters.Length);
            var dialog = new OpenFileName
            {
                structSize = Marshal.SizeOf(typeof(OpenFileName)),
                owner = GetActiveWindow(), filter = filter, filterIndex = 1,
                file = fileBuffer, maxFile = maximumFileCharacters,
                title = title, defaultExtension = extension,
                flags = 0x00080000 | 0x00000800 | 0x00000002 | 0x00000008
            };
            if (!GetSaveFileName(ref dialog))
            {
                int error = CommDlgExtendedError();
                if (error != 0)
                {
                    UnityEngine.Debug.LogWarning("HISTORY_SAVE_DIALOG_FAILED native=" + error);
                    throw new IOException("Native save dialog error " + error + ".");
                }
                return null;
            }
            string selected = Path.GetFullPath(Marshal.PtrToStringUni(fileBuffer));
            // Keep exactly the path approved by the native dialog. Appending a suffix here
            // could overwrite a different existing file without its overwrite confirmation.
            File.WriteAllBytes(selected, export.Utf8Json.ToArray());
            return selected;
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer); Marshal.FreeHGlobal(filter);
                Marshal.FreeHGlobal(title); Marshal.FreeHGlobal(extension);
            }
#else
            throw new PlatformNotSupportedException("History export currently requires Windows.");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
        [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileName(ref OpenFileName value);
        [DllImport("comdlg32.dll")] private static extern int CommDlgExtendedError();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
#endif
    }
}
