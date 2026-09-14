namespace Aviary;

/// <summary>SearchEngines.Current reads the chosen engine from the settings store; here there is none, so it is the default.</summary>
internal static class AppSettings
{
    public static string Get(string key) => string.Empty;
}
