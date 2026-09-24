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
/// Nothing anywhere else may hardcode a version, a build name or a phase number. Internal
/// pages receive these as substitution markers and the injected JSON; the row BADGES are
/// derived by comparing a row's numeric phase against <see cref="PhaseShipped"/>, so a badge
/// cannot outlive the build it names.
///
/// <para><b>Phases ARE builds</b> (Grant, 13 Sep 2026). Phases 1–5 are Alpha Build 1 to 5,
/// phase 6 is Beta Build 1 and phase 7 Beta Build 2. Phase 0 was the interface study — a
/// mockup, not a build. The numbering stays underneath because the plan, the handoff and
/// every row's <c>phase:N</c> use it; what people see is the build name, from
/// <see cref="NameOf"/>.</para>
/// </summary>
internal static class BuildInfo
{
    public const string Version = "0.5.0";

    /// <summary>The phase — the build — currently being made. About and Updates report it.</summary>
    public const int PhaseNumber = 5;

    /// <summary>This build's headline. Each build has one, the way a named update does.</summary>
    /// <summary>
    /// Grant renamed it on 20 September 2026, at M4.6: Alpha Build 5 began on 10 September and has
    /// run M0, M1, M2, M2.5, M3, M4, M4.5 and M4.6 - far more than any build before it.
    /// </summary>
    public const string PhaseName = "The Long Run";

    /// <summary>
    /// The last phase actually FINISHED, which is what gates the settings rows.
    ///
    /// It is deliberately not <see cref="PhaseNumber"/>. Gating on the build in progress
    /// would un-badge every row that build is going to deliver the moment it opened — so a
    /// row would claim to work for the whole time it was being built. Rows carry the phase
    /// they arrive in and stay badged until that build is done; when this number is bumped,
    /// they all go live together, by which point they all work.
    /// </summary>
    public const int PhaseShipped = 4;

    /// <summary>
    /// How many of this build's milestones are finished, counted in order (PHASE5-PLAN.md: M0,
    /// M1, M2, M2.5 — Grant's "middle update", added 13 Sep — then M3 to M8, so ten). Bump it
    /// when a milestone ships; Updates ticks them off from this one number.
    /// M4.7 "Immediate Fix" (21 Sep) is the ninth: the menus M4.6 broke, put right.
    /// M4.8 "A Iconic Fix" (21 Sep) is the tenth, and M4.9 "Birds News" (24 Sep) the eleventh —
    /// counted here only once all three of its stages had landed, which they have.
    /// </summary>
    public const int MilestonesDone = 11;

    public const int Milestones = 15;

    /// <summary>The last alpha. Phase 6 onwards is beta.</summary>
    private const int LastAlphaPhase = 5;

    /// <summary>"alpha" up to Alpha Build 5; "beta" from Beta Build 1.</summary>
    public static string Track => PhaseNumber <= LastAlphaPhase ? "alpha" : "beta";

    /// <summary>Which build of that track: 5 for Alpha Build 5, 1 for Beta Build 1.</summary>
    public static int Build => PhaseNumber <= LastAlphaPhase ? PhaseNumber : PhaseNumber - LastAlphaPhase;

    /// <summary>True while the build in progress has not shipped — every build until its last milestone.</summary>
    public static bool PreRelease => PhaseShipped < PhaseNumber;

    /// <summary>"Alpha Build 5" for phase 5, "Beta Build 1" for phase 6.</summary>
    public static string NameOf(int phase) =>
        phase <= LastAlphaPhase ? "Alpha Build " + phase : "Beta Build " + (phase - LastAlphaPhase);

    /// <summary>"AB5" for phase 5, "BB1" for phase 6 — Grant's own shorthand.</summary>
    public static string CodeOf(int phase) =>
        phase <= LastAlphaPhase ? "AB" + phase : "BB" + (phase - LastAlphaPhase);

    /// <summary>"Alpha Build 5".</summary>
    public static string BuildName => NameOf(PhaseNumber);

    /// <summary>"AB5".</summary>
    public static string BuildCode => CodeOf(PhaseNumber);

    /// <summary>"Alpha Build 5 (pre-release)" — what the app calls itself everywhere.</summary>
    public static string Channel => BuildName + (PreRelease ? " (pre-release)" : "");

    /// <summary>"05 — Privacy that works".</summary>
    public static string PhaseLabel =>
        (PhaseNumber < 10 ? "0" : "") + PhaseNumber + " — " + PhaseName;

    /// <summary>"4 of 10 milestones done" while a pre-release; empty once the build ships.</summary>
    public static string Progress =>
        PreRelease ? MilestonesDone + " of " + Milestones + " milestones done" : string.Empty;

    /// <summary>
    /// When this copy was compiled — the write time of Aviary.dll, which a publish copies
    /// unchanged. Local time to the minute, which is all Updates shows. Empty if unreadable.
    /// Read on first use, not in a static initialiser: a failure reports through Diagnostics,
    /// which itself reads this class while it starts.
    /// </summary>
    public static string BuiltOn => _builtOn ??= ReadBuiltOn();

    private static string? _builtOn;

    private static string ReadBuiltOn()
    {
        try
        {
            string path = typeof(BuildInfo).Assembly.Location;
            if (path.Length == 0 || !File.Exists(path))
            {
                return string.Empty;
            }

            return File.GetLastWriteTime(path)
                .ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "build: read compile time");
            return string.Empty;
        }
    }

    /// <summary>
    /// The blob injected into every internal page at the build marker. <paramref name="engine"/>
    /// is the running WebView2's version, known only once a tab has started.
    /// </summary>
    public static string Json(string engine = "") => JsonSerializer.Serialize(new
    {
        version = Version,
        track = Track,
        build = Build,
        channel = Channel,
        buildName = BuildName,
        buildCode = BuildCode,
        preRelease = PreRelease,
        phaseNumber = PhaseNumber,
        phaseShipped = PhaseShipped,
        phaseName = PhaseName,
        phaseLabel = PhaseLabel,
        milestonesDone = MilestonesDone,
        milestones = Milestones,
        progress = Progress,
        builtOn = BuiltOn,
        engine,
        // Every build's name and code, so the page never re-derives the numbering.
        names = Enumerable.Range(1, 7).ToDictionary(p => p.ToString(), NameOf),
        codes = Enumerable.Range(1, 7).ToDictionary(p => p.ToString(), CodeOf),
    });
}
