using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PackTool;

public sealed record FileEntry(string Rel, string Sha1, long Size);

public sealed record PackSummary(int FileCount, int DirCount, string Checksum);

public delegate void PackProgressCallback(int done, int total, string name);

/// <summary>
/// RSSYNC3 format + metadata generation — a byte-exact port of the Java reference
/// implementation, cross-validated against both the Java PackTool output and the
/// original Node port (rssync3.js): metadata.json / metadata.sha1 MUST be
/// byte-identical for the same input.
///
/// File layout:
///   "RSSYNC3" (7B) | flags (1B, bit0 = key wrapped)
///   wrapped:    12B wrap-nonce + 48B (32B key || 16B GCM tag)
///   unwrapped:  32B raw key
///   12B content-nonce | AES-256-GCM ciphertext stream | 16B tag (at EOF)
/// KEK = PBKDF2-HMAC-SHA256(passphrase, salt="cn.ymjacky.resource-sync.rssync3.key-wrap.v1",
///                          65536 iterations, 32B)
/// </summary>
public static class Rssync3
{
    private static readonly byte[] Magic = "RSSYNC3"u8.ToArray();
    private static readonly byte[] Pbkdf2Salt = "cn.ymjacky.resource-sync.rssync3.key-wrap.v1"u8.ToArray();
    private const int Pbkdf2Iterations = 65536;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static byte[] DeriveKek(string passphrase)
    {
        using var dbk = new Rfc2898DeriveBytes(passphrase, Pbkdf2Salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        return dbk.GetBytes(32);
    }

    public static string GenerateWrapKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    // ---- streaming AES-256-GCM --------------------------------------------

    /// <summary>
    /// Encrypts one file in RSSYNC3 format (streaming, constant memory for
    /// content; keys and nonces are random per call, as in the Node version).
    /// </summary>
    public static void EncryptFile(string srcPath, string targetPath, string? wrapSecret)
    {
        var fileKey = RandomNumberGenerator.GetBytes(32);
        var contentNonce = RandomNumberGenerator.GetBytes(12);
        bool wrap = !string.IsNullOrEmpty(wrapSecret);

        byte[] header;
        if (wrap)
        {
            var wrapNonce = RandomNumberGenerator.GetBytes(12);
            var kek = DeriveKek(wrapSecret!);
            var ct = new byte[32];
            var tag = new byte[16];
            using (var g = new AesGcm(kek, 16))
            {
                g.Encrypt(wrapNonce, fileKey, ct, tag);
            }
            header = Concat(Magic, [(byte)1], wrapNonce, ct, tag, contentNonce);
        }
        else
        {
            header = Concat(Magic, [(byte)0], fileKey, contentNonce);
        }

        using var outFs = File.Create(targetPath);
        outFs.Write(header, 0, header.Length);

        using var gcm = new GcmEncryptor(fileKey, contentNonce);
        using var inFs = File.OpenRead(srcPath);
        var buffer = new byte[512 * 1024];
        int read;
        while ((read = inFs.Read(buffer, 0, buffer.Length)) > 0)
        {
            gcm.EncryptChunk(buffer.AsSpan(0, read), outFs);
        }
        outFs.Write(gcm.ComputeTag(), 0, 16);
        outFs.Flush();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len += p.Length;
        var buf = new byte[len];
        int off = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, buf, off, p.Length);
            off += p.Length;
        }
        return buf;
    }

    /// <summary>AES-256-GCM encryption over an arbitrary-length stream:
    /// manual CTR keystream (counter starts after J0) + GHASH over the
    /// ciphertext only (no AAD), tag = E_K(Y_final XOR J0). Output is
    /// identical to node:crypto / AesGcm for the same key and nonce.</summary>
    internal sealed class GcmEncryptor : IDisposable
    {
        private const int KsBlocks = 8192; // 128KB keystream per refill
        private readonly byte[] _key;
        private readonly byte[] _h = new byte[16];
        private readonly byte[] _j0 = new byte[16];
        private readonly byte[] _icb = new byte[16];
        private readonly byte[] _y = new byte[16];
        private readonly byte[] _partial = new byte[16]; // pending ciphertext for GHASH
        private int _partialLen;
        private ulong _cipherBits;
        private readonly byte[] _ksRaw = new byte[16 * KsBlocks];
        private readonly byte[] _ks = new byte[16 * KsBlocks];
        private int _ksPos = int.MaxValue;
        private readonly ICryptoTransform _ecb;
        private byte[][] _powH = null!; // powH[i] = H · x^i in the GCM field

        public GcmEncryptor(byte[] key, byte[] nonce)
        {
            _key = key;
            nonce.CopyTo(_j0, 0);
            _j0[12] = 0; _j0[13] = 0; _j0[14] = 0; _j0[15] = 1;
            Buffer.BlockCopy(_j0, 0, _icb, 0, 16);
            IncrementCounter(_icb);

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            _ecb = aes.CreateEncryptor();

            _ecb.TransformBlock(_h, 0, 16, _h, 0); // H = E_K(0^128)
            BuildPowTable();
        }

        public void EncryptChunk(ReadOnlySpan<byte> src, Stream output)
        {
            var ct = new byte[src.Length];
            int offset = 0;
            while (offset < src.Length)
            {
                if (_ksPos >= _ks.Length) RefillKeystream();
                int n = Math.Min(src.Length - offset, _ks.Length - _ksPos);
                for (int i = 0; i < n; i++)
                    ct[offset + i] = (byte)(src[offset + i] ^ _ks[_ksPos + i]);
                _ksPos += n;
                offset += n;
            }
            output.Write(ct, 0, ct.Length);
            GhashUpdate(ct);
            _cipherBits += (ulong)ct.Length * 8;
        }

        public byte[] ComputeTag()
        {
            if (_partialLen > 0)
            {
                GhashBlock(_partial); // zero-padded
                _partialLen = 0;
            }
            var lenBlock = new byte[16];
            BinaryPrimitives.WriteUInt64BigEndian(lenBlock.AsSpan(8), _cipherBits);
            GhashBlock(lenBlock);

            // tag = GHASH output XOR E_K(J0)  (GCTR of the single-block S)
            var eJ0 = new byte[16];
            _ecb.TransformBlock(_j0, 0, 16, eJ0, 0);
            var tag = new byte[16];
            for (int i = 0; i < 16; i++) tag[i] = (byte)(_y[i] ^ eJ0[i]);
            return tag;
        }

        private void RefillKeystream()
        {
            for (int i = 0; i < KsBlocks; i++)
            {
                Buffer.BlockCopy(_icb, 0, _ksRaw, i * 16, 16);
                IncrementCounter(_icb);
            }
            _ecb.TransformBlock(_ksRaw, 0, _ksRaw.Length, _ks, 0);
            _ksPos = 0;
        }

        private static void IncrementCounter(byte[] block)
        {
            for (int i = 15; i >= 12; i--)
            {
                if (++block[i] != 0) break;
            }
        }

        private void BuildPowTable()
        {
            _powH = new byte[128][];
            _powH[0] = (byte[])_h.Clone();
            for (int i = 1; i < 128; i++)
            {
                var v = (byte[])_powH[i - 1].Clone();
                bool lsb = (v[15] & 1) != 0;
                ShiftRight(v);
                if (lsb) v[0] ^= 0xE1;
                _powH[i] = v;
            }
        }

        private void GhashUpdate(ReadOnlySpan<byte> data)
        {
            int pos = 0;
            if (_partialLen > 0)
            {
                int take = Math.Min(16 - _partialLen, data.Length);
                data.Slice(0, take).CopyTo(_partial.AsSpan(_partialLen));
                _partialLen += take;
                pos += take;
                if (_partialLen == 16)
                {
                    GhashBlock(_partial);
                    _partialLen = 0;
                }
            }
            while (pos + 16 <= data.Length)
            {
                GhashBlock(data.Slice(pos, 16));
                pos += 16;
            }
            if (pos < data.Length)
            {
                _partialLen = data.Length - pos;
                data.Slice(pos).CopyTo(_partial);
                Array.Clear(_partial, _partialLen, 16 - _partialLen);
            }
        }

        private void GhashBlock(ReadOnlySpan<byte> block)
        {
            // Y = (Y XOR X) · H — the accumulated state is multiplied by H each step
            for (int i = 0; i < 16; i++) _y[i] ^= block[i];
            var z = new byte[16];
            for (int i = 0; i < 128; i++)
            {
                if ((_y[i >> 3] & (0x80 >> (i & 7))) != 0)
                {
                    var p = _powH[i];
                    for (int j = 0; j < 16; j++) z[j] ^= p[j];
                }
            }
            z.AsSpan().CopyTo(_y);
        }

        private static void ShiftRight(byte[] v)
        {
            for (int j = 15; j > 0; j--)
                v[j] = (byte)((v[j] >> 1) | ((v[j - 1] & 1) << 7));
            v[0] >>= 1;
        }

        public void Dispose() => _ecb.Dispose();
    }

    // ---- scanning + checksum ----------------------------------------------

    /// <summary>Scans inputDir exactly like Java LocalMetadata.scanDir (skips
    /// updater_cache.bin and desktop.ini case-insensitively; symlink/reparse
    /// entries are ignored like node's readdir entry types). Relative paths
    /// use '/' as separator, order matches node's fs.readdirSync walk.</summary>
    public static (List<string> Dirs, List<FileEntry> Files) ScanPack(string inputDir)
    {
        var dirs = new List<string>();
        var files = new List<FileEntry>();
        Walk(inputDir, "");
        return (dirs, files);

        void Walk(string abs, string rel)
        {
            dirs.Add(rel);
            foreach (var childAbs in Directory.EnumerateFileSystemEntries(abs))
            {
                var name = Path.GetFileName(childAbs);
                var childRel = rel.Length > 0 ? rel + "/" + name : name;
                var attrs = File.GetAttributes(childAbs);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                if ((attrs & FileAttributes.Directory) != 0)
                {
                    Walk(childAbs, childRel);
                }
                else
                {
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (childRel == "updater_cache.bin") continue;
                    files.Add(new FileEntry(childRel, Sha1HexFile(childAbs), new FileInfo(childAbs).Length));
                }
            }
        }
    }

    private static string Sha1HexFile(string path)
    {
        using var sha = SHA1.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Rebuilds Java LocalMetadata.getDirChecksum(): sha1 over sorted
    /// dirs (utf8) concatenated, then sorted file names (utf8) each followed
    /// by its 20B sha1. Sorting is by UTF-16 code units (JS default sort).</summary>
    public static string DirChecksum(List<string> dirs, Dictionary<string, string> fileHashes)
    {
        using var sha = SHA1.Create();
        foreach (var d in dirs.OrderBy(x => x, StringComparer.Ordinal))
        {
            var b = Encoding.UTF8.GetBytes(d);
            sha.TransformBlock(b, 0, b.Length, null, 0);
        }
        foreach (var name in fileHashes.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var nb = Encoding.UTF8.GetBytes(name);
            sha.TransformBlock(nb, 0, nb.Length, null, 0);
            var hb = Convert.FromHexString(fileHashes[name]);
            sha.TransformBlock(hb, 0, hb.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    // ---- full pipeline ------------------------------------------------------

    public static PackSummary PackPack(string inputDir, string outDir, string wrapKey,
                                       PackProgressCallback? onProgress)
    {
        var (dirs, files) = ScanPack(inputDir);
        var distDir = Path.Combine(outDir, "dist");
        Directory.CreateDirectory(distDir);

        int done = 0;
        foreach (var f in files)
        {
            var relLocal = f.Rel.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(distDir, relLocal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            EncryptFile(Path.Combine(inputDir, relLocal), target, wrapKey);
            done++;
            onProgress?.Invoke(done, files.Count, f.Rel);
        }

        var checksum = DirChecksum(dirs, files.ToDictionary(f => f.Rel, f => f.Sha1));
        bool encrypt = wrapKey.Length > 0 || files.Count > 0; // files are always encrypted
        File.WriteAllText(Path.Combine(outDir, "metadata.json"),
            BuildMetadataJson(dirs, files, encrypt), Utf8NoBom);
        File.WriteAllText(Path.Combine(outDir, "metadata.sha1"),
            "{\"sha1\":\"" + checksum + "\"}", Utf8NoBom);

        return new PackSummary(files.Count, dirs.Count, checksum);
    }

    /// <summary>Byte-exact JSON.stringify equivalent of:
    /// {version:2, encrypt:bool, file_content:{dirs:{d:{}}, files:{rel:{sha1,size,mtime}}}}
    /// including JS object key ordering (integer-like keys ascending first,
    /// then insertion order) and escaping.</summary>
    private static string BuildMetadataJson(List<string> dirs, List<FileEntry> files, bool encrypt)
    {
        var sb = new StringBuilder();
        sb.Append("{\"version\":2,\"encrypt\":").Append(encrypt ? "true" : "false");
        sb.Append(",\"file_content\":{\"dirs\":{");
        bool first = true;
        foreach (var d in JsOrderedKeys(dirs))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsString(d)).Append(":{}");
        }
        sb.Append("},\"files\":{");
        first = true;
        foreach (var f in JsOrderedKeys(files.Select(f => f.Rel).ToList()))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsString(f)).Append(":{\"sha1\":\"").Append(files.First(x => x.Rel == f).Sha1);
            sb.Append("\",\"size\":").Append(files.First(x => x.Rel == f).Size.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"mtime\":0}");
        }
        sb.Append("}}}");
        return sb.ToString();
    }

    /// <summary>JS object key order: integer-like keys (0, 1, 2 …) first in
    /// ascending numeric order, then the remaining keys in insertion order.</summary>
    private static List<string> JsOrderedKeys(List<string> insertion)
    {
        var ints = new List<uint>();
        var rest = new List<string>();
        foreach (var k in insertion)
        {
            if (k.Length > 0 && k.Length <= 10 && uint.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
                ints.Add(v);
            else
                rest.Add(k);
        }
        ints.Sort();
        var result = new List<string>(insertion.Count);
        foreach (var v in ints) result.Add(v.ToString(CultureInfo.InvariantCulture));
        result.AddRange(rest);
        return result;
    }

    /// <summary>JSON.stringify string escaping: ", \, \b \f \n \r \t and
    /// \uXXXX for other control chars; non-ASCII stays raw (file is UTF-8).</summary>
    public static string JsString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
