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
        // Every switch tells every tab where it is; only a tab whose place CHANGED has anything to
        // decide - so a tab switch does not ask each background page about picture in picture again.
        if (_inBackground == background && _muteGeneration > 0)
        {
            return;
        }

        _inBackground = background;
        RefreshMute();
    }

    public void RefreshMute() => _ = ApplyMuteAsync();

    /// <summary>Counts every decision, so an older one still waiting on the page gives way to a newer one.</summary>
    private int _muteGeneration;

    private async Task ApplyMuteAsync()
    {
        int generation = ++_muteGeneration;
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            // Picture in picture is asked EVERY time the tab is behind - also when it is already muted:
            // M6 stage 3's "Pop a playing video out" pops the video out just AFTER the tab went behind,
            // and that tab must be heard again. Nothing is asked while the experiment is off.
            bool want = MuteBackground && _inBackground;
            if (want && await ShowsPictureInPictureAsync(core))
            {
                want = false;
            }

            // A newer decision was made while the page answered (switched back to, the switch
            // turned off, a video popped out): that one stands.
            if (generation != _muteGeneration)
            {
                return;
            }

            if (want && !_mutedForBackground)
            {
                core.IsMuted = true;
                _mutedForBackground = true;
            }
            else if (!want && _mutedForBackground)
            {
                core.IsMuted = false;
                _mutedForBackground = false;
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
