// Service worker do ClanHub (PWA): guarda o "casco" do app para abrir rápido e funcionar offline.
// A API (/api, /internal) NUNCA é guardada: dados de clã precisam estar sempre atualizados.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => { if (event.data === 'SKIP_WAITING') self.skipWaiting(); });

const cacheNamePrefix = 'clanhub-shell-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];
const neverCache = [/^\/api\//, /^\/internal\//, /^\/health/, /^\/alive/, /^\/openapi/, /^\/scalar/];

async function onInstall(event) {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    const request = event.request;
    const url = new URL(request.url);
    if (request.method !== 'GET' || url.origin !== self.location.origin
        || neverCache.some(pattern => pattern.test(url.pathname))) {
        return fetch(request);
    }

    const cache = await caches.open(cacheName);
    if (request.mode === 'navigate') {
        // Páginas do SPA: sempre o index.html em cache (o roteamento é feito pelo Blazor).
        const shell = await cache.match('index.html');
        try { return await fetch(request); } catch { return shell ?? Response.error(); }
    }
    const cached = await cache.match(request);
    return cached || fetch(request);
}