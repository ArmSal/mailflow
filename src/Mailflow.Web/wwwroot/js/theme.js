// Applied in <head> before first paint so there is no flash. Saved choice wins, else the OS preference.
(function () {
  var saved = null;
  try { saved = localStorage.getItem("mf-theme"); } catch (e) { }
  var dark = saved ? saved === "dark" : window.matchMedia("(prefers-color-scheme: dark)").matches;
  var theme = dark ? "dark" : "light";
  document.documentElement.setAttribute("data-theme", theme);
  document.documentElement.setAttribute("data-bs-theme", theme);
})();
