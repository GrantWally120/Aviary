using System.Text.Json;

namespace Aviary;

/// <summary>
/// The single source of truth for what build this is.
///
/// Before Phase 4 there were four: the About pane said "0.2.0 · alpha build 2", the About
/// pane's Phase row said "02 — Tabs and settings", the Start Page footer said "Alpha build
/// 2", and every deferred settings row carried a hand-typed "Phase N" badge. Phase 3
/// shipped and all four stayed where they were, so the app spent a whole phase describing
/// itself as the previous one.
///
/// Nothing anywhere else may hardcode a version, a channel or a phase number. Internal
/// pages receive these as substitution markers; the phase BADGES are derived by comparing
/// a row's numeric phase against <see cref="PhaseNumber"/>, so a badge cannot outlive the
/// phase it names.
/// </summary>
internal static class BuildInfo
{
    public const string Version = "0.4.0";

    /// <summary>"alpha" through Phase 05; "beta" from Phase 06.</summary>
    public const string Track = "alpha";

    /// <summary>Which build of that track. Bumped with the phase, not with every compile.</summary>
    public const int Build = 4;

    /// <summary>The phase currently being built. This is what About reports.</summary>
    public const int PhaseNumber = 4;

    public const string PhaseName = "Safari features";

    /// <summary>
    /// The last phase actually FINISHED, which is what gates the settings rows.
    ///
    /// It is deliberately not <see cref="PhaseNumber"/>. Gating on the phase in progress
    /// would un-badge every row the current phase is going to deliver the moment that
    /// phase opened — so a row would claim to work for the whole time it was being built.
    /// Rows carry the phase they arrive in and stay badged until that phase is done; when
    /// this number is bumped, they all go live together, by which point they all work.
    /// </summary>
    public const int PhaseShipped = 4;

    /// <summary>"alpha build 4".</summary>
    public static string Channel => Track + " build " + Build;

    /// <summary>"04 — Safari features".</summary>
    public static string PhaseLabel =>
        (PhaseNumber < 10 ? "0" : "") + PhaseNumber + " — " + PhaseName;

    /// <summary>The blob injected into every internal page at the build marker.</summary>
    public static string Json() => JsonSerializer.Serialize(new
    {
        version = Version,
        track = Track,
        build = Build,
        channel = Channel,
        phaseNumber = PhaseNumber,
        phaseShipped = PhaseShipped,
        phaseName = PhaseName,
        phaseLabel = PhaseLabel,
    });
}
