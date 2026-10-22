using System;
using System.Linq;

namespace Aviary;

/// <summary>
/// Which sites are told "Safari" when "Identify as Safari" is on (Settings, Privacy).
///
/// The identity is a Safari user agent over a Chromium engine, which a few sites can tell. YouTube
/// is the one that matters: with the identity on, a video plays for 35-45 seconds and then the
/// player stops with "Something went wrong" (found 23 Sep 2026 in the playtest review; reproduced
/// 22 Oct on 0.5.0 - 3 of 3 with it on, none in 90 s with it off, same video and settings otherwise).
/// So those sites are shown the engine's own identity instead, whatever the switch says. Kept in
/// one pure place so the check is testable without a window (tools/test/security).
/// </summary>
internal static class SafariIdentity
{
    /// <summary>Registrable domains that break under the Safari identity. Subdomains follow.</summary>
    private static readonly string[] BreaksUnderSafari =
    {
        "youtube.com", "youtu.be", "youtube-nocookie.com",
    };

    internal static bool BreaksSite(string host) =>
        host.Length > 0
        && BreaksUnderSafari.Contains(PublicSuffix.RegistrableDomain(host), StringComparer.OrdinalIgnoreCase);

    /// <summary>The identity a page on <paramref name="host"/> should be requested with, given the switch.</summary>
    internal static bool WantedFor(string host, bool switchedOn) => switchedOn && !BreaksSite(host);
}
