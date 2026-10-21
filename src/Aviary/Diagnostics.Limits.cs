namespace Aviary;

internal static partial class Diagnostics
{
    // Grant's existing local recording limits. Shared with the policy and test harness;
    // diagnostics currently uses a binary byte cap despite its historical MB name.
    public const int CapMB = 700;
    public const long CapBytes = CapMB * 1024L * 1024;
    public const int RetentionDays = 30;
}
