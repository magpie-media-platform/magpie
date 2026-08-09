import type { Page } from '@playwright/test';
import type { BrowserContext } from '@playwright/test';

import { ADMIN_PASSWORD, ADMIN_USERNAME, UI_TOKEN_STORAGE_KEY } from './api';
import { integrationEnvironment } from './environment';

export async function loginUi(page: Page): Promise<void> {
    const baseURL = integrationEnvironment.baseUrl;
    const response = await page.context().request.post(`${baseURL}/api/auth/login`, {
        data: { username: ADMIN_USERNAME, password: ADMIN_PASSWORD },
    });
    if (!response.ok()) {
        throw new Error(`in-browser login failed with ${response.status()}`);
    }

    const body = (await response.json()) as { accessToken: string };
    await page.addInitScript(
        ([key, token]) => {
            // the app rotates tokens and retires the old one, so never stomp a live session
            if (window.localStorage.getItem(key) === null) {
                window.localStorage.setItem(key, token);
            }
        },
        [UI_TOKEN_STORAGE_KEY, body.accessToken] as const,
    );
}

export async function loginUiContext(context: BrowserContext, username: string, password: string): Promise<void> {
    const baseURL = integrationEnvironment.baseUrl;
    const response = await context.request.post(`${baseURL}/api/auth/login`, {
        data: { username, password },
    });
    if (!response.ok()) {
        throw new Error(`in-browser login failed with ${response.status()}`);
    }

    const body = (await response.json()) as { accessToken: string; accessTokenExpiresAt: string };
    await context.addInitScript(
        ([tokenKey, token, expiresAt]) => {
            // the app rotates tokens and retires the old one, so never stomp a live session
            if (window.localStorage.getItem(tokenKey) === null) {
                window.localStorage.setItem(tokenKey, token);
                window.localStorage.setItem('magpie_token_expires', expiresAt);
            }
        },
        [UI_TOKEN_STORAGE_KEY, body.accessToken, body.accessTokenExpiresAt] as const,
    );
}
