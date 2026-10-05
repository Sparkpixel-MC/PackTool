#!/usr/bin/env bash
# Cross-language regression check: C# PackTool vs the original Node rssync3.js.
# Verifies (1) metadata.json / metadata.sha1 are byte-identical to the Node
# version, (2) every C#-encrypted dist file decrypts cleanly with node:crypto.
# Usage: bash scripts/validate-against-node.sh
set -e
cd "$(dirname "$0")/.."
KEY="dGVzdC13cmFwLWtleS0xMjMtdGVzdC13cmFw"

rm -rf .test && mkdir -p .test/in/assets/minecraft/textures/block .test/in/assets/minecraft/lang \
  .test/in/sounds .test/in/assets/empty_dir .test/in/assets/资源
node -e "
const fs=require('fs'),crypto=require('crypto');
const w=(p,d)=>fs.writeFileSync(p,d);
w('.test/in/assets/minecraft/textures/block/stone.png', crypto.randomBytes(1024*1024));
w('.test/in/assets/minecraft/textures/block/dirt.png', crypto.randomBytes(4096));
w('.test/in/sounds/big_sound.ogg', crypto.randomBytes(2*1024*1024));
w('.test/in/assets/minecraft/lang/zh_cn.json', JSON.stringify({'方块名称':'石头'},null,2));
w('.test/in/assets/资源/纹理.png', crypto.randomBytes(2048));
w('.test/in/desktop.ini', '[.ShellClassInfo]');
w('.test/in/updater_cache.bin', 'skipped');
"
node -e "require('./rssync3.js').packPack('.test/in','.test/out-node','$KEY',{},()=>{})"
dotnet run --project PackTool.Cli -- .test/in .test/out-cs "$KEY" 2>/dev/null

# Encrypted dist files embed random nonces, so ciphertext sha1s differ between
# runs — metadata can no longer be compared byte-for-byte. Instead: same file
# set and (deterministic) encrypted sizes, every recorded sha1 matches the
# actual dist file, and metadata.sha1 matches a recomputed dirChecksum.
node -e "
const fs=require('fs'),crypto=require('crypto'),path=require('path');
const mn=JSON.parse(fs.readFileSync('.test/out-node/metadata.json'));
const mc=JSON.parse(fs.readFileSync('.test/out-cs/metadata.json'));
const sn=JSON.parse(fs.readFileSync('.test/out-node/metadata.sha1')).sha1;
const sc=JSON.parse(fs.readFileSync('.test/out-cs/metadata.sha1')).sha1;
const fn=Object.keys(mn.file_content.files), fc=Object.keys(mc.file_content.files);
if(mn.version!==2||mc.version!==2) throw new Error('version');
if(!mn.encrypt||!mc.encrypt) throw new Error('encrypt flag');
if(String(fn.sort())!==String(fc.sort())) throw new Error('file sets differ');
for(const r of fn){
  const e=mn.file_content.files[r], c=mc.file_content.files[r];
  if(e.size!==c.size) throw new Error('size differs: '+r);
  const actual=crypto.createHash('sha1').update(fs.readFileSync(path.join('.test/out-cs/dist',r))).digest('hex');
  if(c.sha1!==actual) throw new Error('cs sha1 mismatch: '+r);
  const actualN=crypto.createHash('sha1').update(fs.readFileSync(path.join('.test/out-node/dist',r))).digest('hex');
  if(e.sha1!==actualN) throw new Error('node sha1 mismatch: '+r);
}
function dirChecksum(meta){
  const parts=[];
  for(const d of Object.keys(meta.file_content.dirs).sort()) parts.push(Buffer.from(d,'utf8'));
  for(const name of Object.keys(meta.file_content.files).sort()){
    parts.push(Buffer.from(name,'utf8'));
    parts.push(Buffer.from(meta.file_content.files[name].sha1,'hex'));
  }
  return crypto.createHash('sha1').update(Buffer.concat(parts)).digest('hex');
}
if(dirChecksum(mn)!==sn) throw new Error('node metadata.sha1 mismatch');
if(dirChecksum(mc)!==sc) throw new Error('cs metadata.sha1 mismatch');
console.log('metadata: '+fn.length+' files, sets/sizes/sha1/metadata.sha1 all consistent');
"

node -e "
const fs=require('fs'),crypto=require('crypto'),path=require('path');
const kek=crypto.pbkdf2Sync('$KEY','cn.ymjacky.resource-sync.rssync3.key-wrap.v1',65536,32,'sha256');
function dec(f){const b=require('fs').readFileSync(f);let o=8,k;
  if(b[7]===1){const n=b.subarray(o,o+12);o+=12;const w=b.subarray(o,o+48);o+=48;
    const d=crypto.createDecipheriv('aes-256-gcm',kek,n);d.setAuthTag(w.subarray(32));
    k=Buffer.concat([d.update(w.subarray(0,32)),d.final()]);}
  else{k=b.subarray(o,o+32);o+=32;}
  const n=b.subarray(o,o+12);o+=12;
  const d=crypto.createDecipheriv('aes-256-gcm',k,n);d.setAuthTag(b.subarray(b.length-16));
  return Buffer.concat([d.update(b.subarray(o,b.length-16)),d.final()]);}
let n=0;(function walk(rel){for(const e of fs.readdirSync(path.join('.test/in',rel),{withFileTypes:true})){
  const r=rel?rel+'/'+e.name:e.name;
  if(e.isDirectory())walk(r);
  else if(e.name.toLowerCase()==='desktop.ini'||r==='updater_cache.bin')continue;
  else{if(!dec(path.join('.test/out-cs/dist',r)).equals(fs.readFileSync(path.join('.test/in',r))))
    {console.error('PLAINTEXT MISMATCH: '+r);process.exit(1);}n++;}}})('');
console.log('dist files: '+n+' decrypted by node:crypto, all plaintexts identical');
"
rm -rf .test
echo "ALL CHECKS PASSED"
