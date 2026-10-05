'use strict';
/**
 * RSSYNC3 format + metadata generation — a byte-exact port of the Java
 * reference implementation in common/ (cn.ymjacky.drm.AssetEncryption,
 * cn.ymjacky.io.LocalMetadata). Cross-validated against Java PackTool output:
 * metadata.sha1 MUST be byte-identical to the Java tool's for the same input.
 *
 * File layout:
 *   "RSSYNC3" (7B) | flags (1B, bit0 = key wrapped)
 *   wrapped:    12B wrap-nonce + 48B (32B key || 16B GCM tag)
 *   unwrapped:  32B raw key
 *   12B content-nonce | AES-256-GCM ciphertext stream | 16B tag (at EOF)
 * KEK = PBKDF2-HMAC-SHA256(passphrase, salt="cn.ymjacky.resource-sync.rssync3.key-wrap.v1",
 *                          65536 iterations, 32B)
 */
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const MAGIC = Buffer.from('RSSYNC3', 'ascii');
const PBKDF2_SALT = Buffer.from('cn.ymjacky.resource-sync.rssync3.key-wrap.v1', 'utf8');
const PBKDF2_ITERATIONS = 65536;

function deriveKek(passphrase) {
  return crypto.pbkdf2Sync(passphrase, PBKDF2_SALT, PBKDF2_ITERATIONS, 32, 'sha256');
}

/** Encrypts one file in RSSYNC3 format. Returns a Promise resolved when written. */
function encryptFile(srcPath, targetPath, wrapSecret) {
  return new Promise((resolve, reject) => {
    const fileKey = crypto.randomBytes(32);
    const contentNonce = crypto.randomBytes(12);
    const wrap = wrapSecret && wrapSecret.length > 0;

    const headerParts = [MAGIC, Buffer.from([wrap ? 1 : 0])];
    if (wrap) {
      const wrapNonce = crypto.randomBytes(12);
      const kek = deriveKek(wrapSecret);
      const c = crypto.createCipheriv('aes-256-gcm', kek, wrapNonce);
      const wrapped = Buffer.concat([c.update(fileKey), c.final(), c.getAuthTag()]); // 32 + 16
      headerParts.push(wrapNonce, wrapped);
    } else {
      headerParts.push(fileKey);
    }
    headerParts.push(contentNonce);

    const cipher = crypto.createCipheriv('aes-256-gcm', fileKey, contentNonce);
    const out = fs.createWriteStream(targetPath);
    out.on('error', reject);
    const source = fs.createReadStream(srcPath);
    source.on('error', reject);
    source.on('data', (chunk) => out.write(cipher.update(chunk)));
    source.on('end', () => {
      try {
        out.write(cipher.final());
        out.write(cipher.getAuthTag()); // 16B tag at EOF
        out.end(() => resolve());
      } catch (e) { reject(e); }
    });
    // header first
    out.write(Buffer.concat(headerParts));
  });
}

/** Rebuilds the Java LocalMetadata.getDirChecksum(): sha1 over sorted dirs (utf8)
 *  concatenated, then sorted file names (utf8) each followed by its 20B sha1. */
function dirChecksum(dirs, fileHashes) {
  const parts = [];
  for (const d of [...dirs].sort()) parts.push(Buffer.from(d, 'utf8'));
  for (const name of Object.keys(fileHashes).sort()) {
    parts.push(Buffer.from(name, 'utf8'));
    parts.push(Buffer.from(fileHashes[name], 'hex'));
  }
  return crypto.createHash('sha1').update(Buffer.concat(parts)).digest('hex');
}

/** Scans inputDir exactly like LocalMetadata.scanDir (skips updater_cache.bin,
 *  desktop.ini case-insensitive). Returns {dirs:[], files:{rel:{sha1,size,mtime}}} */
function scanPack(inputDir) {
  const dirs = [];
  const files = {};
  const walk = (abs, rel) => {
    dirs.push(rel);
    for (const entry of fs.readdirSync(abs, { withFileTypes: true })) {
      const childAbs = path.join(abs, entry.name);
      const childRel = rel ? rel + '/' + entry.name : entry.name;
      if (entry.isDirectory()) {
        walk(childAbs, childRel);
      } else if (entry.isFile()) {
        const lower = entry.name.toLowerCase();
        if (lower === 'desktop.ini' || childRel === 'updater_cache.bin') continue;
        const buf = fs.readFileSync(childAbs);
        files[childRel] = {
          sha1: crypto.createHash('sha1').update(buf).digest('hex'),
          size: buf.length,
          mtime: 0,
        };
      }
    }
  };
  walk(inputDir, '');
  return { dirs, files };
}

/** Full pipeline: scan → encrypt to outDir/dist → metadata.json + metadata.sha1.
 *  onProgress(done, total, name). Returns summary object. */
async function packPack(inputDir, outDir, wrapKey, opts, onProgress) {
  const { dirs, files } = scanPack(inputDir);
  const names = Object.keys(files);
  const distDir = path.join(outDir, 'dist');
  fs.mkdirSync(distDir, { recursive: true });

  let done = 0;
  for (const rel of names) {
    const target = path.join(distDir, rel);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    await encryptFile(path.join(inputDir, rel), target, wrapKey);
    // metadata describes the dist artifact the client downloads (encrypted
    // file), not the plaintext source — sha1/size are taken after encryption.
    const enc = fs.readFileSync(target);
    files[rel] = {
      sha1: crypto.createHash('sha1').update(enc).digest('hex'),
      size: enc.length,
      mtime: 0,
    };
    done++;
    if (onProgress) onProgress(done, names.length, rel);
  }

  const checksum = dirChecksum(dirs, Object.fromEntries(names.map(n => [n, files[n].sha1])));
  const metadata = {
    version: 2,
    encrypt: wrapKey.length > 0 || Object.keys(files).length > 0, // files are always encrypted
    file_content: { dirs: Object.fromEntries(dirs.map(d => [d, {}])), files },
  };
  fs.writeFileSync(path.join(outDir, 'metadata.json'), JSON.stringify(metadata), 'utf8');
  const sha1Body = (opts && opts.rawSha1)
    ? checksum + '\n'
    : JSON.stringify({ sha1: checksum });
  fs.writeFileSync(path.join(outDir, 'metadata.sha1'), sha1Body, 'utf8');

  return { fileCount: names.length, dirCount: dirs.length, checksum };
}

function generateWrapKey() {
  return crypto.randomBytes(32).toString('base64');
}

module.exports = { encryptFile, dirChecksum, scanPack, packPack, generateWrapKey, deriveKek };
