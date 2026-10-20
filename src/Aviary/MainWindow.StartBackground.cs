using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Aviary;

/// <summary>
/// Choosing what is behind the Start Page (Alpha Build 5 M4.9), and the file picker that goes with
/// "Your picture…" — the first time Aviary has asked Windows to open one.
///
/// <para><b>The handle.</b> A WinUI 3 desktop app must tell a picker which window it belongs to:
/// <c>FileOpenPicker</c> is a WinRT type that expects a CoreWindow, and a desktop app has none, so
/// without being given a window handle it throws "invalid window handle" rather than opening.
/// <c>WinRT.Interop.InitializeWithWindow.Initialize</c> is how that is done — and it has to be that,
/// not a hand-declared <c>IInitializeWithWindow</c> interface with a cast: a projected WinRT object
/// is not a plain COM object, so the cast throws InvalidCastException and the picker never opens.
/// Written the wrong way first, and it was Aviary's own diagnostics that said why, by name, on the
/// first run — which is the whole argument for collecting swallowed errors.</para>
///
/// <para><b>What the picker is allowed to do.</b> Only read, only pictures, one file. What comes
/// back is a path, and a path is not trusted: <see cref="StartBackground.Adopt"/> reads the bytes,
/// checks they really are an image by their first bytes rather than their name, refuses anything
/// over 24 MB, and copies them into the profile. Nothing outside the profile is read again after
/// that, so the Start Page can never be made to read an arbitrary file on every launch.</para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The Start Page (or Settings) asked for a background. Everything but "Your picture" is just a
    /// saved word; that one opens the picker, and only changes the setting if a picture was really
    /// adopted — cancelling leaves the page exactly as it was.
    /// </summary>
    private async Task ChooseStartBackgroundAsync(string choice, string from)
    {
        try
        {
            if (!Array.Exists(StartBackground.Choices, c => string.Equals(c, choice, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (!string.Equals(choice, StartBackground.OwnPicture, StringComparison.OrdinalIgnoreCase))
            {
                // A drawn background was chosen, so the copied picture is no longer wanted.
                StartBackground.Forget();
                AppSettings.Set("startBackground", choice);
                DiagFeature("start-background", new { choice, from });
                PushToEverySettingsPage("startBackground", choice);
                RefreshStartPages();
                return;
            }

            string? path = await PickAPictureAsync();
            if (path is null)
            {
                // Cancelled. The page has already ticked the row it was told to tick, so it is
                // sent back what is actually saved.
                PushToEverySettingsPage("startBackground", AppSettings.Get("startBackground"));
                RefreshStartPages();
                return;
            }

            if (!StartBackground.Adopt(path))
            {
                // Said on the page, where the choice was made - not left in a log nobody reads.
                _backgroundProblem = "Aviary could not use that file. Pick a PNG, JPEG, GIF, WebP or BMP under 24 MB.";
                PushToEverySettingsPage("startBackground", AppSettings.Get("startBackground"));
                RefreshStartPages();
                return;
            }

            AppSettings.Set("startBackground", StartBackground.OwnPicture);

            // The file's name and where it came from are not recorded: a path is a piece of the
            // person's disk, and the only thing worth knowing is that they chose a picture.
            DiagFeature("start-background", new { choice = StartBackground.OwnPicture, from });
            PushToEverySettingsPage("startBackground", StartBackground.OwnPicture);
            RefreshStartPages();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start background: choose");
            PushToEverySettingsPage("startBackground", AppSettings.Get("startBackground"));
        }
    }

    /// <summary>
    /// Why the last chosen picture was refused, shown once on the next Start Page and then
    /// forgotten. Read by the page's payload, which clears it.
    /// </summary>
    private static string? _backgroundProblem;

    /// <summary>Taken, not peeked at: the message is shown once.</summary>
    internal static string? TakeBackgroundProblem()
    {
        string? problem = _backgroundProblem;
        _backgroundProblem = null;
        return problem;
    }

    /// <summary>The picker, or null when it was cancelled or could not open.</summary>
    private async Task<string?> PickAPictureAsync()
    {
        try
        {
            FileOpenPicker picker = new()
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };

            foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            StorageFile? file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start background: open the picker");
            return null;
        }
    }
}
