// Minimal service worker: only enables PWA installability and lets the app
// show notifications via ServiceWorkerRegistration.showNotification().
// Deliberately no asset pre-caching / offline support, so it activates
// (and navigator.serviceWorker.ready resolves) almost immediately.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
