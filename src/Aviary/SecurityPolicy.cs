using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>Small, testable boundaries shared by native handlers and list downloads.</summary>
internal static class SecurityPolicy
{
    public const int MaxFilterBytes = 40_000_000;
    public const int MaxWebMessageChars = 65_536;
    public const int MaxInternalMessageChars = 4 * 1024 * 1024;

    internal static JsonDocument? ParseMessage(string? text, int limit = MaxWebMessageChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length > limit) { return null; }
        try
        {
            JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String && type.GetString() is { Length: > 0 and <= 64 })
                return doc;
            doc.Dispose();
        }
        catch (JsonException) { /* An arbitrary website can post malformed messages. */ }
        return null;
    }

    public static bool IsSecurePermissionOrigin(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
        && uri.UserInfo.Length == 0
        && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback));

    public static bool IsInternalMessageSource(string source, string currentSource) =>
        string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase)
        && string.Equals(currentSource, "about:blank", StringComparison.OrdinalIgnoreCase);

    public static bool IsCurrentWebDocument(string source, string currentSource) =>
        Uri.TryCreate(source, UriKind.Absolute, out Uri? from)
        && Uri.TryCreate(currentSource, UriKind.Absolute, out Uri? current)
        && from.Scheme is "http" or "https" && from.UserInfo.Length == 0
        && Uri.Compare(from, current, UriComponents.HttpRequestUrl, UriFormat.UriEscaped,
            StringComparison.Ordinal) == 0;

    // Test overrides may use HTTP only on loopback, with an explicit scratch profile.
    public static bool IsAllowedFilterUri(Uri uri, bool test) =>
        uri.IsAbsoluteUri && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (test ? uri.IsLoopback && uri.Scheme is "http" or "https"
                 : uri.Scheme == "https" && uri.IsDefaultPort
                   && (uri.IdnHost.Equals("easylist.to", StringComparison.OrdinalIgnoreCase)
                       || uri.IdnHost.Equals("secure.fanboy.co.nz", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Bound the bytes while streaming, including a chunked or dishonest response.</summary>
    internal static async Task<string> ReadFilterTextAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxFilterBytes)
            throw new InvalidDataException("Filter response is too large.");
        string? media = response.Content.Headers.ContentType?.MediaType;
        if (media is not null && (media.Contains("html", StringComparison.OrdinalIgnoreCase)
            || media.Contains("json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Filter response is not plain list text.");

        using Stream input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using MemoryStream bytes = new();
        byte[] buffer = new byte[16_384];
        int read;
        while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + read > MaxFilterBytes)
                throw new InvalidDataException("Filter response exceeded the size limit.");
            bytes.Write(buffer, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }
}
