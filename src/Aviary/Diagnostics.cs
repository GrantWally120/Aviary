using System;

namespace Aviary;

/// <summary>
/// Local diagnostics — the "Allow data collection for improvements" feature.
///
/// STUB. Phase 5 milestone M1 replaces this file with the real collector. It exists now so
/// that code written before M1 can already route its failures here, and so that turning
/// the 71 silent <c>catch (Exception) {}</c> blocks into recorded ones is a mechanical
/// change rather than a second pass.
/// </summary>
internal static class Diagnostics
{
    /// <summary>A failure that was caught and survived. A no-op until M1.</summary>
    public static void Swallowed(Exception ex, string where)
    {
    }
}
