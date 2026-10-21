using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>Limits the decompressed body, including chunked responses, under the caller's whole-request deadline.</summary>
internal static class BoundedDownload
{
    internal static async Task<byte[]> ReadAsync(HttpContent content, int limit, CancellationToken cancel)
    {
        if (limit <= 0) { throw new ArgumentOutOfRangeException(nameof(limit)); }
        if (content.Headers.ContentLength is long declared && declared > limit)
            throw new InvalidDataException("Response exceeds the size limit.");
        using Stream input = await content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using MemoryStream copy = new();
        byte[] buffer = new byte[(int)Math.Min(16_384L, (long)limit + 1)];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancel).ConfigureAwait(false)) != 0)
        {
            if (copy.Length + read > limit)
                throw new InvalidDataException("Response exceeds the size limit.");
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }
}
