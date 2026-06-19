// inspired by discord.
const SaveBar = (() => {
  let pending = {}
  let bar = null

  function ensure() {
    if (bar) return
    bar = document.createElement('div')
    bar.className = 'save-bar'
    bar.innerHTML = `
      <span class="save-bar-text">Careful — you have unsaved changes!</span>
      <div class="save-bar-actions">
        <button class="save-bar-btn save-bar-reset" id="sb-reset">Reset</button>
        <button class="save-bar-btn save-bar-apply" id="sb-apply">Apply</button>
      </div>
    `
    document.body.appendChild(bar)

    bar.querySelector('#sb-apply').addEventListener('click', async () => {
      for (const key of Object.keys(pending)) await pending[key].apply()
      pending = {}
      hide()
    })

    bar.querySelector('#sb-reset').addEventListener('click', () => {
      for (const key of Object.keys(pending)) pending[key].reset()
      pending = {}
      hide()
    })
  }

  function show() {
    ensure()
    requestAnimationFrame(() => bar.classList.add('save-bar--visible'))
  }

  function hide() {
    bar?.classList.remove('save-bar--visible')
  }

  return {
    watch(key, applyFn, resetFn) {
      pending[key] = { apply: applyFn, reset: resetFn }
      show()
    },
    unwatch(key) {
      delete pending[key]
      if (!Object.keys(pending).length) hide()
    },
    clean() {
      pending = {}
      hide()
    }
  }
})()

window.SaveBar = SaveBar
