using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// What is behind the Start Page (Alpha Build 5 M4.9) — Chrome's idea, from Grant's pick.
///
/// <para>Five of the six choices cost nothing: they are drawn by the page itself out of CSS and the
/// theme's own tokens, so every one follows Classic/Liquid Glass and light/dark with no second
/// image and no licensing question. That is the same decision the glasshouse was built on.</para>
///
/// <para>The sixth is the person's own picture, and that is what this class is for.</para>
///
/// <para><b>Why it is copied into the profile.</b> The picker hands back a path somewhere on the
/// disk. Keeping the path would mean the Start Page reads an arbitrary file on every launch, for
/// ever — a file that may move, be deleted, be on a drive that is not there, or be replaced by
/// something else entirely. So the bytes are copied once, into the profile, and nothing outside the
/// profile is ever read again.</para>
///
/// <para><b>How the page reaches it.</b> A NavigateToString document has an opaque origin and cannot
/// load <c>file://</c> at all, and base64 in the markup would put a photograph inside the document
/// on every render. So it is served the way site icons already are: over the synthetic
/// <c>aviary.localhost</c> host, answered from memory inside WebResourceRequested, never touching
/// the network. The token in the path is per-process, like the icons' — a website that guessed the
/// host still cannot guess the path.</para>
/// </summary>
internal static class StartBackground
{
    /// <summary>The settings value that means "the picture in the profile".</summary>
    public const string OwnPicture = "Your picture";

    /// <summary>
    /// The choices, in the order they are offered. Each drawn one is a CSS gradient the page
    /// builds from theme tokens; "Glasshouse" is the drawn dome that has always been there.
    /// </summary>
    public static readonly string[] Choices =
    {
        "Glasshouse", "Plain", "Dawn", "Dusk", "Moss", OwnPicture,
    };

    /// <summary>
    /// What is actually behind the page — which is not always what was saved. If the picture was
    /// deleted from outside Aviary, "Your picture" has stopped meaning anything, so the STORE is
    /// corrected rather than the page being taught to read a value that lies (bug 73's rule: when a
    /// saved value stops being true, migrate it — do not paper over it at the other end, or the
    /// round-trip check will rightly report the page and the store disagreeing).
    /// </summary>
    public static string Current
    {
        get
        {
            string saved = AppSettings.Get("startBackground");

            if (string.Equals(saved, OwnPicture, StringComparison.OrdinalIgnoreCase) && !HavePicture)
            {
                AppSettings.Set("startBackground", "Glasshouse");
                return "Glasshouse";
            }

            return Choices.Contains(saved, StringComparer.OrdinalIgnoreCase) ? saved : "Glasshouse";
        }
    }

    private static string FilePath => AppPaths.Of("startbackground.img");

    private static string? _token;
    private static byte[]? _bytes;
    private static string _mime = "image/png";

    /// <summary>True when there is a picture in the profile to show.</summary>
    public static bool HavePicture => File.Exists(FilePath);

    /// <summary>
    /// The address the Start Page points at, or an empty string when there is nothing to point at.
    /// Reading it loads the bytes once per process, so TryServe can stay synchronous.
    /// </summary>
    public static string Url
    {
        get
        {
            try
            {
                if (!HavePicture)
                {
                    return string.Empty;
                }

                if (_bytes is null)
                {
                    _bytes = File.ReadAllBytes(FilePath);
                    _mime = SniffMime(_bytes) ?? "image/png";
                }

                _token ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

                // The length is in the address so a newly chosen picture is a new address: the page
                // would otherwise show the one the engine already had under the same name.
                return "https://aviary.localhost/background/" + _token + "/" + _bytes.Length;
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "start background: read the picture");
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Copies a chosen picture into the profile, replacing whatever was there. Returns false when
    /// the file could not be read or is not an image this can serve — the caller says so rather
    /// than leaving the setting pointing at nothing.
    /// </summary>
    public static bool Adopt(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);

            // 24 MB is far more than any wallpaper needs and still small enough to hold in memory;
            // the point is that a mis-picked video file cannot be adopted.
            if (bytes.Length == 0 || bytes.Length > 24 * 1024 * 1024 || SniffMime(bytes) is null)
            {
                Diagnostics.Anomaly("start-background-refused",
                    "the chosen file is not an image this can show", new { bytes = bytes.Length });
                return false;
            }

            // Written beside the real name and moved into place, so a failed write never leaves a
            // half-copied picture behind — the same way the filter lists are saved.
            string temp = FilePath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, FilePath, overwrite: true);

            _bytes = bytes;
            _mime = SniffMime(bytes) ?? "image/png";
            _token = null;   // a new address for the new picture
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start background: adopt a picture");
            return false;
        }
    }

    /// <summary>
    /// Forgets the picture entirely — the file goes with it. Called when a drawn background is
    /// chosen instead: "Your picture…" always opens the picker, so a copy of someone's photograph
    /// has no reason to keep sitting in the profile once they have moved on from it.
    /// </summary>
    public static void Forget()
    {
        try
        {
            _bytes = null;
            _token = null;
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start background: forget the picture");
        }
    }

    /// <summary>
    /// Answers the Start Page's request for the picture. Called before the icons' server, which
    /// answers every other request to that host.
    /// </summary>
    public static bool TryServe(string requestUri, CoreWebView2Environment env, out CoreWebView2WebResourceResponse? response)
    {
        response = null;

        if (_token is null || _bytes is null
            || !Uri.TryCreate(requestUri, UriKind.Absolute, out Uri? uri)
            || !uri.Host.Equals("aviary.localhost", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/background/" + _token + "/", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            // NOT inside a using: WebView2 reads the stream after this returns (Favicons.TryServe
            // has the same note, and the same bug once produced an empty image in silence).
            MemoryStream stream = new(_bytes);
            response = env.CreateWebResourceResponse(
                stream.AsRandomAccessStream(), 200, "OK",
                "Content-Type: " + _mime + "\r\nCache-Control: no-store");
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start background: serve the picture");
            return false;
        }
    }

    /// <summary>
    /// What the bytes actually are, not what the name claims. A file called .png that is really
    /// something else would either not draw or not be an image at all.
    /// </summary>
    private static string? SniffMime(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') { return "image/png"; }
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) { return "image/jpeg"; }
        if (b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') { return "image/gif"; }
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
            && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') { return "image/webp"; }
        if (b.Length > 2 && b[0] == 'B' && b[1] == 'M') { return "image/bmp"; }
        return null;
    }
}
