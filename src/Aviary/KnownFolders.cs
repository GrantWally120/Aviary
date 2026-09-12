using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Aviary;

/// <summary>
/// Downloads is the one folder .NET cannot name.
///
/// <see cref="Environment.SpecialFolder"/> covers Desktop, Documents, Music and a dozen
/// others, but not Downloads — it postdates that enumeration and was never added. The
/// settings page used to print a hardcoded "C:\Users\user\Downloads", which is wrong on
/// any machine but this one and wrong on this one the moment the folder is redirected to
/// another drive, which is exactly what people do with Downloads.
/// </summary>
internal static class KnownFolders
{
    private static readonly Guid DownloadsFolderId =
        new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    private static string? _downloads;

    /// <summary>
    /// The real Downloads folder, redirection included. Falls back to
    /// %USERPROFILE%\Downloads, which is where it is when nobody has moved it.
    /// </summary>
    public static string Downloads
    {
        get
        {
            if (_downloads is not null)
            {
                return _downloads;
            }

            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out buffer) == 0
                    && buffer != IntPtr.Zero)
                {
                    _downloads = Marshal.PtrToStringUni(buffer);
                }
            }
            catch (Exception ex)
            {
                // Fall through to the profile-relative guess.
                Diagnostics.Swallowed(ex, "known folders: Downloads");
            }
            finally
            {
                // CoTaskMemFree, not FreeHGlobal: the shell allocated this with the COM
                // task allocator, and freeing it with the wrong one corrupts the heap.
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }

            if (string.IsNullOrEmpty(_downloads))
            {
                _downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
            }

            return _downloads;
        }
    }
}
