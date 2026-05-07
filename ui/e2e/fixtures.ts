import { test as base, type Page } from '@playwright/test';

export const Role = {
    Viewer: 0,
    Requester: 1,
    Manager: 2,
    Admin: 3,
} as const;

export type Role = (typeof Role)[keyof typeof Role];

export interface MockUser {
    id: string;
    username: string;
    role: Role;
}

export const testUsers: Record<string, MockUser> = {
    admin: { id: 'user-admin', username: 'admin', role: Role.Admin },
    manager: { id: 'user-manager', username: 'manager', role: Role.Manager },
    requester: { id: 'user-requester', username: 'requester', role: Role.Requester },
    viewer: { id: 'user-viewer', username: 'viewer', role: Role.Viewer },
};

function createMockToken(user: MockUser): string {
    const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
    const payload = btoa(
        JSON.stringify({
            sub: user.id,
            name: user.username,
            role: String(user.role),
            exp: Math.floor(Date.now() / 1000) + 3600,
        })
    );
    return `${header}.${payload}.mock-signature`;
}

async function setupAuth(page: Page, user: MockUser): Promise<void> {
    const token = createMockToken(user);
    const expires = new Date(Date.now() + 3600000).toISOString();

    await page.addInitScript(
        ({ token, expires }) => {
            localStorage.setItem('magpie_auth_token', token);
            localStorage.setItem('magpie_refresh_token', 'mock-refresh-token');
            localStorage.setItem('magpie_token_expires', expires);
        },
        { token, expires }
    );
}

async function clearAuth(page: Page): Promise<void> {
    await page.addInitScript(() => {
        localStorage.removeItem('magpie_auth_token');
        localStorage.removeItem('magpie_refresh_token');
        localStorage.removeItem('magpie_token_expires');
    });
}

export const test = base.extend<{
    adminPage: Page;
    managerPage: Page;
    requesterPage: Page;
    viewerPage: Page;
    unauthenticatedPage: Page;
}>({
    adminPage: async ({ page }, use) => {
        await setupAuth(page, testUsers.admin);
        await use(page);
    },
    managerPage: async ({ page }, use) => {
        await setupAuth(page, testUsers.manager);
        await use(page);
    },
    requesterPage: async ({ page }, use) => {
        await setupAuth(page, testUsers.requester);
        await use(page);
    },
    viewerPage: async ({ page }, use) => {
        await setupAuth(page, testUsers.viewer);
        await use(page);
    },
    unauthenticatedPage: async ({ page }, use) => {
        await clearAuth(page);
        await use(page);
    },
});

export { expect } from '@playwright/test';
