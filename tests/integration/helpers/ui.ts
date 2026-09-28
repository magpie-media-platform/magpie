import { expect } from '@playwright/test';
import type { BrowserContext, Page, Request } from '@playwright/test';

import { ADMIN_PASSWORD, ADMIN_USERNAME, UI_TOKEN_STORAGE_KEY, type ApiClient } from './api';
import { integrationEnvironment } from './environment';
import { until } from './wait';

interface MonitoredItem {
    monitoring: 'Unmonitored' | 'Monitored' | 'Shotgun' | null;
    effectiveMonitoring: 'Unmonitored' | 'Monitored' | 'Shotgun';
}

interface ObservedRequest {
    url: string;
    at: number;
}

const FIXTURE_IMAGE_HOST = '**/assets.journey.invalid/**';
const SCOPED_REFRESH_WINDOW_MS = 15_000;

// A 1x1 JPEG: the smallest body a browser decodes as an image.
const ONE_PIXEL_JPEG = Buffer.from(
    '/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==',
    'base64',
);

export async function serveFixtureImages(context: BrowserContext): Promise<void> {
    await context.route(FIXTURE_IMAGE_HOST, (route) =>
        route.fulfill({ status: 200, contentType: 'image/jpeg', body: ONE_PIXEL_JPEG }),
    );
}

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
            if (window.localStorage.getItem(key) === null) {
                window.localStorage.setItem(key, token);
            }
        },
        [UI_TOKEN_STORAGE_KEY, body.accessToken] as const,
    );
    await serveFixtureImages(page.context());
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
            if (window.localStorage.getItem(tokenKey) === null) {
                window.localStorage.setItem(tokenKey, token);
                window.localStorage.setItem('magpie_token_expires', expiresAt);
            }
        },
        [UI_TOKEN_STORAGE_KEY, body.accessToken, body.accessTokenExpiresAt] as const,
    );
    await serveFixtureImages(context);
}

export async function expectScopedEventRefresh(
    page: Page,
    api: ApiClient,
    visibleId: string,
    unrelatedId: string,
): Promise<void> {
    const observed: ObservedRequest[] = [];
    const record = (request: Request) => {
        const url = new URL(request.url());
        if (
            request.method() === 'GET' &&
            (url.pathname.startsWith('/api/content') || url.pathname.includes('/metadata'))
        ) {
            observed.push({ url: `${url.pathname}${url.search}`, at: Date.now() });
        }
    };

    const unrelated = await api.get<MonitoredItem>(`/api/content/${unrelatedId}`);
    const visible = await api.get<MonitoredItem>(`/api/content/${visibleId}`);
    page.on('request', record);
    try {
        const openedAt = Date.now();
        const since = (predicate: (url: string) => boolean): ObservedRequest[] =>
            observed.filter((request) => request.at >= openedAt && predicate(request.url));

        // The host emits both events in this order, so the visible refresh proves the unrelated event arrived first.
        await api.patch(`/api/content/${unrelatedId}`, { monitoring: { value: unrelated.effectiveMonitoring === 'Unmonitored' ? 'Monitored' : 'Unmonitored' } });
        await api.patch(`/api/content/${visibleId}`, { monitoring: { value: visible.effectiveMonitoring === 'Unmonitored' ? 'Monitored' : 'Unmonitored' } });
        await until(
            'the visible record to refresh after its event',
            async () => since((url) => url === `/api/content/${visibleId}`).length || undefined,
            SCOPED_REFRESH_WINDOW_MS,
            100,
        );

        expect(since((url) => url === `/api/content/${unrelatedId}`)).toHaveLength(0);
        expect(since((url) => url === `/api/content/${visibleId}`)).toHaveLength(1);
        const metadataRequests = since((url) => url.startsWith(`/api/content/${visibleId}/metadata`));
        expect(new Set(metadataRequests.map((request) => request.url)).size).toBe(metadataRequests.length);
    } finally {
        page.off('request', record);
        await Promise.all([
            api.patch(`/api/content/${unrelatedId}`, { monitoring: { value: unrelated.monitoring } }),
            api.patch(`/api/content/${visibleId}`, { monitoring: { value: visible.monitoring } }),
        ]);
    }
}
