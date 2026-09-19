var CACHE_PREFIX = 'novara-web-shell-';
var CACHE = CACHE_PREFIX + 'v17';
var SCOPE = self.registration.scope;


var SHELL = ['', 'index.html', 'viewer.css', 'app.css', 'editor.css', 'viewer.js', 'app.js', 'edit.js', 'manifest.webmanifest', 'icon.svg']
  .map(function (path) { return new URL(path, SCOPE).href; });





var SHELL_PATHS = {};
SHELL.forEach(function (href) { SHELL_PATHS[new URL(href).pathname] = href; });

self.addEventListener('install', function (event) {
  event.waitUntil(
    caches.open(CACHE)
      .then(function (cache) { return cache.addAll(SHELL); })
      .then(function () { return self.skipWaiting(); })
  );
});

self.addEventListener('activate', function (event) {
  event.waitUntil(
    caches.keys()
      .then(function (keys) {



        return Promise.all(keys.map(function (key) {
          return key.indexOf(CACHE_PREFIX) === 0 && key !== CACHE ? caches.delete(key) : null;
        }));
      })
      .then(function () { return self.clients.claim(); })
  );
});

self.addEventListener('fetch', function (event) {
  var request = event.request;
  if (request.method !== 'GET') return;

  var url = new URL(request.url);

  var canonical = SHELL_PATHS[url.pathname];
  if (!canonical) return;

  event.respondWith(
    caches.match(canonical).then(function (cached) {
      if (cached) return cached;

      return fetch(request).then(function (response) {
        if (response.ok) {
          var copy = response.clone();
          caches.open(CACHE).then(function (cache) { cache.put(canonical, copy); });
        }
        return response;
      });
    })
  );
});
