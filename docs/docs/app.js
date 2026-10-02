// ExtractX web: nav móvil, animaciones, año y versión viva desde GitHub Releases.
(function () {
  var toggle = document.querySelector('.nav-toggle');
  var nav = document.getElementById('nav');
  if (toggle && nav) toggle.addEventListener('click', function () {
    nav.classList.toggle('open');
  });

  var y = document.getElementById('year');
  if (y) y.textContent = new Date().getFullYear();

  document.querySelectorAll('a[href^="#"]').forEach(function (a) {
    a.addEventListener('click', function (ev) {
      var t = document.querySelector(a.getAttribute('href'));
      if (t) { ev.preventDefault(); t.scrollIntoView({ behavior: 'smooth' }); }
    });
  });

  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
      });
    }, { threshold: 0.12 });
    document.querySelectorAll('.reveal').forEach(function (el) { io.observe(el); });
  } else {
    document.querySelectorAll('.reveal').forEach(function (el) { el.classList.add('in'); });
  }

  // Versión viva: si el repo ya tiene Releases, muestra la última y apunta las descargas.
  var base = 'https://api.github.com/repos/bddjf00-cell/ExtractX/releases/latest';
  fetch(base).then(function (r) {
    if (!r.ok) throw new Error('sin releases');
    return r.json();
  }).then(function (rel) {
    var tag = (rel.tag_name || '').trim();
    if (!tag) return;
    document.querySelectorAll('#latest-ver').forEach(function (el) { el.textContent = tag; });
    var fy = document.getElementById('foot-ver');
    if (fy) fy.textContent = tag;
    (rel.assets || []).forEach(function (a) {
      var n = (a.name || '').toLowerCase(), u = a.browser_download_url;
      if (!u) return;
      if (n.indexOf('setup') !== -1) {
        var s = document.getElementById('dl-setup');
        if (s) s.href = u;
        var t = document.getElementById('dl-top');
        if (t) t.href = u;
      }
      if (/extractx-v.*\.exe$/.test(n) && n.indexOf('setup') === -1) {
        var p = document.getElementById('dl-portable');
        if (p) p.href = u;
      }
    });
  }).catch(function () { /* sin red o sin releases: se queda la versión local */ });
})();
