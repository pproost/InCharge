export function isSupported() {
    return 'Notification' in window && 'serviceWorker' in navigator;
}

export function getPermission() {
    return 'Notification' in window ? Notification.permission : 'unsupported';
}

export async function requestPermission() {
    if (!('Notification' in window)) {
        return 'unsupported';
    }
    return await Notification.requestPermission();
}

export async function showTestNotification(title, body) {
    if (!('serviceWorker' in navigator)) {
        throw new Error('Service worker wordt niet ondersteund in deze browser.');
    }

    const registration = await withTimeout(
        navigator.serviceWorker.ready,
        8000,
        'De service worker werd niet op tijd actief. Herlaad de pagina en probeer opnieuw.');

    await registration.showNotification(title, {
        body: body,
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        vibrate: [200, 100, 200],
        tag: 'incharge-test',
        renotify: true
    });
}

function withTimeout(promise, timeoutMs, timeoutMessage) {
    return Promise.race([
        promise,
        new Promise((_, reject) => setTimeout(() => reject(new Error(timeoutMessage)), timeoutMs))
    ]);
}
