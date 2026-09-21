import { expect, test } from '@playwright/test';

import { ADMIN_PASSWORD, ADMIN_USERNAME, ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { loginUi } from '../helpers/ui';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface User {
    id: string;
    username: string;
    role: string;
    enabled: boolean;
    failedLoginAttempts: number;
    lockedUntil: string | null;
}

interface LoginResponse {
    accessToken: string;
    accessTokenExpiresAt: string;
}

interface ApiKey {
    id: string;
    userId: string;
    name: string;
    enabled: boolean;
}

interface ApiKeyCreated {
    key: ApiKey;
    plaintextKey: string;
}

interface ProblemDetails {
    detail: string;
}

const MEMBER_USERNAME = 'journey-requester';
const MEMBER_PASSWORD = 'journey-requester-pa55!';
const MEMBER_NEW_PASSWORD = 'journey-requester-new-pa55!';

test.describe.serial('Journey 2: the locked door', () => {
    test('protects accounts, sessions and keys across their full lifecycle', async ({
        browser,
        page,
        playwright,
        request,
    }) => {
        test.setTimeout(900_000);

        let api!: ApiClient;
        let member!: User;
        let memberAccessToken = '';
        let currentMemberAccessToken = '';

        await test.step('restore the configured baseline and sign in as the administrator', async () => {
            api = await restoreCheckpoint('configured-baseline', async (restored) => {
                const users = await restored.get<Paged<User>>('/api/users?pageSize=100');
                member = users.items.find((user) => user.username === MEMBER_USERNAME)!;
                expect(member).toMatchObject({
                    username: 'journey-requester',
                    role: 'Requester',
                    enabled: true,
                    failedLoginAttempts: 0,
                    lockedUntil: null,
                });
                expect(
                    (await restored.get<Array<{ name: string }>>('/api/root-folders'))
                        .map((root) => root.name)
                        .sort(),
                ).toEqual(['Books', 'Movies', 'Music', 'TV']);
            });
            await loginUi(page);
            await page.goto('/');
            await expect(page.locator('[data-page="dashboard"]')).toBeVisible();
        });

        await test.step('a second administrator claim is refused now that setup is complete', async () => {
            const response = await request.post('/api/system/setup/admin', {
                data: { username: 'locked-door-admin', password: 'locked-door-admin-pa55!' },
            });
            expect(response.status()).toBe(400);
            expect((await response.json()) as ProblemDetails).toMatchObject({
                detail: 'Setup is complete.',
            });
            const users = await api.get<Paged<User>>('/api/users?pageSize=100');
            expect(users.items.some((user) => user.username === 'locked-door-admin')).toBe(false);
        });

        await test.step('the completed wizard is not reachable again', async () => {
            await page.goto('/setup');
            await expect(page).toHaveURL(`${integrationEnvironment.baseUrl}/`);
            await expect(page.locator('[data-page="dashboard"]')).toBeVisible();
        });

        await test.step('an unauthenticated browser is sent to login and returned to where it was going', async () => {
            const visitor = await browser.newPage();
            const deepLink = '/library/content/movie?journey=redirect-check#locked-door';
            await visitor.goto(deepLink);
            await expect(visitor).toHaveURL(`${integrationEnvironment.baseUrl}/login`);
            await visitor.locator('[data-field="username"]').fill(ADMIN_USERNAME);
            await visitor.locator('[data-field="password"]').fill(ADMIN_PASSWORD);
            await visitor.locator('[data-action="sign-in"]').click();
            await expect(visitor).toHaveURL(`${integrationEnvironment.baseUrl}${deepLink}`);
            await expect(visitor.locator('[data-page="library"]')).toBeVisible();
            await visitor.close();
        });

        await test.step('a wrong password fails in the form without navigating', async () => {
            const loginPage = await browser.newPage();
            await loginPage.goto('/login');
            await loginPage.locator('[data-field="username"]').fill(MEMBER_USERNAME);
            await loginPage.locator('[data-field="password"]').fill('wrong-password');
            await loginPage.locator('[data-action="sign-in"]').click();
            await expect(loginPage.locator('[data-testid="login-error"]')).toHaveText('Invalid username or password.');
            await expect(loginPage).toHaveURL(`${integrationEnvironment.baseUrl}/login`);
            await loginPage.close();
        });

        await test.step('four more wrong passwords lock the account after the form used the first attempt', async () => {
            for (let attempt = 0; attempt < 4; attempt += 1) {
                const response = await request.post('/api/auth/login', {
                    data: { username: MEMBER_USERNAME, password: 'wrong-password' },
                });
                expect(response.status()).toBe(401);
            }

            const locked = await api.get<User>(`/api/users/${member.id}`);
            expect(locked).toMatchObject({ failedLoginAttempts: 5, lockedUntil: expect.any(String) });
            expect(Date.parse(locked.lockedUntil!)).toBeGreaterThan(Date.now());

            const lockedLogin = await request.post('/api/auth/login', {
                data: { username: MEMBER_USERNAME, password: MEMBER_PASSWORD },
            });
            expect(lockedLogin.status()).toBe(401);
            expect(await api.get<User>(`/api/users/${member.id}`)).toMatchObject({
                failedLoginAttempts: 5,
                lockedUntil: locked.lockedUntil,
            });
        });

        await test.step('an administrator unlock restores the account and a real login succeeds', async () => {
            await api.post(`/api/users/${member.id}/unlock`);
            expect(await api.get<User>(`/api/users/${member.id}`)).toMatchObject({
                failedLoginAttempts: 0,
                lockedUntil: null,
            });

            const login = await request.post('/api/auth/login', {
                data: { username: MEMBER_USERNAME, password: MEMBER_PASSWORD },
            });
            expect(login.status()).toBe(200);
            memberAccessToken = ((await login.json()) as LoginResponse).accessToken;
            expect(await new ApiClient({ accessToken: memberAccessToken }).get<User>('/api/users/my')).toMatchObject({
                username: 'journey-requester',
            });
        });

        await test.step('a disabled account cannot sign in at all', async () => {
            await api.patch(`/api/users/${member.id}`, { enabled: { value: false } });
            await expect(
                new ApiClient().post('/api/auth/login', { username: MEMBER_USERNAME, password: MEMBER_PASSWORD }),
            ).rejects.toMatchObject({ status: 401 });
            await api.patch(`/api/users/${member.id}`, { enabled: { value: true } });
            await expect(
                new ApiClient({ accessToken: memberAccessToken }).get('/api/users/my'),
            ).rejects.toMatchObject({ status: 401 });
            const restored = await request.post('/api/auth/login', {
                data: { username: MEMBER_USERNAME, password: MEMBER_PASSWORD },
            });
            expect(restored.status()).toBe(200);
            memberAccessToken = ((await restored.json()) as LoginResponse).accessToken;
        });

        await test.step('the member changes their own password using their current one', async () => {
            const memberApi = new ApiClient({ accessToken: memberAccessToken });
            await expect(
                memberApi.post(`/api/users/${member.id}/password`, { newPassword: MEMBER_NEW_PASSWORD }),
            ).rejects.toMatchObject({ status: 404 });
            await expect(
                memberApi.post(`/api/users/${member.id}/password`, {
                    currentPassword: 'wrong-current-password',
                    newPassword: MEMBER_NEW_PASSWORD,
                }),
            ).rejects.toMatchObject({ status: 404 });
            await memberApi.post(`/api/users/${member.id}/password`, {
                currentPassword: MEMBER_PASSWORD,
                newPassword: MEMBER_NEW_PASSWORD,
            });
            await expect(
                new ApiClient().post('/api/auth/login', { username: MEMBER_USERNAME, password: MEMBER_PASSWORD }),
            ).rejects.toMatchObject({ status: 401 });
        });

        await test.step("the old session stops working after the member's own password change", async () => {
            await expect(new ApiClient({ accessToken: memberAccessToken }).get('/api/users/my'))
                .rejects.toMatchObject({ status: 401 });
        });

        await test.step('the member cannot escalate their own role', async () => {
            const login = await request.post('/api/auth/login', {
                data: { username: MEMBER_USERNAME, password: MEMBER_NEW_PASSWORD },
            });
            expect(login.status()).toBe(200);
            currentMemberAccessToken = ((await login.json()) as LoginResponse).accessToken;
            const memberApi = new ApiClient({ accessToken: currentMemberAccessToken });
            await expect(memberApi.patch('/api/users/my', { role: { value: 'Admin' } })).rejects.toMatchObject({
                status: 404,
            });
            expect(await memberApi.get<User>('/api/users/my')).toMatchObject({ role: 'Requester' });
        });

        await test.step('signing out invalidates the refresh token', async () => {
            const session = await playwright.request.newContext({ baseURL: integrationEnvironment.baseUrl });
            const login = await session.post('/api/auth/login', {
                data: { username: MEMBER_USERNAME, password: MEMBER_NEW_PASSWORD },
            });
            expect(login.status()).toBe(200);
            const accessToken = ((await login.json()) as LoginResponse).accessToken;
            const refreshCookie = (await session.storageState()).cookies.find(
                (cookie) => cookie.name === 'magpie.refresh',
            );
            expect(refreshCookie).toBeDefined();

            const logout = await session.post('/api/auth/logout', {
                headers: { Authorization: `Bearer ${accessToken}` },
            });
            expect(logout.status()).toBe(204);

            const replay = await playwright.request.newContext({
                baseURL: integrationEnvironment.baseUrl,
                storageState: { cookies: [refreshCookie!], origins: [] },
            });
            expect((await replay.post('/api/auth/refresh')).status()).toBe(401);
            await replay.dispose();
            await session.dispose();
        });

        await test.step('a refreshed session retires the previous access token', async () => {
            const session = await playwright.request.newContext({ baseURL: integrationEnvironment.baseUrl });
            const login = await session.post('/api/auth/login', {
                data: { username: MEMBER_USERNAME, password: MEMBER_NEW_PASSWORD },
            });
            expect(login.status()).toBe(200);
            const previousAccessToken = ((await login.json()) as LoginResponse).accessToken;
            const previousRefreshCookie = (await session.storageState()).cookies.find(
                (cookie) => cookie.name === 'magpie.refresh',
            );
            expect(previousRefreshCookie).toBeDefined();
            const refresh = await session.post('/api/auth/refresh');
            expect(refresh.status()).toBe(200);
            currentMemberAccessToken = ((await refresh.json()) as LoginResponse).accessToken;
            expect(currentMemberAccessToken).not.toBe(previousAccessToken);
            expect(
                await new ApiClient({ accessToken: currentMemberAccessToken }).get<User>('/api/users/my'),
            ).toMatchObject({ username: 'journey-requester' });
            await expect(new ApiClient({ accessToken: previousAccessToken }).get('/api/users/my'))
                .rejects.toMatchObject({ status: 401 });
            const replay = await playwright.request.newContext({
                baseURL: integrationEnvironment.baseUrl,
                storageState: { cookies: [previousRefreshCookie!], origins: [] },
            });
            expect((await replay.post('/api/auth/refresh')).status()).toBe(401);
            await replay.dispose();
            await session.dispose();
        });

        await test.step("a deleted user's token stops working immediately", async () => {
            await api.delete(`/api/users/${member.id}`);
            await expect(
                new ApiClient({ accessToken: currentMemberAccessToken }).get('/api/users/my'),
            ).rejects.toMatchObject({ status: 401 });
        });

        await test.step('a revoked API key is rejected on the very next request', async () => {
            const user = await api.post<User>('/api/users', {
                username: 'revoked-key-user',
                password: 'revoked-key-user-pa55!',
                role: 'Viewer',
                enabled: true,
            });
            const created = await api.post<ApiKeyCreated>('/api/api-keys?username=revoked-key-user', {
                name: 'Key to revoke',
            });
            const keyed = new ApiClient({ apiKey: created.plaintextKey });
            expect(await keyed.get<User>('/api/users/my')).toMatchObject({ id: user.id, username: 'revoked-key-user' });
            await api.delete(`/api/api-keys/${created.key.id}`);
            await expect(keyed.get('/api/users/my')).rejects.toMatchObject({ status: 401 });
        });

        await test.step('a key belonging to a disabled or deleted user stops working', async () => {
            const disabledUser = await api.post<User>('/api/users', {
                username: 'disabled-key-user',
                password: 'disabled-key-user-pa55!',
                role: 'Viewer',
                enabled: true,
            });
            const disabledKey = await api.post<ApiKeyCreated>('/api/api-keys?username=disabled-key-user', {
                name: 'Disabled user key',
            });
            const disabledKeyApi = new ApiClient({ apiKey: disabledKey.plaintextKey });
            expect(await disabledKeyApi.get<User>('/api/users/my')).toMatchObject({ username: 'disabled-key-user' });
            await api.patch(`/api/users/${disabledUser.id}`, { enabled: { value: false } });
            await expect(disabledKeyApi.get('/api/users/my')).rejects.toMatchObject({ status: 401 });

            const deletedUser = await api.post<User>('/api/users', {
                username: 'deleted-key-user',
                password: 'deleted-key-user-pa55!',
                role: 'Viewer',
                enabled: true,
            });
            const deletedKey = await api.post<ApiKeyCreated>('/api/api-keys?username=deleted-key-user', {
                name: 'Deleted user key',
            });
            const deletedKeyApi = new ApiClient({ apiKey: deletedKey.plaintextKey });
            expect(await deletedKeyApi.get<User>('/api/users/my')).toMatchObject({ username: 'deleted-key-user' });
            await api.delete(`/api/users/${deletedUser.id}`);
            await expect(deletedKeyApi.get('/api/users/my')).rejects.toMatchObject({ status: 401 });
        });

        await test.step('keys are renamed, and the administrator sees every key in the instance', async () => {
            const user = await api.post<User>('/api/users', {
                username: 'key-list-user',
                password: 'key-list-user-pa55!',
                role: 'Viewer',
                enabled: true,
            });
            const userKey = await api.post<ApiKeyCreated>('/api/api-keys?username=key-list-user', {
                name: 'Key before rename',
            });
            const adminKey = await api.post<ApiKeyCreated>('/api/api-keys', { name: 'Administrator list key' });

            const userKeys = await api.get<ApiKey[]>(`/api/users/${user.id}/api-keys`);
            expect(userKeys.some((key) => key.id === userKey.key.id)).toBe(true);
            const allKeys = await api.get<ApiKey[]>('/api/api-keys');
            expect(allKeys.some((key) => key.id === userKey.key.id)).toBe(true);
            expect(allKeys.some((key) => key.id === adminKey.key.id)).toBe(true);
        });

    });
});
