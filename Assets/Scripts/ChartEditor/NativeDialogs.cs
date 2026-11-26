using System;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Windows file and message dialogs for the in-game chart editor (comdlg32 / user32).
/// They block until closed, which keeps the editor's "ask, then continue" code simple.
/// On other platforms nothing opens: the file dialogs return null and the message
/// dialogs answer with their default.
/// </summary>
public static class NativeDialogs
{
    public enum Answer
    {
        Yes,
        No,
        Cancel
    }

    public const string JsonFilter = "譜面 (*.json)\0*.json\0すべてのファイル (*.*)\0*.*\0\0";
    public const string AudioFilter = "音声 (*.wav;*.ogg;*.mp3;*.aif;*.aiff)\0*.wav;*.ogg;*.mp3;*.aif;*.aiff\0すべてのファイル (*.*)\0*.*\0\0";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const int MaxPath = 1024;
    private const int OfnOverwritePrompt = 0x2;
    private const int OfnNoChangeDir = 0x8;
    private const int OfnPathMustExist = 0x800;
    private const int OfnFileMustExist = 0x1000;
    private const int OfnExplorer = 0x80000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int structSize;
        public IntPtr owner;
        public IntPtr instance;
        public string filter;
        public string customFilter;
        public int maxCustomFilter;
        public int filterIndex;
        public IntPtr file;
        public int maxFile;
        public string fileTitle;
        public int maxFileTitle;
        public string initialDir;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defaultExtension;
        public IntPtr customData;
        public IntPtr hook;
        public string templateName;
        public IntPtr reserved;
        public int reserved2;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")]
    private static extern bool GetSaveFileName(ref OpenFileName dialog);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private static string ShowFileDialog(bool save, string title, string filter, string initialPath, string defaultExtension)
    {
        string directory = null;
        string fileName = string.Empty;
        if (!string.IsNullOrEmpty(initialPath))
        {
            if (Directory.Exists(initialPath))
            {
                directory = initialPath;
            }
            else
            {
                directory = Path.GetDirectoryName(initialPath);
                fileName = Path.GetFileName(initialPath);
            }
        }

        IntPtr buffer = Marshal.AllocHGlobal(MaxPath * 2);
        try
        {
            char[] initial = new char[MaxPath];
            fileName.CopyTo(0, initial, 0, Math.Min(fileName.Length, MaxPath - 1));
            Marshal.Copy(initial, 0, buffer, MaxPath);

            OpenFileName dialog = new OpenFileName
            {
                owner = GetActiveWindow(),
                filter = filter,
                filterIndex = 1,
                file = buffer,
                maxFile = MaxPath,
                initialDir = directory,
                title = title,
                defaultExtension = defaultExtension,
                flags = OfnExplorer | OfnNoChangeDir | OfnPathMustExist | (save ? OfnOverwritePrompt : OfnFileMustExist)
            };
            dialog.structSize = Marshal.SizeOf(dialog);

            bool ok = save ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog);
            return ok ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Answer ShowMessage(string title, string text, uint type)
    {
        switch (MessageBox(GetActiveWindow(), text, title, type))
        {
            case 6: return Answer.Yes;
            case 7: return Answer.No;
            case 1: return Answer.Yes;
            default: return Answer.Cancel;
        }
    }
#else
    private static string ShowFileDialog(bool save, string title, string filter, string initialPath, string defaultExtension)
    {
        return null;
    }

    private static Answer ShowMessage(string title, string text, uint type)
    {
        return Answer.Yes;
    }
#endif

    /// <returns>The chosen path, or null when cancelled.</returns>
    public static string OpenFile(string title, string filter, string initialPath)
    {
        return ShowFileDialog(false, title, filter, initialPath, null);
    }

    /// <returns>The chosen path, or null when cancelled.</returns>
    public static string SaveFile(string title, string filter, string initialPath, string defaultExtension)
    {
        return ShowFileDialog(true, title, filter, initialPath, defaultExtension);
    }

    /// <summary>OK / Cancel. Returns true on OK.</summary>
    public static bool Confirm(string title, string text)
    {
        // MB_OKCANCEL | MB_ICONQUESTION
        return ShowMessage(title, text, 0x1 | 0x20) == Answer.Yes;
    }

    /// <summary>Yes / No / Cancel (Yes = save).</summary>
    public static Answer AskSave(string title, string text)
    {
        // MB_YESNOCANCEL | MB_ICONQUESTION
        return ShowMessage(title, text, 0x3 | 0x20);
    }

    public static void Notify(string title, string text)
    {
        // MB_OK | MB_ICONINFORMATION
        ShowMessage(title, text, 0x0 | 0x40);
    }
}
