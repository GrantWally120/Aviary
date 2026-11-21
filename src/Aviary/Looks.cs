using System;

namespace Aviary;

/// <summary>
/// The names of Aviary's two looks, in one place.
///
/// <para><b>Beta Prep 2 (Grant, 21 Nov 2026): "replace the liquid glass name with Prism 2".</b>
/// Prism 2 is Wally Design's own glass look, and since 23 Oct 2026 the floating translucent look
/// has been drawn from its tokens (design/prism2.tokens.json) — it only still carried the name it
/// was built under. What people see, and what settings.json holds, is "Prism 2" from here on.</para>
///
/// <para>A saved "Liquid Glass" is migrated when settings load (AppSettings.Load) and is still
/// understood here, so an old profile, an old test seed and a hand-edited file all open in the
/// right look. The CODE keeps its old words where they are only names for parts —
/// Themes/LiquidGlass.xaml, GlassCapsule, "glass" — because renaming a resource dictionary buys
/// nothing a person can see.</para>
/// </summary>
internal static class Looks
{
    public const string SettingKey = "look";
    public const string Classic = "Classic";
    public const string Prism2 = "Prism 2";

    /// <summary>What Prism 2 was called until Beta Prep 2. Read, never written.</summary>
    public const string FormerGlassName = "Liquid Glass";

    /// <summary>True for the floating translucent look, by either of the names it has had.</summary>
    public static bool IsGlass(string? look) =>
        look is not null
        && (look.Equals(Prism2, StringComparison.OrdinalIgnoreCase)
            || look.Equals(FormerGlassName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The look chosen in Settings is Prism 2.</summary>
    public static bool GlassChosen => IsGlass(AppSettings.Get(SettingKey));
}
