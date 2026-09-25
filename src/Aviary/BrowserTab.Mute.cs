using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Experimental → "Mute tabs in the background" (Alpha Build 5 M6 stage 2): only the tab
/// in front of each window can make a sound. The engine's own per-tab mute (<c>IsMuted</c>), set and
/// cleared by Aviary alone — a tab Aviary did not mute is never unmuted by this, and nothing is ever
/// sent to a tab while the experiment is off.
///
/// <para>A tab whose video is in picture in picture keeps its sound: popping a video out is how you
/// watch one while doing something else. Checked when the tab goes to the back (the page's own
/// <c>pictureInPictureElement</c> and document picture in picture). Known limit: a video popped out
/// from inside a FRAME (an embedded player) is not seen from the top of the page — measured 25 Sep
/// (tools/test/frame-pip.js): it goes silent when its tab goes behind, and plays on with the row off.
/// The row says so; seeing into frames would need the engine's per-frame objects (FrameCreated).</para>
/// </summary>
internal sealed partial class BrowserTab
{
    /// <summary>Not the tab in front of its window — set by the window, whether or not the engine is up yet.</summary>
    private bool _inBackground;

    /// <summary>Aviary muted this tab for being in the background; only then does it unmute it.</summary>
    private bool _mutedForBackground;

    public static bool MuteBackground => AppSettings.GetBool("exp.muteBackground");

    public void SetInBackground(bool background)
    {
        _inBackground = background;
        RefreshMute();
    }

    public void RefreshMute() => _ = ApplyMuteAsync();

    private async Task ApplyMuteAsync()
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            bool want = MuteBackground && _inBackground;
            if (!want)
            {
                if (_mutedForBackground)
                {
                    core.IsMuted = false;
                    _mutedForBackground = false;
                }

                return;
            }

            if (_mutedForBackground || await ShowsPictureInPictureAsync(core))
            {
                return;
            }

            // Things may have moved on during that await (switched back to, or the switch turned off).
            if (MuteBackground && _inBackground && !_mutedForBackground)
            {
                core.IsMuted = true;
                _mutedForBackground = true;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: mute in the background");
        }
    }

    private static async Task<bool> ShowsPictureInPictureAsync(CoreWebView2 core)
    {
        try
        {
            string answer = await core.ExecuteScriptAsync(
                "!!(document.pictureInPictureElement || (window.documentPictureInPicture && documentPictureInPicture.window))");
            return answer == "true";
        }
        catch (Exception)
        {
            // A page mid-navigation cannot answer; it has no video popped out that it could keep.
            return false;
        }
    }
}
