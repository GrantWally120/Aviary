// Every method in a managed dll: its full name, where its body sits, and a hash of the body (M5, 25 Sep).
// Two builds with the same methods in a different ORDER give the same (name, hash) set.
//
//   dotnet run --project tools/test/ildump -- <a.dll> > a.txt     (sorted; diff two of these)
//   dotnet run --project tools/test/ildump -- <a.dll> x           (adds each method's token and RVA)
//
// Why it exists: a byte compare of the tested and published Aviary.dll cannot tell "Aviary's code
// differs" from "CsWinRT's source generator emitted its WinRT vtable classes in another order" - it
// does, between builds of the SAME source - and the second is what M5's publish check found.
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

using var fs = File.OpenRead(args[0]);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var lines = new List<string>();
foreach (var h in md.MethodDefinitions)
{
    var m = md.GetMethodDefinition(h);
    var t = md.GetTypeDefinition(m.GetDeclaringType());
    var name = md.GetString(t.Namespace) + "." + md.GetString(t.Name) + "::" + md.GetString(m.Name)
             + " sig=" + Convert.ToHexString(md.GetBlobBytes(m.Signature));
    string body = "-";
    if (m.RelativeVirtualAddress != 0)
    {
        var b = pe.GetMethodBody(m.RelativeVirtualAddress);
        body = Convert.ToHexString(SHA256.HashData(b.GetILBytes())).Substring(0, 16) + " max" + b.MaxStack;
    }
    lines.Add($"{name} {body}" + (args.Length > 1 ? $" tok={MetadataTokens.GetToken(h):X8} @{m.RelativeVirtualAddress:X}" : ""));
}
lines.Sort(StringComparer.Ordinal);
foreach (var l in lines) Console.WriteLine(l);
