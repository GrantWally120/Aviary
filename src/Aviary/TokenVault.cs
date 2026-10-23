using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Aviary;

/// <summary>
/// Where an account's tokens live once accounts exist: one small file per secret, each sealed
/// with the Windows Data Protection API for the signed-in Windows user (so another Windows
/// account, or a copy of the file on another machine, cannot open it). Deliberately NOT part of
/// settings, history, diagnostics or anything the person exports or syncs: those are read,
/// logged and scrubbed on the assumption they contain no credentials, and the diagnostic
/// collector skips this folder.
///
/// Nothing here writes a secret to a log or an exception message, and every failure to read a
/// secret returns null (tampered, copied from another user, unreadable) rather than throwing.
/// A secret is bound to its name: a file renamed to another name will not open. Names are a
/// small closed alphabet so a name can never become a path.
///
/// Not wired to any screen yet: the account system does not exist. See docs/SECURITY.md.
/// </summary>
internal sealed class TokenVault
{
    public const int MaxSecretBytes = 16 * 1024;
    public const int MaxEntries = 64;
    private const string Extension = ".tok";
    private static readonly byte[] Magic = { (byte)'A', (byte)'V', (byte)'T', (byte)'1' };
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("Aviary token vault v1\0");

    private readonly string _folder;
    private readonly ISecretProtector _protector;
    private readonly object _gate = new();

    public TokenVault(string folder, ISecretProtector? protector = null)
    {
        _folder = Path.GetFullPath(folder);
        _protector = protector ?? new DpapiProtector();
    }

    public string Folder => _folder;

    /// <summary>Lowercase letters, digits, dot, dash, underscore; 1 to 64 characters; no leading dot.</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name[0] == '.') { return false; }
        foreach (char c in name)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_')) { return false; }
        }

        return true;
    }

    public bool Set(string name, string secret)
    {
        if (!IsValidName(name) || string.IsNullOrEmpty(secret)) { return false; }
        byte[] plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            if (plain.Length > MaxSecretBytes) { return false; }
            lock (_gate)
            {
                Directory.CreateDirectory(_folder);
                if (!File.Exists(PathOf(name)) && Count() >= MaxEntries) { return false; }
                byte[] sealedBytes = _protector.Protect(plain, EntropyFor(name));
                byte[] file = new byte[Magic.Length + sealedBytes.Length];
                Buffer.BlockCopy(Magic, 0, file, 0, Magic.Length);
                Buffer.BlockCopy(sealedBytes, 0, file, Magic.Length, sealedBytes.Length);

                // Write beside, then move over: a crash leaves the old secret or the new, never half of one.
                string target = PathOf(name), temp = target + "." + Guid.NewGuid().ToString("N") + ".new";
                File.WriteAllBytes(temp, file);
                File.Move(temp, target, overwrite: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public string? Get(string name)
    {
        if (!IsValidName(name)) { return null; }
        try
        {
            byte[] file;
            lock (_gate)
            {
                string path = PathOf(name);
                if (!File.Exists(path)) { return null; }
                FileInfo info = new(path);
                if (info.Length > MaxSecretBytes + 4096) { return null; }
                file = File.ReadAllBytes(path);
            }

            if (file.Length <= Magic.Length || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic)) { return null; }
            byte[] plain = _protector.Unprotect(file.AsSpan(Magic.Length).ToArray(), EntropyFor(name));
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    public bool Delete(string name)
    {
        if (!IsValidName(name)) { return false; }
        try
        {
            lock (_gate)
            {
                string path = PathOf(name);
                if (!File.Exists(path)) { return false; }
                // Overwrite before unlinking: the sealed bytes are not worth recovering, but cost nothing to lose.
                long length = new FileInfo(path).Length;
                using (FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    stream.Write(RandomNumberGenerator.GetBytes((int)Math.Min(length, MaxSecretBytes + 4096)));
                    stream.Flush(true);
                }

                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> Names()
    {
        List<string> names = new();
        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(_folder)) { return names; }
                foreach (string file in Directory.EnumerateFiles(_folder, "*" + Extension))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (IsValidName(name)) { names.Add(name); }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>Signing out everywhere, or "Forget everything": every secret and the folder go.</summary>
    public int Wipe()
    {
        int removed = 0;
        foreach (string name in Names()) { if (Delete(name)) { removed++; } }
        try { if (Directory.Exists(_folder) && Directory.GetFileSystemEntries(_folder).Length == 0) { Directory.Delete(_folder); } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    private int Count() => Names().Count;

    private string PathOf(string name) => Path.Combine(_folder, name + Extension);

    private static byte[] EntropyFor(string name)
    {
        byte[] label = Encoding.UTF8.GetBytes(name);
        byte[] entropy = new byte[Purpose.Length + label.Length];
        Buffer.BlockCopy(Purpose, 0, entropy, 0, Purpose.Length);
        Buffer.BlockCopy(label, 0, entropy, Purpose.Length, label.Length);
        return entropy;
    }
}

/// <summary>Seals and opens a secret. DPAPI in the app; replaceable in tests.</summary>
internal interface ISecretProtector
{
    byte[] Protect(byte[] plain, byte[] entropy);

    /// <exception cref="CryptographicException">The data was altered, or belongs to another user or machine.</exception>
    byte[] Unprotect(byte[] sealedBytes, byte[] entropy);
}

/// <summary>CryptProtectData / CryptUnprotectData for the current Windows user, with no UI and no prompt.</summary>
internal sealed class DpapiProtector : ISecretProtector
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public byte[] Protect(byte[] plain, byte[] entropy) => Run(plain, entropy, protect: true);

    public byte[] Unprotect(byte[] sealedBytes, byte[] entropy) => Run(sealedBytes, entropy, protect: false);

    private static byte[] Run(byte[] input, byte[] entropy, bool protect)
    {
        GCHandle inHandle = GCHandle.Alloc(input, GCHandleType.Pinned), entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        DataBlob output = default;
        try
        {
            DataBlob blobIn = new() { cbData = input.Length, pbData = inHandle.AddrOfPinnedObject() };
            DataBlob blobEntropy = new() { cbData = entropy.Length, pbData = entropyHandle.AddrOfPinnedObject() };
            bool ok = protect
                ? CryptProtectData(ref blobIn, null, ref blobEntropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output)
                : CryptUnprotectData(ref blobIn, IntPtr.Zero, ref blobEntropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!ok)
            {
                // The Win32 error number only: never any part of the input.
                throw new CryptographicException("Data protection failed (" + Marshal.GetLastWin32Error() + ").");
            }

            byte[] result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (output.pbData != IntPtr.Zero)
            {
                // Cleared before it is freed: for Unprotect this block held the plaintext.
                byte[] zeros = new byte[output.cbData];
                Marshal.Copy(zeros, 0, output.pbData, output.cbData);
                LocalFree(output.pbData);
            }

            inHandle.Free();
            entropyHandle.Free();
        }
    }
}
