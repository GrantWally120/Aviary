using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Beta Prep 2 — "What websites may ask for" (Settings → Content): location, camera, microphone and
/// notifications, each <b>Ask</b> (the engine's own question, per site, as before) or <b>Block</b>
/// (refused for every site, without a question).
///
/// <para>Block is a rule, not a remembered answer: the request is denied when it is made
/// (<c>BrowserTab.OnPermissionRequested</c>) and nothing is saved for the site. But a site that was
/// ALLOWED earlier holds a saved grant, and the engine does not ask Aviary again for one — so when a
/// kind is switched to Block its saved grants are taken back (<see cref="RevokeAsync"/>), and
/// "Forget every site's answers" takes back every saved answer of every kind.</para>
///
/// <para>Insecure pages never get any of these whatever is chosen (SecurityPolicy), and a private
/// window's answers are never saved.</para>
/// </summary>
internal static class SitePermissions
{
    public const string Ask = "Ask";
    public const string Block = "Block";

    /// <summary>Each row's setting key and the kind of request it governs.</summary>
    public static readonly IReadOnlyDictionary<string, CoreWebView2PermissionKind> Kinds =
        new Dictionary<string, CoreWebView2PermissionKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["permLocation"] = CoreWebView2PermissionKind.Geolocation,
            ["permCamera"] = CoreWebView2PermissionKind.Camera,
            ["permMicrophone"] = CoreWebView2PermissionKind.Microphone,
            ["permNotifications"] = CoreWebView2PermissionKind.Notifications,
        };

    public static bool Blocked(CoreWebView2PermissionKind kind)
    {
        foreach (KeyValuePair<string, CoreWebView2PermissionKind> row in Kinds)
        {
            if (row.Value == kind)
            {
                return IsBlock(AppSettings.Get(row.Key));
            }
        }

        return false;
    }

    public static bool IsBlock(string value) => string.Equals(value, Block, StringComparison.OrdinalIgnoreCase);

    /// <summary>The names of the kinds set to Block, for Safety Check: "location", "notifications".</summary>
    public static List<string> BlockedNames()
    {
        List<string> names = new();
        if (IsBlock(AppSettings.Get("permLocation"))) names.Add("location");
        if (IsBlock(AppSettings.Get("permCamera"))) names.Add("camera");
        if (IsBlock(AppSettings.Get("permMicrophone"))) names.Add("microphone");
        if (IsBlock(AppSettings.Get("permNotifications"))) names.Add("notifications");
        return names;
    }

    /// <summary>
    /// Takes back saved answers in this profile: only <paramref name="only"/>'s when given, and then
    /// only the ALLOWED ones (a kind just set to Block); every kind's and every answer otherwise.
    /// Returns how many were taken back.
    /// </summary>
    public static async Task<int> RevokeAsync(CoreWebView2Profile profile, CoreWebView2PermissionKind? only)
    {
        int revoked = 0;
        IReadOnlyList<CoreWebView2PermissionSetting> saved = await profile.GetNonDefaultPermissionSettingsAsync();
        foreach (CoreWebView2PermissionSetting setting in saved)
        {
            if (only is { } kind
                && (setting.PermissionKind != kind || setting.PermissionState != CoreWebView2PermissionState.Allow))
            {
                continue;
            }

            await profile.SetPermissionStateAsync(setting.PermissionKind, setting.PermissionOrigin, CoreWebView2PermissionState.Default);
            revoked++;
        }

        return revoked;
    }
}
