const TOKEN_KEY = 'magpie_auth_token';

const TOKEN_EXPIRES_KEY = 'magpie_token_expires';

const SESSION_GENERATION_KEY = 'magpie_session_generation';

export const AUTH_STORAGE_EVENT = 'magpie:auth-storage';

export function authScope(baseUrl = import.meta.env.VITE_API_URL || '/api'): string {
    return new URL(baseUrl.replace(/\/+$/, ''), window.location.href).href;
}

function scopedKey(key: string, baseUrl?: string): string {
    const scope = authScope(baseUrl);
    return scope === new URL('/api', window.location.href).href ? key : `${key}:${scope}`;
}

export function sessionGeneration(baseUrl?: string): string {
    try {
        return localStorage.getItem(scopedKey(SESSION_GENERATION_KEY, baseUrl)) ?? 'legacy';
    } catch {
        return 'unavailable';
    }
}

export const tokenStorage = {
    get(baseUrl?: string): string | null {
        try {
            return localStorage.getItem(scopedKey(TOKEN_KEY, baseUrl));
        } catch {
            return null;
        }
    },

    set(token: string, baseUrl?: string): void {
        try {
            localStorage.setItem(scopedKey(TOKEN_KEY, baseUrl), token);
        } catch {
            void 0;
        }
    },

    remove(baseUrl?: string): void {
        try {
            localStorage.removeItem(scopedKey(TOKEN_KEY, baseUrl));
        } catch {
            void 0;
        }
    },

    exists(): boolean {
        return this.get() !== null;
    },
};

export const tokenExpirationStorage = {
    get storageKey(): string {
        return scopedKey(TOKEN_EXPIRES_KEY);
    },

    isStorageKey(key: string | null): boolean {
        return key === this.storageKey;
    },

    get(baseUrl?: string): Date | null {
        try {
            const value = localStorage.getItem(scopedKey(TOKEN_EXPIRES_KEY, baseUrl));
            return value ? new Date(value) : null;
        } catch {
            return null;
        }
    },

    set(expiresAt: Date, baseUrl?: string): void {
        try {
            localStorage.setItem(scopedKey(TOKEN_EXPIRES_KEY, baseUrl), expiresAt.toISOString());
        } catch {
            void 0;
        }
    },

    remove(baseUrl?: string): void {
        try {
            localStorage.removeItem(scopedKey(TOKEN_EXPIRES_KEY, baseUrl));
        } catch {
            void 0;
        }
    },
};

export function storeAuthTokens(accessToken: string, accessTokenExpiresAt: string, baseUrl?: string): void {
    tokenStorage.set(accessToken, baseUrl);
    tokenExpirationStorage.set(new Date(accessTokenExpiresAt), baseUrl);
    window.dispatchEvent(new Event(AUTH_STORAGE_EVENT));
}

export function clearAuthStorage(baseUrl?: string): void {
    try {
        localStorage.setItem(
            scopedKey(SESSION_GENERATION_KEY, baseUrl),
            crypto.getRandomValues(new Uint32Array(4)).join('-'),
        );
    } catch {
        void 0;
    }
    tokenStorage.remove(baseUrl);
    tokenExpirationStorage.remove(baseUrl);
    window.dispatchEvent(new Event(AUTH_STORAGE_EVENT));
}
