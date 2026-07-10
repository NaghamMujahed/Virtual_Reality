using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PaintBucketSim.Systems.UI
{
    internal static class NativeSaveFileDialog
    {
        public static string SavePng(
            string title,
            string initialDirectory,
            string defaultFileName)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return ShowWindowsSaveDialog(
                title,
                initialDirectory,
                defaultFileName);
#else
            string folder = string.IsNullOrWhiteSpace(initialDirectory)
                ? Application.persistentDataPath
                : initialDirectory;
            return Path.Combine(folder, defaultFileName);
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int OfnOverwritePrompt = 0x00000002;
        private const int OfnPathMustExist = 0x00000800;
        private const int OfnNoChangeDirectory = 0x00000008;
        private const int MaxPathBuffer = 4096;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr ownerWindow;
            public IntPtr instance;
            public IntPtr filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDirectory;
            public IntPtr title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public IntPtr defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reservedValue;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileNameW(ref OpenFileName fileName);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static string ShowWindowsSaveDialog(
            string title,
            string initialDirectory,
            string defaultFileName)
        {
            string folder = string.IsNullOrWhiteSpace(initialDirectory)
                ? Application.persistentDataPath
                : initialDirectory;
            string fileName = string.IsNullOrWhiteSpace(defaultFileName)
                ? "paint_board.png"
                : defaultFileName;
            string initialPath = Path.Combine(folder, fileName);
            string dialogTitle = string.IsNullOrWhiteSpace(title)
                ? "Export Paint Board"
                : title;

            IntPtr fileBuffer = IntPtr.Zero;
            IntPtr filterBuffer = IntPtr.Zero;
            IntPtr folderBuffer = IntPtr.Zero;
            IntPtr titleBuffer = IntPtr.Zero;
            IntPtr extensionBuffer = IntPtr.Zero;
            try
            {
                fileBuffer = Marshal.AllocHGlobal(MaxPathBuffer * sizeof(char));
                ZeroMemory(fileBuffer, MaxPathBuffer * sizeof(char));
                char[] initialPathChars = (initialPath + '\0').ToCharArray();
                Marshal.Copy(
                    initialPathChars,
                    0,
                    fileBuffer,
                    Mathf.Min(initialPathChars.Length, MaxPathBuffer));

                filterBuffer = Marshal.StringToHGlobalUni(
                    "PNG Image (*.png)\0*.png\0All Files (*.*)\0*.*\0\0");
                folderBuffer = Marshal.StringToHGlobalUni(folder);
                titleBuffer = Marshal.StringToHGlobalUni(dialogTitle);
                extensionBuffer = Marshal.StringToHGlobalUni("png");

                OpenFileName dialog = new OpenFileName
                {
                    structSize = Marshal.SizeOf(typeof(OpenFileName)),
                    ownerWindow = GetActiveWindow(),
                    filter = filterBuffer,
                    filterIndex = 1,
                    file = fileBuffer,
                    maxFile = MaxPathBuffer,
                    initialDirectory = folderBuffer,
                    title = titleBuffer,
                    flags = OfnOverwritePrompt | OfnPathMustExist | OfnNoChangeDirectory,
                    defaultExtension = extensionBuffer
                };

                if (!GetSaveFileNameW(ref dialog))
                {
                    int error = CommDlgExtendedError();
                    if (error != 0)
                        Debug.LogWarning($"Save dialog failed with Windows error 0x{error:X}.");
                    return null;
                }

                string selectedPath = Marshal.PtrToStringUni(fileBuffer);
                if (string.IsNullOrWhiteSpace(selectedPath))
                    return null;

                return string.Equals(
                    Path.GetExtension(selectedPath),
                    ".png",
                    StringComparison.OrdinalIgnoreCase)
                    ? selectedPath
                    : selectedPath + ".png";
            }
            finally
            {
                FreeBuffer(fileBuffer);
                FreeBuffer(filterBuffer);
                FreeBuffer(folderBuffer);
                FreeBuffer(titleBuffer);
                FreeBuffer(extensionBuffer);
            }
        }

        private static void ZeroMemory(IntPtr buffer, int byteCount)
        {
            byte[] zeros = new byte[byteCount];
            Marshal.Copy(zeros, 0, buffer, byteCount);
        }

        private static void FreeBuffer(IntPtr buffer)
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
#endif
    }
}
