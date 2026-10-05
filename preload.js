'use strict';
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('packtool', {
  pickDirectory: () => ipcRenderer.invoke('pick-directory'),
  generateWrapKey: () => ipcRenderer.invoke('generate-wrap-key'),
  pack: (opts) => ipcRenderer.invoke('pack', opts),
  onProgress: (callback) => ipcRenderer.on('pack-progress', (_event, payload) => callback(payload)),
});
