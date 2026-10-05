// Headless test harness for the RSSYNC3 port — packs a directory exactly like
// the GUI does, so the output can be byte-compared against the Node version.
// Usage: dotnet run --project PackTool.Cli -- <inputDir> <outputDir> [wrapKey] [lock] [bind]
using PackTool;

// AES-256-GCM cross-validation vector (key/nonce/plaintext encrypted with
// node:crypto and verified byte-identical; also exercises cross-chunk partial
// blocks since the plaintext is fed in 37-byte chunks).
if (args.Length > 0 && args[0] == "selftest")
{
    var key = Convert.FromHexString("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308");
    var nonce = Convert.FromHexString("cafebabefacedbaddecaf888");
    var pt = Convert.FromHexString("d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b391bafd9");
    var expectedCt = "522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f66288801e";
    var expectedTag = "b13d809ecf5c14a77e54b658b6163d75";
    var ms = new MemoryStream();
    using (var g = new Rssync3.GcmEncryptor(key, nonce))
    {
        // feed in uneven chunks to exercise cross-chunk partial blocks
        for (int off = 0; off < pt.Length; off += 37)
        {
            int n = Math.Min(37, pt.Length - off);
            g.EncryptChunk(pt.AsSpan(off, n), ms);
        }
        var tag = g.ComputeTag();
        var ct = Convert.ToHexString(ms.ToArray()).ToLowerInvariant();
        var tagHex = Convert.ToHexString(tag).ToLowerInvariant();
        if (ct != expectedCt || tagHex != expectedTag)
        {
            Console.Error.WriteLine($"selftest FAIL\nct:  {ct}\ntag: {tagHex}");
            return 1;
        }
    }
    Console.WriteLine("gcm selftest OK");
    return 0;
}

var inputDir = args.Length > 0 ? args[0] : throw new ArgumentException("usage: <inputDir> <outputDir> [wrapKey] [lock] [bind] | selftest");
var outputDir = args.Length > 1 ? args[1] : throw new ArgumentException("missing <outputDir>");
var wrapKey = args.Length > 2 ? args[2] : "";
var lockVal = args.Length > 3 ? args[3] : "";
var bindVal = args.Length > 4 ? args[4] : "";

var result = PackService.Pack(inputDir, outputDir, wrapKey, lockVal, bindVal,
    text => Console.Error.WriteLine("[stage] " + text),
    (done, total, name) => Console.Error.WriteLine($"[progress] {done}/{total} {name}"));

if (!result.Ok)
{
    Console.Error.WriteLine("ERROR: " + result.Error);
    return 1;
}
Console.WriteLine(result.Summary!.Checksum);
return 0;
