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

    const registration = await navigator.serviceWorker.ready;
    await registration.showNotification(title, {
        body: body,
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        vibrate: [200, 100, 200],
        tag: 'incharge-test'
    });
}
