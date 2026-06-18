// Theme is local-only (no server round trip) so it applies instantly on load.
const STORAGE_KEY = 'jello_theme'
const DEFAULTS = { mode: 'dark', accent: '#ffffff' }

function loadTheme() {
  try { return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(STORAGE_KEY)) } }
  catch { return { ...DEFAULTS } }
}

function persistTheme(t) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(t))
}

function hexToRgb(hex) {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `${r} ${g} ${b}`
}

function applyTheme(t) {
  const root = document.documentElement
  root.setAttribute('data-mode', t.mode)
  root.style.setProperty('--accent', t.accent)
  root.style.setProperty('--accent-rgb', hexToRgb(t.accent))
  root.style.setProperty('--invert', t.mode === 'dark' ? '1' : '0')
}

const theme = {
  get() { return loadTheme() },
  apply() { applyTheme(loadTheme()) },
  setMode(mode) {
    const t = loadTheme()
    t.mode = mode
    persistTheme(t)
    applyTheme(t)
  },
  setAccent(hex) {
    const t = loadTheme()
    t.accent = hex
    persistTheme(t)
    applyTheme(t)
  },
  save(patch) {
    const t = { ...loadTheme(), ...patch }
    persistTheme(t)
    applyTheme(t)
  }
}

theme.apply()
window.theme = theme
