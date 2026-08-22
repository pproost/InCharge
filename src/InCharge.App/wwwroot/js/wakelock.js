// Keeps the screen from auto-locking while an activity is running, so the
// interval timer keeps ticking accurately in the foreground tab. This does
// NOT prevent the screen from turning off if the user presses the power
// button themselves — only the OS-level idle timeout is deferred.
let wakeLock = null;
let desired = false;

async function acquire() {
    if (!('wakeLock' in navigator)) {
        return false;
    }
    try {
        wakeLock = await navigator.wakeLock.request('screen');
        wakeLock.addEventListener('release', () => { wakeLock = null; });
        return true;
    } catch {
        return false;
    }
}

export async function requestWakeLock() {
    desired = true;
    return await acquire();
}

export async function releaseWakeLock() {
    desired = false;
    if (wakeLock) {
        try {
            await wakeLock.release();
        } catch {
            // Already released.
        }
        wakeLock = null;
    }
}

document.addEventListener('visibilitychange', async () => {
    if (desired && document.visibilityState === 'visible' && !wakeLock) {
        await acquire();
    }
});
