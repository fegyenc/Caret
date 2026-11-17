using System;
using System.IO;
using Typedown.WinUI.Services;
using Xunit;

namespace Caret.ConverterTests
{
    // Which folder of Windows' Controlled folder access covers a file that could not be written, and which errors a blocked write can cause.
    // The settings themselves are the PC's (never changed by Caret, never by a test): only the pure parts are tested here.
    public class ControlledFolderAccessTests
    {
        [Fact]
        public void A_file_in_a_protected_folder_or_below_it_is_covered()
        {
            var folders = new[] { @"C:\Users\ana\Desktop", @"D:\OneDrive\Documents" };
            Assert.Equal(@"D:\OneDrive\Documents", ControlledFolderAccess.Covering(@"D:\OneDrive\Documents\notes\a.md", folders));
            Assert.Equal(@"D:\OneDrive\Documents", ControlledFolderAccess.Covering(@"d:\onedrive\documents\A.MD", folders));
            Assert.Equal(@"C:\Users\ana\Desktop", ControlledFolderAccess.Covering(@"C:\Users\ana\Desktop", folders));
        }

        [Fact]
        public void A_folder_whose_name_only_starts_the_same_does_not_cover()
        {
            var folders = new[] { @"D:\Docs\" };
            Assert.Null(ControlledFolderAccess.Covering(@"D:\Docs2\a.md", folders));
            Assert.Null(ControlledFolderAccess.Covering(@"E:\Docs\a.md", folders));
            Assert.Equal(@"D:\Docs\", ControlledFolderAccess.Covering(@"D:\Docs\a.md", folders));
        }

        [Fact]
        public void A_name_in_the_list_that_is_not_a_path_is_passed_over()
        {
            Assert.Equal(@"D:\Docs", ControlledFolderAccess.Covering(@"D:\Docs\a.md", new[] { "", "\0bad", @"D:\Docs" }));
        }

        [Fact]
        public void Only_errors_a_blocked_write_can_cause_count()
        {
            Assert.True(ControlledFolderAccess.CouldBeBlockedWrite(new UnauthorizedAccessException()));
            Assert.True(ControlledFolderAccess.CouldBeBlockedWrite(new FileNotFoundException()));
            Assert.True(ControlledFolderAccess.CouldBeBlockedWrite(new IOException("blocked")));
            Assert.False(ControlledFolderAccess.CouldBeBlockedWrite(new IOException("disk full", unchecked((int)0x80070070))));
            Assert.False(ControlledFolderAccess.CouldBeBlockedWrite(new IOException("in use", unchecked((int)0x80070020))));
            Assert.False(ControlledFolderAccess.CouldBeBlockedWrite(new PathTooLongException()));
            Assert.False(ControlledFolderAccess.CouldBeBlockedWrite(new InvalidOperationException()));
        }

        [Fact]
        public void No_path_is_never_covered()
        {
            Assert.Null(ControlledFolderAccess.ProtectingFolder(null));
            Assert.Null(ControlledFolderAccess.ProtectingFolder(""));
        }
    }
}
