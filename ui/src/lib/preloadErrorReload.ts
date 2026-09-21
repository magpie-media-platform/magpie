const GUARD_KEY = 'magpie:reloaded-for-preload-error';
const GUARD_TTL_MS = 10_000;

const CHUNK_ERROR_PATTERNS = [
    'failed to fetch dynamically imported module',
    'error loading dynamically imported module',
    'unable to preload',
];

function isChunkLoadMessage(message: string): boolean {
    const normalized = message.toLowerCase();
    return CHUNK_ERROR_PATTERNS.some((pattern) => normalized.includes(pattern));
}

export function reloadForPreloadError(now: number = Date.now()): boolean {
    let raw: string | null = null;
    try {
        raw = sessionStorage.getItem(GUARD_KEY);
    } catch {
        // sessionStorage can throw in privacy modes, where an unreadable guard means no prior reload.
    }

    if (raw !== null) {
        const reloadedAt = Number(raw);
        if (Number.isFinite(reloadedAt) && now - reloadedAt < GUARD_TTL_MS) {
            return false;
        }
    }

    try {
        sessionStorage.setItem(GUARD_KEY, String(now));
    } catch {
        // The reload proceeds without the guard.
    }

    window.location.reload();
    return true;
}

export function installPreloadErrorReload(): void {
    window.addEventListener('vite:preloadError', (event) => {
        event.preventDefault();
        reloadForPreloadError();
    });

    window.addEventListener('unhandledrejection', (event) => {
        const reason: unknown = event.reason;
        const message = reason instanceof Error ? reason.message : typeof reason === 'string' ? reason : '';
        if (message && isChunkLoadMessage(message)) {
            event.preventDefault();
            reloadForPreloadError();
        }
    });
}
