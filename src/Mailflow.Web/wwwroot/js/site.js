(function () {
  var root = document.documentElement;

  // Theme toggle: <button data-theme-toggle>
  var toggle = document.querySelector("[data-theme-toggle]");
  if (toggle) {
    var sync = function () { toggle.setAttribute("aria-pressed", root.getAttribute("data-theme") === "dark" ? "true" : "false"); };
    sync();
    toggle.addEventListener("click", function () {
      var next = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
      root.setAttribute("data-theme", next);
      root.setAttribute("data-bs-theme", next);
      try { localStorage.setItem("mf-theme", next); } catch (e) { }
      sync();
    });
  }

  // Narrow screens: side navigation opens as a drawer. <button data-nav-toggle>
  var navBtn = document.querySelector("[data-nav-toggle]");
  if (navBtn) {
    var setNav = function (open) {
      document.body.classList.toggle("nav-open", open);
      navBtn.setAttribute("aria-expanded", open ? "true" : "false");
    };
    navBtn.addEventListener("click", function () { setNav(!document.body.classList.contains("nav-open")); });
    document.addEventListener("keydown", function (e) { if (e.key === "Escape") setNav(false); });
    var scrim = document.querySelector(".nav-scrim");
    if (scrim) scrim.addEventListener("click", function () { setNav(false); });
  }

  // Toasts: any [data-autohide] inside .toast-region disappears after 6 s (initial page and htmx swaps).
  var sweep = function (el) {
    var nodes = el.matches && el.matches("[data-autohide]") ? [el] : [];
    el.querySelectorAll && el.querySelectorAll("[data-autohide]").forEach(function (n) { nodes.push(n); });
    nodes.forEach(function (n) { setTimeout(function () { n.remove(); }, 6000); });
  };
  document.body.addEventListener("htmx:load", function (e) { sweep(e.detail.elt); });
})();
