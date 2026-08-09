import { expect, test, type BrowserContext, type Page } from '@playwright/test';

import { ADMIN_PASSWORD, ADMIN_USERNAME, ApiClient, loginAdmin } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { loginUi, loginUiContext } from '../helpers/ui';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface User {
    id: string;
    username: string;
    role: string;
    enabled: boolean;
}

interface ApiKey {
    id: string;
    userId: string;
    name: string;
    keyPrefix: string;
}

interface ApiKeyCreated {
    key: ApiKey;
    plaintextKey: string;
}

interface EffectivePermissions {
    role: string;
    permissions: Record<string, { requiredRole: string; granted: boolean }>;
}

interface JobExecution {
    id: string;
    createdByUserId: string | null;
}

interface ContentFilter {
    id: string;
    ownerUserId: string | null;
    name: string;
}

const MANAGER_USERNAME = 'household-manager';
const MANAGER_PASSWORD = 'household-manager-pa55!';
const REQUESTER_USERNAME = 'household-requester';
const REQUESTER_PASSWORD = 'household-requester-pa55!';
const VIEWER_USERNAME = 'household-viewer';
const VIEWER_PASSWORD = 'household-viewer-pa55!';
const MISSING_CONTENT_ID = '00000000-0000-0000-0000-000000000003';
test.describe.serial("Journey 3: staff the household and prove everyone's reach", () => {
    test("staffs the household and proves everyone's reach", async ({ browser, page }) => {
        test.setTimeout(900_000);

        let adminApi!: ApiClient;
        let managerApi!: ApiClient;
        let requesterApi!: ApiClient;
        let viewerApi!: ApiClient;
        let adminAccessToken = '';
        let managerAccessToken = '';
        let requesterAccessToken = '';
        let viewerAccessToken = '';
        let baselineUserCount = 0;
        let manager!: User;
        let requester!: User;
        let viewer!: User;
        let managerContext!: BrowserContext;
        let requesterContext!: BrowserContext;
        let viewerContext!: BrowserContext;
        let managerPage!: Page;
        let requesterPage!: Page;
        let viewerPage!: Page;
        let adminExecutionId = '';

        await test.step('restore the configured baseline and sign in as the administrator', async () => {
            await restoreCheckpoint('configured-baseline', async (restored) => {
                const users = await restored.get<Paged<User>>('/api/users?pageSize=100');
                baselineUserCount = users.totalItems;
                expect(users.items).toContainEqual(
                    expect.objectContaining({
                        username: ADMIN_USERNAME,
                        role: 'Admin',
                        enabled: true,
                    }),
                );
                expect(users.items).toContainEqual(
                    expect.objectContaining({
                        username: 'journey-requester',
                        role: 'Requester',
                        enabled: true,
                    }),
                );
            });
            const adminLogin = await loginAdmin(ADMIN_USERNAME, ADMIN_PASSWORD);
            adminApi = adminLogin.api;
            adminAccessToken = adminLogin.accessToken;
            await loginUi(page);
            await page.goto('/admin/users');
            await expect(page.locator('[data-page="users"]')).toBeVisible();
        });

        await test.step('create a manager, a requester and a viewer through the users page', async () => {
            await page.locator('[data-action="add-user"]').first().click();
            const form = page.locator('[data-component="user-form"]');
            await expect(form).toBeVisible();
            await form.locator('[data-action="create"]').click();
            await expect(form).toContainText('Username is required');
            await expect(form).toContainText('Password must be at least 8 characters');
            await form.locator('[data-field="password"]').fill('short');
            await expect(form).toContainText('Password must be at least 8 characters');
            await form.locator('[data-field="username"]').fill(MANAGER_USERNAME);
            await form.locator('[data-field="password"]').fill(MANAGER_PASSWORD);
            await form.locator('[data-field="role"]').click();
            await page.getByRole('option', { name: 'Manager', exact: true }).click();
            await form.locator('[data-action="create"]').click();
            await expect(form).toBeHidden();
            await createLocalUser(page, REQUESTER_USERNAME, REQUESTER_PASSWORD, 'Requester');
            await createLocalUser(page, VIEWER_USERNAME, VIEWER_PASSWORD, 'Viewer');

            const users = await adminApi.get<Paged<User>>('/api/users?pageSize=100');
            manager = users.items.find((user) => user.username === MANAGER_USERNAME)!;
            requester = users.items.find((user) => user.username === REQUESTER_USERNAME)!;
            viewer = users.items.find((user) => user.username === VIEWER_USERNAME)!;
            expect(manager).toMatchObject({
                username: MANAGER_USERNAME,
                role: 'Manager',
                enabled: true,
            });
            expect(requester).toMatchObject({
                username: REQUESTER_USERNAME,
                role: 'Requester',
                enabled: true,
            });
            expect(viewer).toMatchObject({
                username: VIEWER_USERNAME,
                role: 'Viewer',
                enabled: true,
            });
            expect(users.totalItems).toBe(baselineUserCount + 3);
        });

        await test.step('a duplicate username is refused and creates nothing', async () => {
            const countBefore = (await adminApi.get<Paged<User>>('/api/users?pageSize=100')).totalItems;
            await page.locator('[data-action="add-user"]').first().click();
            const form = page.locator('[data-component="user-form"]');
            await form.locator('[data-field="username"]').fill(MANAGER_USERNAME);
            await form.locator('[data-field="password"]').fill(MANAGER_PASSWORD);
            await form.locator('[data-field="role"]').click();
            await page.getByRole('option', { name: 'Manager', exact: true }).click();
            const duplicateResponse = page.waitForResponse(
                (response) => response.url().endsWith('/api/users') && response.request().method() === 'POST',
            );
            await form.locator('[data-action="create"]').click();
            const response = await duplicateResponse;
            expect(response.status()).toBe(409);
            expect(await response.json()).toMatchObject({
                detail: `A user with the username '${MANAGER_USERNAME}' already exists.`,
            });
            await expect(page.locator('[data-component="toast"][data-variant="error"]')).toContainText(
                'Failed to create user',
            );
            expect((await adminApi.get<Paged<User>>('/api/users?pageSize=100')).totalItems).toBe(countBefore);
            await form.locator('[data-action="cancel"]').click();
            await page.locator('[data-component="unsaved-changes-dialog"] [data-action="discard"]').click();
            await expect(form).toBeHidden();
        });

        await test.step('the last administrator cannot be deleted or demoted', async () => {
            const users = await adminApi.get<Paged<User>>('/api/users?pageSize=100');
            const administrator = users.items.find((user) => user.username === ADMIN_USERNAME)!;
            await expect(
                adminApi.patch(`/api/users/${administrator.id}`, {
                    role: { value: 'Manager' },
                }),
            ).rejects.toMatchObject({ status: 404 });
            await expect(adminApi.delete(`/api/users/${administrator.id}`)).rejects.toMatchObject({ status: 404 });
            expect(await adminApi.get<User>(`/api/users/${administrator.id}`)).toMatchObject({
                username: ADMIN_USERNAME,
                role: 'Admin',
                enabled: true,
            });
        });

        await test.step('the user list pages and searches once there are more people than fit on a page', async () => {
            for (let index = 1; index <= 24; index += 1) {
                await adminApi.post('/api/users', {
                    username: `household-viewer-${String(index).padStart(2, '0')}`,
                    password: `household-viewer-${String(index).padStart(2, '0')}-pa55!`,
                    role: 'Viewer',
                    enabled: true,
                });
            }

            await page.goto('/admin/users');
            const totalUsers = baselineUserCount + 27;
            const usersPanel = page.getByRole('tabpanel', { name: /^Users\b/ });
            await expect(page.locator('[data-tab="users"] [data-testid="users-page__tab-count"]')).toHaveText(
                String(totalUsers),
            );
            await expect(usersPanel.locator('[data-testid="users-page__item"]')).toHaveCount(25);
            const pager = page.locator('[data-component="pager"]');
            await expect(pager).toContainText('Page 1 of 2');
            await pager.locator('[data-action="next-page"]').click();
            await expect(pager).toContainText('Page 2 of 2');
            await expect(usersPanel.locator('[data-testid="users-page__item"]')).toHaveCount(totalUsers - 25);
            await pager.locator('[data-action="first-page"]').click();
        });

        await test.step('mint an API key for the viewer from the users page, reveal it once, and delete it', async () => {
            await page.locator('[data-tab="api-keys"]').click();
            await page.locator('[data-action="add-api-key"]').first().click();
            const createDialog = page.locator('[data-component="create-api-key-dialog"]');
            await createDialog.locator('[data-field="api-key-name"]').fill('Viewer phone script');
            await createDialog.locator('[data-action="submit-create-key"]').click();

            const revealDialog = page.locator('[data-component="reveal-api-key-dialog"]');
            await expect(revealDialog).toBeVisible();
            const plaintextKey = (await revealDialog.locator('[data-testid="reveal-api-key-value"]').textContent())!;
            expect(plaintextKey).toMatch(/^mgp_/);
            const createdKey = (await adminApi.get<ApiKey[]>('/api/api-keys')).find(
                (key) => key.name === 'Viewer phone script',
            )!;

            await revealDialog.locator('[data-action="dismiss-reveal-key"]').click();
            await expect(revealDialog).toBeHidden();
            await expect(page.locator('body')).not.toContainText(plaintextKey);
            await page.locator('[data-filter="scope"][data-value="all"]').click();
            const keyRow = page.locator(`[data-api-key-id="${createdKey.id}"]`);
            await expect(keyRow).toContainText(`${createdKey.keyPrefix}...`);
            await expect(keyRow).not.toContainText(plaintextKey);
            await keyRow.locator('[data-action="delete"]').click();
            await page.locator('[data-component="delete-confirm-dialog"] [data-action="confirm-delete"]').click();
            await expect(keyRow).toBeHidden();
        });

        await test.step('each role reports exactly its own permission set', async () => {
            const managerLogin = await loginAdmin(MANAGER_USERNAME, MANAGER_PASSWORD);
            managerApi = managerLogin.api;
            managerAccessToken = managerLogin.accessToken;
            const requesterLogin = await loginAdmin(REQUESTER_USERNAME, REQUESTER_PASSWORD);
            requesterApi = requesterLogin.api;
            requesterAccessToken = requesterLogin.accessToken;
            const viewerLogin = await loginAdmin(VIEWER_USERNAME, VIEWER_PASSWORD);
            viewerApi = viewerLogin.api;
            viewerAccessToken = viewerLogin.accessToken;

            managerContext = await browser.newContext();
            await loginUiContext(managerContext, MANAGER_USERNAME, MANAGER_PASSWORD);
            managerPage = await managerContext.newPage();
            requesterContext = await browser.newContext();
            await loginUiContext(requesterContext, REQUESTER_USERNAME, REQUESTER_PASSWORD);
            requesterPage = await requesterContext.newPage();
            viewerContext = await browser.newContext();
            await loginUiContext(viewerContext, VIEWER_USERNAME, VIEWER_PASSWORD);
            viewerPage = await viewerContext.newPage();

            const cases: Array<[string, ApiClient, string[]]> = [
                [
                    'Viewer',
                    viewerApi,
                    [
                        'activity.view',
                        'calendar.view',
                        'content.view',
                        'downloads.view',
                        'filters.view',
                        'subjects.view',
                        'tags.view',
                        'wishlists.view',
                    ],
                ],
                [
                    'Requester',
                    requesterApi,
                    [
                        'activity.view',
                        'calendar.view',
                        'content.request',
                        'content.view',
                        'downloads.control',
                        'downloads.view',
                        'filters.own',
                        'filters.view',
                        'jobs.schedule',
                        'subjects.edit',
                        'subjects.view',
                        'tags.view',
                        'wishlists.own',
                        'wishlists.view',
                    ],
                ],
                [
                    'Manager',
                    managerApi,
                    [
                        'activity.view',
                        'calendar.view',
                        'content.add',
                        'content.delete',
                        'content.edit',
                        'content.request',
                        'content.view',
                        'downloads.control',
                        'downloads.manage',
                        'downloads.view',
                        'filters.manage',
                        'filters.own',
                        'filters.view',
                        'jobs.run',
                        'jobs.schedule',
                        'subjects.edit',
                        'subjects.view',
                        'tags.manage',
                        'tags.view',
                        'wishlists.manage',
                        'wishlists.own',
                        'wishlists.view',
                    ],
                ],
                [
                    'Admin',
                    adminApi,
                    [
                        'activity.view',
                        'backups.manage',
                        'calendar.view',
                        'clients.manage',
                        'content.add',
                        'content.delete',
                        'content.edit',
                        'content.request',
                        'content.view',
                        'downloads.control',
                        'downloads.manage',
                        'downloads.view',
                        'extensions.manage',
                        'files.browse',
                        'filters.manage',
                        'filters.own',
                        'filters.view',
                        'folders.manage',
                        'indexers.manage',
                        'jobs.manage',
                        'jobs.run',
                        'jobs.schedule',
                        'jobs.view',
                        'quality.manage',
                        'quality.view',
                        'servers.manage',
                        'settings.edit',
                        'settings.view',
                        'subjects.edit',
                        'subjects.view',
                        'system.view',
                        'tags.manage',
                        'tags.view',
                        'users.manage',
                        'wishlists.manage',
                        'wishlists.own',
                        'wishlists.view',
                    ],
                ],
            ];

            for (const [role, client, expectedPermissions] of cases) {
                const effective = await client.get<EffectivePermissions>('/api/auth/permissions');
                expect(effective.role).toBe(role);
                expect(
                    Object.entries(effective.permissions)
                        .filter(([, grant]) => grant.granted)
                        .map(([permission]) => permission)
                        .sort(),
                ).toEqual(expectedPermissions);
            }
        });

        await test.step('drive one representative endpoint per gate level as each of the four accounts', async () => {
            const accounts: Array<[string, string]> = [
                ['Viewer', viewerAccessToken],
                ['Requester', requesterAccessToken],
                ['Manager', managerAccessToken],
                ['Admin', adminAccessToken],
            ];
            const gates: Array<[string, string, number[]]> = [
                ['Viewer', '/api/content/stats', [200, 200, 200, 200]],
                ['Requester', '/api/notification-rules', [403, 200, 200, 200]],
                ['Manager', '/api/settings/groups', [403, 403, 200, 200]],
                ['Admin', '/api/users?pageSize=1', [403, 403, 403, 200]],
            ];

            for (const [gate, path, expectedStatuses] of gates) {
                for (let index = 0; index < accounts.length; index += 1) {
                    const [account, accessToken] = accounts[index]!;
                    expect(
                        (await authorizedRequest(accessToken, 'GET', path)).status,
                        `${account} account -> ${gate} endpoint`,
                    ).toBe(expectedStatuses[index]);
                }
            }
        });

        await test.step('the viewer is refused the add and import pages by direct URL', async () => {
            for (const path of ['/library/add', '/library/import']) {
                await viewerPage.goto(path);
                await expect(viewerPage).toHaveURL(`${integrationEnvironment.baseUrl}/forbidden`);
                await expect(viewerPage.locator('[data-page="forbidden"]')).toContainText(
                    'This page requires Manager role or higher.',
                );
            }
        });

        await test.step('the viewer is refused the interactive search page by direct URL', async () => {
            await viewerPage.goto(`/interactive-search/${MISSING_CONTENT_ID}`);
            await expect(viewerPage).toHaveURL(`${integrationEnvironment.baseUrl}/forbidden`);
            await expect(viewerPage.locator('[data-page="forbidden"]')).toContainText(
                'This page requires Requester role or higher.',
            );
        });

        await test.step('the manager may change content but not system settings', async () => {
            expect(
                (
                    await authorizedRequest(managerAccessToken, 'PATCH', `/api/content/${MISSING_CONTENT_ID}`, {
                        monitoring: { value: false },
                    })
                ).status,
            ).toBe(404);
            expect(
                (await authorizedRequest(managerAccessToken, 'GET', '/api/settings/groups/system/monitoring')).status,
            ).toBe(200);
            expect(
                (
                    await authorizedRequest(managerAccessToken, 'PATCH', '/api/settings/groups/system/monitoring', {
                        settings: {},
                    })
                ).status,
            ).toBe(403);
        });

        await test.step('the requester may enqueue a job but not cancel one', async () => {
            adminExecutionId = await adminApi.post<string>('/api/jobs/enqueue', {
                jobType: 'IntegrationHealthCheck',
            });
            await expect(requesterApi.post(`/api/jobs/${adminExecutionId}/cancel`)).rejects.toMatchObject({
                status: 403,
            });
        });

        await test.step("the viewer's API key reaches no further than the viewer", async () => {
            const created = await adminApi.post<ApiKeyCreated>(
                `/api/api-keys?username=${encodeURIComponent(VIEWER_USERNAME)}`,
                { name: 'Viewer authorization probe' },
            );
            expect(created.key.userId).toBe(viewer.id);
            const keyed = new ApiClient({ apiKey: created.plaintextKey });
            expect(await keyed.get<User>('/api/users/my')).toMatchObject({
                id: viewer.id,
                username: VIEWER_USERNAME,
                role: 'Viewer',
            });
            await expect(keyed.get('/api/users?pageSize=1')).rejects.toMatchObject({
                status: 403,
            });
            await adminApi.delete(`/api/api-keys/${created.key.id}`);
        });

        await test.step('the requester sees only their own executions, the administrator sees them all', async () => {
            const requesterJobs = await requesterApi.get<Paged<JobExecution>>('/api/jobs?mine=true&pageSize=100');
            expect(requesterJobs.items.every((execution) => execution.createdByUserId === requester.id)).toBe(true);
            expect(requesterJobs.items.some((execution) => execution.id === adminExecutionId)).toBe(false);

            const allJobs = await adminApi.get<Paged<JobExecution>>('/api/jobs?pageSize=100');
            expect(allJobs.items).toContainEqual(expect.objectContaining({ id: adminExecutionId }));

            await requesterPage.goto('/my-jobs');
            await expect(requesterPage.locator('[data-page="my-jobs"]')).toBeVisible();
            await expect(requesterPage.locator(`[data-execution-id="${adminExecutionId}"]`)).toHaveCount(0);

            await page.goto('/admin/jobs');
            await expect(page.locator('[data-page="jobs"]')).toBeVisible();
            await expect(page.locator(`[data-execution-id="${adminExecutionId}"]`)).toBeVisible();
        });

        await test.step('personal filters stay personal', async () => {
            const personalFilter = await requesterApi.post<ContentFilter>('/api/content-filters', {
                name: 'Requester movie library',
                criteria: {
                    contentHierarchyItem: {
                        and: [
                            {
                                contentType: {
                                    and: [{ operator: 'Equals', value: 'movie' }],
                                },
                            },
                        ],
                    },
                },
            });
            expect(personalFilter.ownerUserId).toBe(requester.id);
            expect(await requesterApi.get<ContentFilter[]>('/api/content-filters/my')).toContainEqual(
                expect.objectContaining({
                    id: personalFilter.id,
                    name: 'Requester movie library',
                }),
            );
            expect(await managerApi.get<ContentFilter[]>('/api/content-filters/my')).not.toContainEqual(
                expect.objectContaining({ id: personalFilter.id }),
            );

            await requesterPage.goto('/filters');
            await requesterPage.locator('[data-component="ownership-toggle"] [data-value="mine"]').click();
            await expect(requesterPage.locator(`[data-filter-id="${personalFilter.id}"]`)).toContainText(
                'Requester movie library',
            );
            await managerPage.goto('/filters');
            await managerPage.locator('[data-component="ownership-toggle"] [data-value="mine"]').click();
            await expect(managerPage.locator(`[data-filter-id="${personalFilter.id}"]`)).toHaveCount(0);

            await requesterApi.delete(`/api/content-filters/${personalFilter.id}`);
            await managerContext.close();
            await requesterContext.close();
            await viewerContext.close();
        });
    });
});

async function createLocalUser(
    page: Page,
    username: string,
    password: string,
    role: 'Viewer' | 'Requester' | 'Manager',
): Promise<void> {
    await page.locator('[data-action="add-user"]').first().click();
    const form = page.locator('[data-component="user-form"]');
    await expect(form).toBeVisible();
    await form.locator('[data-field="username"]').fill(username);
    await form.locator('[data-field="password"]').fill(password);
    await form.locator('[data-field="role"]').click();
    await page.getByRole('option', { name: role, exact: true }).click();
    await form.locator('[data-action="create"]').click();
    await expect(form).toBeHidden();
}

function authorizedRequest(
    accessToken: string,
    method: 'GET' | 'PATCH',
    path: string,
    body?: unknown,
): Promise<Response> {
    return fetch(`${integrationEnvironment.baseUrl}${path}`, {
        method,
        headers: {
            Accept: 'application/json',
            Authorization: `Bearer ${accessToken}`,
            ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
    });
}
