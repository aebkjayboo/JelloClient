const { ipcRenderer } = require('electron')

let SERVER_PORT = null

async function getPort() {
  if (SERVER_PORT) return SERVER_PORT
  SERVER_PORT = await ipcRenderer.invoke('get-port')
  return SERVER_PORT
}

async function api(endpoint, method = 'GET') {
  const port = await getPort()
  if (!port) return { status: 'error', message: 'Server not running' }
  const res = await fetch(`http://127.0.0.1:${port}${endpoint}`, { method })
  return res.json()
}

window.api = api

document.getElementById('btn-close')?.addEventListener('click', () => ipcRenderer.send('win-close'))
document.getElementById('btn-minimize')?.addEventListener('click', () => ipcRenderer.send('win-minimize'))
document.getElementById('btn-maximize')?.addEventListener('click', () => ipcRenderer.send('win-maximize'))
document.getElementById('btn-settings')?.addEventListener('click', () => ipcRenderer.send('navigate', 'settings.html'))
document.getElementById('btn-back')?.addEventListener('click', () => ipcRenderer.send('navigate', 'index.html'))

document.addEventListener('keydown', (e) => {
  const blocked = (e.ctrlKey && e.shiftKey && e.key === 'I') ||
    (e.ctrlKey && e.key === 'c') ||
    (e.ctrlKey && e.key === 'u') ||
    e.key === 'F12'
  if (blocked) e.preventDefault()
})

document.addEventListener('contextmenu', (e) => e.preventDefault())
document.addEventListener('copy', (e) => e.preventDefault())
