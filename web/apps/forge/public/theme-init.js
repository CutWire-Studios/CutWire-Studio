// Applied before first paint so a stored theme does not flash the other one. External so the CSP needs no inline-script exception.
;(function () {
  try {
    var stored = localStorage.getItem("cw-theme")
    if (stored === "light" || stored === "dark") document.documentElement.dataset.theme = stored
  } catch (_) {}
})()
