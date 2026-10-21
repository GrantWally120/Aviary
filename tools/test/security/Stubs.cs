// Native stores are replaced with isolated memory stores. Security logic and downloads
// compile directly from shipping sources; no normal profile is read or written.
namespace Aviary;
internal static partial class Diagnostics
{
    public static TimeSpan Uptime => TimeSpan.FromMinutes(1);
    public static void Shields(string name, object? data) { }
    public static void Perf(string name, object? data) { }
    public static void Anomaly(string name, string text, object? data) { }
    public static void Swallowed(Exception ex, string where) { }
}
internal static class Library { public const string Host = "collection.aviary", LegacyHost = "menagerie.aviary"; }
internal static class BuildInfo { public const string Version = "0.5.0"; }
internal static class AppPaths
{
    public static string Root = Path.Combine(Path.GetTempPath(), "aviary-security-" + Guid.NewGuid().ToString("N"));
    public static string Of(string name) => Path.Combine(Root, name);
}
internal static class AppSettings
{
    static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
    public static string Get(string key) => Values.GetValueOrDefault(key, "");
    public static bool GetBool(string key) => Get(key) == "true";
    public static void Set(string key, string value) => Values[key] = value;
}
internal static class SiteSettings
{
    static readonly Dictionary<string, Dictionary<string, string>> Values = new();
    public static int Writes;
    public static string Get(string site, string key) => Values.TryGetValue(Shields.SiteOf(site), out var row) ? row.GetValueOrDefault(key, "") : "";
    public static bool GetBool(string site, string key) => Get(site, key) == "true";
    public static void Set(string site, string key, string value)
    {
        site = Shields.SiteOf(site);
        if (!Values.ContainsKey(site)) Values[site] = new();
        Values[site][key] = value; Writes++;
    }
    public static void Remove(string site, string key)
    {
        if (Values.TryGetValue(Shields.SiteOf(site), out var row)) row.Remove(key);
        Writes++;
    }
    public static IReadOnlyList<string> SitesWith(string key, string value = "true") =>
        Values.Where(p => p.Value.GetValueOrDefault(key) == value).Select(p => p.Key).ToList();
}
