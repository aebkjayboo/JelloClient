document.addEventListener('DOMContentLoaded', () => {
  const el = document.getElementById('watermark')
  requestAnimationFrame(() => el.classList.add('visible'))
})