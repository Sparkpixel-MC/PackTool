'use strict';
const $ = (id) => document.getElementById(id);

function wirePicker(dropId, inputId) {
  const zone = $(dropId), input = $(inputId);
  zone.addEventListener('click', async () => {
    const dir = await window.packtool.pickDirectory();
    if (dir) input.value = dir;
  });
  ['dragover', 'dragenter'].forEach(ev =>
    zone.addEventListener(ev, (e) => { e.preventDefault(); zone.classList.add('hot'); }));
  ['dragleave', 'drop'].forEach(ev =>
    zone.addEventListener(ev, (e) => { e.preventDefault(); zone.classList.remove('hot'); }));
  zone.addEventListener('drop', (e) => {
    const item = [...e.dataTransfer.items].find(i => i.kind === 'file');
    if (item) {
      const file = item.getAsFile();
      if (file && file.path) input.value = file.path;
    }
  });
}
wirePicker('dropInput', 'inputDir');
wirePicker('dropOutput', 'outputDir');

$('genKey').addEventListener('click', async () => {
  const key = await window.packtool.generateWrapKey();
  $('wrapKey').value = key;
  const hint = $('keyHint');
  hint.textContent = '已生成并填入，请同时保存到密码管理器：' + key;
  hint.classList.remove('hidden');
});

document.querySelectorAll('.copy').forEach(btn =>
  btn.addEventListener('click', () => navigator.clipboard.writeText($(btn.dataset.copy).textContent)));

$('packBtn').addEventListener('click', async () => {
  const inputDir = $('inputDir').value.trim();
  const outputDir = $('outputDir').value.trim();
  const wrapKey = $('wrapKey').value.trim();
  if (!inputDir || !outputDir) { $('stage').textContent = '请先选择输入和输出目录'; return; }
  $('packBtn').disabled = true;
  $('result').innerHTML = '';
  $('fill').style.width = '0%';
  $('stage').textContent = '启动打包…';
  await window.packtool.pack({
    inputDir, outputDir, wrapKey,
    mcmetaLock: $('mcmetaLock').value.trim(),
    mcmetaBind: $('mcmetaBind').value.trim(),
  });
  $('packBtn').disabled = false;
});

window.packtool.onProgress((payload) => {
  if (payload.type === 'stage') {
    $('stage').textContent = payload.text;
  } else if (payload.type === 'progress') {
    const pct = Math.round(payload.done * 100 / Math.max(payload.total, 1));
    $('fill').style.width = pct + '%';
    $('stage').textContent = `加密中 ${payload.done} / ${payload.total} — ${payload.name}`;
  } else if (payload.type === 'done') {
    $('fill').style.width = '100%';
    const s = payload.summary;
    $('stage').innerHTML = `<span class="ok-msg">完成！${s.fileCount} 个文件、${s.dirCount} 个目录，耗时 ${s.seconds}s</span>`;
    $('serverSnippet').textContent = payload.snippets.server;
    $('clientSnippet').textContent = payload.snippets.client;
    $('snippetNote').textContent = payload.snippets.note;
    $('snippetCard').classList.remove('hidden');
  } else if (payload.type === 'error') {
    $('stage').innerHTML = `<span class="err-msg">失败：${payload.message}</span>`;
  }
});
