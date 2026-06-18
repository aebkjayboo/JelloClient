const { app, BrowserWindow, ipcMain, dialog, screen } = require('electron')
const path = require('path')
const { spawn } = require('child_process')

let win
let watermarkWin
let pyServer
let serverPort = null

function startPythonServer() {
  return new Promise((resolve, reject) => {
    const serverPath = path.join(__dirname, 'src/PY/server.py')
    pyServer = spawn('python', [serverPath], { stdio: ['ignore', 'pipe', 'pipe'] })

    pyServer.stdout.on('data', (data) => {
      const match = data.toString().trim().match(/PORT:(\d+)/)
      if (match && !serverPort) {
        serverPort = parseInt(match[1])
        resolve(serverPort)
      }
    })

    pyServer.stderr.on('data', (data) => console.error('[Python]', data.toString().trim()))
    pyServer.on('error', (err) => {
      console.error('[Python] Failed to start:', err)
      reject(err)
    })

    setTimeout(() => {
      if (!serverPort) reject(new Error('Python server timeout'))
    }, 8000)
  })
}

function createWindow(port) {
  win = new BrowserWindow({
    width: 900,
    height: 650,
    icon: path.join(__dirname, 'assets', 'icon.ico'),
    frame: false,
    webPreferences: {
      devTools: false,
      nodeIntegration: true,
      contextIsolation: false
    }
  })

  win.setMenu(null)
  win.loadFile(path.join(__dirname, 'src/pages/index.html'))

  win.on('closed', () => {
    if (watermarkWin && !watermarkWin.isDestroyed()) watermarkWin.close()
    win = null
  })

  ipcMain.on('win-minimize', () => win.minimize())
  ipcMain.on('win-maximize', () => win.isMaximized() ? win.unmaximize() : win.maximize())
  ipcMain.on('win-close', () => win.close())
  ipcMain.on('navigate', (e, page) => win.loadFile(path.join(__dirname, 'src/pages', page)))
  ipcMain.handle('get-port', () => port)
}

function createWatermarkWindow() {
  if (watermarkWin && !watermarkWin.isDestroyed()) return

  const { width: screenWidth } = screen.getPrimaryDisplay().workAreaSize
  const WIDTH = 180
  const HEIGHT = 110
  const MARGIN = 16

  watermarkWin = new BrowserWindow({
    width: WIDTH,
    height: HEIGHT,
    x: screenWidth - WIDTH - MARGIN,
    y: MARGIN,
    transparent: true,
    frame: false,
    resizable: false,
    movable: false,
    minimizable: false,
    maximizable: false,
    skipTaskbar: true,
    focusable: false,
    hasShadow: false,
    show: false,
    webPreferences: {
      devTools: false
    }
  })

  watermarkWin.setIgnoreMouseEvents(true)
  watermarkWin.setAlwaysOnTop(true, 'screen-saver')
  watermarkWin.loadFile(path.join(__dirname, 'src/pages/watermark.html'))

  watermarkWin.once('ready-to-show', () => watermarkWin.show())
  watermarkWin.on('closed', () => { watermarkWin = null })
}

ipcMain.handle('select-font-file', async () => {
  const result = await dialog.showOpenDialog(win, {
    title: 'Select a font file',
    filters: [{ name: 'TrueType Fonts', extensions: ['ttf'] }],
    properties: ['openFile']
  })
  return result.canceled || !result.filePaths.length ? null : result.filePaths[0]
})

app.whenReady().then(async () => {
  try {
    const port = await startPythonServer()
    createWindow(port)
  } catch (err) {
    console.error('Could not start Python server:', err)
    createWindow(null)
  }
  createWatermarkWindow()
})

app.on('window-all-closed', () => {
  if (pyServer) pyServer.kill()
  if (process.platform !== 'darwin') app.quit()
})

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow(serverPort)
    createWatermarkWindow()
  }
})