const TOKEN_KEY = 'magpie_auth_token';

const TOKEN_EXPIRES_KEY = 'magpie_token_expires';

export const tokenStorage = {
    get(): string | null {
        try {
            return localStorage.getItem(TOKEN_KEY);
        } catch {
            return null;
        }
    },

    set(token: string): void {
        try {
            localStorage.setItem(TOKEN_KEY, token);
        } catch {
            void 0;
        }
    },

    remove(): void {
        try {
            localStorage.removeItem(TOKEN_KEY);
        } catch {
            void 0;
        }
    },

    exists(): boolean {
        return this.get() !== null;
    },
};

export const tokenExpirationStorage = {
    storageKey: TOKEN_EXPIRES_KEY,

    isStorageKey(key: string | null): boolean {
        return key === TOKEN_EXPIRES_KEY;
    },

    get(): Date | null {
        try {
            const value = localStorage.getItem(TOKEN_EXPIRES_KEY);
            return value ? new Date(value) : null;
        } catch {
            return null;
        }
    },

    set(expiresAt: Date): void {
        try {
            localStorage.setItem(TOKEN_EXPIRES_KEY, expiresAt.toISOString());
        } catch {
            void 0;
        }
    },

    remove(): void {
        try {
            localStorage.removeItem(TOKEN_EXPIRES_KEY);
        } catch {
            void 0;
        }
    },
};

export function storeAuthTokens(accessToken: string, accessTokenExpiresAt: string): void {
    tokenStorage.set(accessToken);
    tokenExpirationStorage.set(new Date(accessTokenExpiresAt));
}

export function clearAuthStorage(): void {
    tokenStorage.remove();
    tokenExpirationStorage.remove();
}
