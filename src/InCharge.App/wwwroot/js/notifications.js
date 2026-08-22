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

let lastTag = null;

export async function showTestNotification(title, body) {
    if (!('serviceWorker' in navigator)) {
        throw new Error('Service worker wordt niet ondersteund in deze browser.');
    }

    const registration = await withTimeout(
        navigator.serviceWorker.ready,
        8000,
        'De service worker werd niet op tijd actief. Herlaad de pagina en probeer opnieuw.');

    // A shared tag only *updates* the existing notification, which Android does not
    // treat as a new alert: it neither vibrates again nor gets relayed to a paired
    // wearable (e.g. a Fitbit) after the first post. So every cue gets its own unique
    // tag to always post as a brand new notification, and we close the previous one
    // ourselves so they don't pile up in the notification shade.
    const tag = `incharge-${Date.now()}`;

    await registration.showNotification(title, {
        body: body,
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        vibrate: [200, 100, 200],
        tag: tag
    });

    if (lastTag) {
        const previous = await registration.getNotifications({ tag: lastTag });
        previous.forEach(notification => notification.close());
    }
    lastTag = tag;
}

function withTimeout(promise, timeoutMs, timeoutMessage) {
    return Promise.race([
        promise,
        new Promise((_, reject) => setTimeout(() => reject(new Error(timeoutMessage)), timeoutMs))
    ]);
}
