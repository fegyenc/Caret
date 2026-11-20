using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Typedown.WinUI.Services
{
    // Windows Security's "Controlled folder access" (ransomware protection): when it is on, apps that Windows does not know as friendly cannot write into
    // the protected folders (Documents, Pictures, Desktop... and any folder the user added), and the app only gets an error that does not say why ("access
    // denied", or even "could not find file"). After a write failed, this tells whether the folder is one that the protection covers, so that Caret can say
    // so and what to do. It only reads the settings; it never changes them, and it never says more than that the protection may be the reason.
    internal static class ControlledFolderAccess
    {
        private const string DefenderKey = @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";
        private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";

        // The protected folder that holds this path, when the protection is on and blocking; null when it is not, or when that cannot be read.
        public static string ProtectingFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (Mode() != 1) return null;
                return Covering(path, ProtectedFolders());
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        // A failure that a blocked write can cause: access denied, or a file or folder "not found" because it could not be made. A full disk, a name too
        // long or a file in use is something else, and is said as it is.
        public static bool CouldBeBlockedWrite(Exception ex) => ex switch
        {
            UnauthorizedAccessException => true,
            PathTooLongException => false,
            IOException io when io.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027) or unchecked((int)0x80070020) or unchecked((int)0x80070021) => false,
            IOException => true,
            _ => false,
        };

        // 0 off, 1 block, 2 audit only, 3 and 4 the disk-sector variants (they do not protect folders). A policy set by an organisation wins over the
        // setting in Windows Security.
        private static int Mode()
        {
            foreach (var key in new[] { PolicyKey, DefenderKey })
            {
                using var open = Registry.LocalMachine.OpenSubKey(key);
                if (open?.GetValue("EnableControlledFolderAccess") is int mode) return mode;
            }
            return 0;
        }

        // Windows' own list (the user's Documents, Pictures, Videos, Music, Desktop and Favorites, and the public ones) and the folders added by the user
        // or a policy (the names of the values under ProtectedFolders).
        private static IEnumerable<string> ProtectedFolders()
        {
            var special = new[]
            {
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyMusic,
                Environment.SpecialFolder.Desktop, Environment.SpecialFolder.Favorites, Environment.SpecialFolder.CommonDocuments,
                Environment.SpecialFolder.CommonPictures, Environment.SpecialFolder.CommonVideos, Environment.SpecialFolder.CommonMusic,
                Environment.SpecialFolder.CommonDesktopDirectory,
            };
            foreach (var folder in special)
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path)) yield return path;
            }
            foreach (var key in new[] { DefenderKey, PolicyKey })
            {
                using var open = Registry.LocalMachine.OpenSubKey(key + @"\ProtectedFolders");
                if (open == null) continue;
                foreach (var name in open.GetValueNames())
                    if (!string.IsNullOrWhiteSpace(name)) yield return Environment.ExpandEnvironmentVariables(name);
            }
        }

        // The first folder of the list that is the path itself or holds it (case does not matter, "C:\Docs" does not hold "C:\Docs2\a.md").
        internal static string Covering(string path, IEnumerable<string> folders)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var folder in folders)
            {
                string root;
                try { root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
                if (root.Length == 0) continue;
                if (full.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return folder;
            }
            return null;
        }
    }
}
