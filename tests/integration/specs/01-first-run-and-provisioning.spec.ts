import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { ADMIN_PASSWORD, ADMIN_USERNAME, ApiClient, loginAdmin } from '../helpers/api';
import { createCheckpoint, saveCliApiKey } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { integrationEnvironment } from '../helpers/environment';
import { restartHost } from '../helpers/host';
import { mocks } from '../helpers/mocks';

interface NamedResource {
    id: string;
    name: string;
    providerType?: string;
    settings?: Record<string, string>;
}

interface ApiKeyCreated {
    key: { id: string; name: string; keyPrefix: string };
    plaintextKey: string;
}

interface SystemInfo {
    nodeId: string;
    startedAt: string;
    version: string;
}

interface SetupStatus {
    isRequired: boolean;
    isComplete: boolean;
    requiresAdminClaim: boolean;
}

interface SettingsGroup {
    name: string;
    extensionId: string | null;
    settings: Array<{ key: string; value: string }>;
}

interface IndexerConnection {
    id: string;
    name: string;
    status: string;
}

interface QualityTierList {
    id: string;
    name: string;
}

test.describe.serial('Journey 1: day one: unbox the instance', () => {
    test('completes first run, provisions the instance and creates the configured baseline', async ({ page }) => {
        test.setTimeout(420_000);

        let api: ApiClient;
        let configuredMetadataProviderType = '';

        await test.step('prove the instance is clean and the deployment is initialized', async () => {
            const status = await new ApiClient().get<SetupStatus>('/api/system/setup/status');
            expect(status).toEqual({ isRequired: true, isComplete: false, requiresAdminClaim: true });
            const database = await new ApiClient().get<{ initialized: boolean; provider: string }>(
                '/api/system/setup/db-status',
            );
            expect(database).toEqual({ initialized: true, provider: 'SQLite' });
        });

        await test.step('claim the administrator account and reach the metadata step', async () => {
            await page.goto('/setup');
            await expect(page.locator('[data-page="setup"]')).toHaveAttribute('data-current-step', 'admin');

            await page.locator('[data-field="username"]').fill(ADMIN_USERNAME);
            await page.locator('[data-field="password"]').fill(ADMIN_PASSWORD);
            await page.locator('[data-field="confirm-password"]').fill(ADMIN_PASSWORD);
            await page.locator('[data-action="create-admin"]').click();
            await expect(page.locator('[data-page="setup"]')).toHaveAttribute('data-current-step', 'metadata');
            await expect(page.locator('[data-step="metadata"]')).toBeVisible();
        });

        await test.step('restart the host mid-wizard and resume at the same step', async () => {
            api = (await loginAdmin(ADMIN_USERNAME, ADMIN_PASSWORD)).api;
            api = await restartHost(api);

            expect(await new ApiClient().get<SetupStatus>('/api/system/setup/status')).toEqual({
                isRequired: true,
                isComplete: false,
                requiresAdminClaim: false,
            });
            await page.goto('/setup');
            await expect(page.locator('[data-page="setup"]')).toHaveAttribute('data-current-step', 'metadata');
            await expect(page.locator('[data-step="metadata"]')).toBeVisible();
        });

        await test.step('choose a metadata provider instead of skipping it', async () => {
            await mocks.hierarchyPrimary.useScenario('bootstrap-metadata');
            await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
                providerType: mocks.hierarchyPrimary.slug,
                source: 'tmdb:bootstrap-preview',
                title: 'Bootstrap Metadata Preview',
                overview: 'Metadata returned by the local integration shim.',
                language: 'en',
                externalIds: [{ source: 'tmdb', value: 'bootstrap-preview', ordinal: 0 }],
            });
            await mocks.hierarchyPrimary.write('expandChildrenAsync.json', []);
            await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', []);

            const metadata = page.locator('[data-group="Primary hierarchy shim"]');
            await expect(metadata).toBeVisible();
            await metadata.getByLabel('providerType').fill(mocks.hierarchyPrimary.slug);
            await metadata.locator('[data-action="save-metadata-group"]').click();
            await expect(page.getByText('Saved Primary hierarchy shim', { exact: true })).toBeVisible();

            const group = (await api.get<SettingsGroup[]>('/api/settings/groups')).find(
                (item) => item.name === 'Primary hierarchy shim',
            );
            expect(group).toBeDefined();
            configuredMetadataProviderType = group!.settings.find((setting) => setting.key === 'providerType')?.value ?? '';
            expect(configuredMetadataProviderType).toBe(mocks.hierarchyPrimary.slug);
            await page.locator('[data-action="continue-metadata"]').click();
            await expect(page.locator('[data-step="indexer"]')).toBeVisible();
        });

        await test.step('skip the steps that are genuinely optional', async () => {
            await page.locator('[data-action="open-indexer-form"]').click();
            const indexer = page.getByRole('dialog', { name: 'Add Indexer' });
            await indexer.getByLabel('Name').fill('Local Indexer');
            await indexer.getByRole('combobox', { name: 'Integration' }).click();
            await page.getByRole('option', { name: mocks.indexer.slug }).click();
            await indexer.getByLabel('API URL').fill('https://indexer.invalid/local');
            await indexer.getByLabel('API Key').fill('baseline-indexer-secret');
            await indexer.locator('[data-action="save"]').click();
            await expect(indexer).toBeHidden();
            await page.locator('[data-action="continue-indexer"]').click();

            await page.locator('[data-action="open-download-client-form"]').click();
            const downloadClient = page.getByRole('dialog', { name: 'Add Download Client' });
            await downloadClient.getByLabel('Name').fill('Local Download Client');
            await downloadClient.getByRole('combobox', { name: 'Integration' }).click();
            await page.getByRole('option', { name: mocks.downloadClient.slug }).click();
            await downloadClient.getByLabel('Host').fill('download.invalid');
            await downloadClient.getByLabel('Port').fill('8080');
            await downloadClient.getByLabel('API Key').fill('baseline-download-secret');
            await downloadClient.locator('[data-action="save"]').click();
            await expect(downloadClient).toBeHidden();
            await page.locator('[data-action="continue-download-client"]').click();

            await mkdir(resolve(integrationEnvironment.paths.library, 'movies'), { recursive: true });
            await page.locator('[data-action="open-root-folder-form"]').click();
            const rootFolder = page.getByRole('dialog', { name: 'Add Root Folder' });
            await rootFolder.locator('[data-field="name"]').fill('Movies');
            await rootFolder.getByLabel('Path').fill('/library/movies');
            await rootFolder.getByRole('combobox', { name: 'Content Type' }).click();
            await page.getByRole('option', { name: 'Movie', exact: true }).click();
            await rootFolder.getByRole('combobox', { name: 'Default Quality Tiers' }).click();
            await page.getByRole('option', { name: 'Video - HD - video/*', exact: true }).click();
            await rootFolder.locator('[data-action="save"]').click();
            await expect(rootFolder).toBeHidden();
            await page.locator('[data-action="continue-root-folder"]').click();

            await expect(page.locator('[data-step="server"]')).toBeVisible();
            await page.locator('[data-action="skip-media-server"]').click();
        });

        await test.step('read the summary and see exactly what was configured and what was not', async () => {
            const summary = page.locator('[data-component="setup-summary"]');
            await expect(summary).toBeVisible();
            const metadataSummary = summary.getByText('Metadata-provider configs').locator('..');
            await expect(metadataSummary.locator('strong')).toHaveText('1');
            await expect(metadataSummary).toContainText('Primary hierarchy shim');
            await expect(summary.getByText('Indexers').locator('..').locator('strong')).toHaveText('1');
            await expect(summary.getByText('Download clients').locator('..').locator('strong')).toHaveText('1');
            const mediaServerSummary = summary.getByText('Media servers').locator('..');
            await expect(mediaServerSummary.locator('strong')).toHaveText('0');
            await expect(mediaServerSummary).toContainText('Skipped');
            await expect(summary.getByText('Root folders').locator('..').locator('strong')).toHaveText('1');
        });

        await test.step('finish the wizard and land on the dashboard', async () => {
            await page.locator('[data-action="finish-setup"]').click();
            await expect(page).toHaveURL(/\/$/);
            expect(await new ApiClient().get<SetupStatus>('/api/system/setup/status')).toEqual({
                isRequired: false,
                isComplete: true,
                requiresAdminClaim: false,
            });
        });

        await test.step('restart through the supported workflow and retain completed setup', async () => {
            api = await restartHost(api, async () => {
                await page.goto('/admin/system');
                await page.getByRole('button', { name: 'Restart service' }).click();
                await page
                    .getByRole('dialog', { name: 'Restart Magpie?' })
                    .getByRole('button', { name: 'Restart' })
                    .click();
            });
            await expect(page.getByRole('heading', { name: 'Magpie is back online' })).toBeVisible({ timeout: 120_000 });
            await page.locator('[data-action="dismiss"]').click();

            expect(await api.get<SetupStatus>('/api/system/setup/status')).toEqual({
                isRequired: false,
                isComplete: true,
                requiresAdminClaim: false,
            });
        });

        await test.step('refresh the session, sign out, and sign back in', async () => {
            await page.waitForLoadState('networkidle');
            const initialToken = await page.evaluate(() => window.localStorage.getItem('magpie_auth_token'));
            const refresh = await page.context().request.post(`${integrationEnvironment.baseUrl}/api/auth/refresh`);
            expect(refresh.status()).toBe(200);
            const refreshed = (await refresh.json()) as { accessToken: string };
            expect(refreshed.accessToken).toBeTruthy();
            expect(refreshed.accessToken).not.toBe(initialToken);
            await page.evaluate(
                (accessToken) => window.localStorage.setItem('magpie_auth_token', accessToken),
                refreshed.accessToken,
            );
            await page.reload();

            await page.getByRole('button', { name: `User menu for ${ADMIN_USERNAME}` }).click();
            await page.getByRole('menuitem', { name: 'Log out' }).click();
            await expect(page).toHaveURL(/\/login$/);
            await page.getByLabel('Username').fill(ADMIN_USERNAME);
            await page.getByLabel('Password').fill(ADMIN_PASSWORD);
            await page.getByRole('button', { name: 'Sign in' }).click();
            await expect(page).toHaveURL(/\/admin\/system$/);
            await expect(page.locator('[data-page="system-status"]')).toBeVisible();
        });

        await test.step('create the suite API key and prove the authorization boundary', async () => {
            const created = await api.post<ApiKeyCreated>('/api/api-keys', { name: 'Integration journey CLI' });
            expect(created.key.name).toBe('Integration journey CLI');
            expect(created.key.keyPrefix).toBeTruthy();
            expect(created.plaintextKey).toBeTruthy();
            await saveCliApiKey(created.plaintextKey);

            const keyed = new ApiClient({ apiKey: created.plaintextKey });
            const system = await keyed.get<SystemInfo>('/api/system');
            expect(system.nodeId).toBeTruthy();
            expect(system.version).toBeTruthy();
            await expect(new ApiClient().get('/api/system')).rejects.toMatchObject({ status: 401 });
        });

        await test.step('provision root folders, indexers, a notification rule and a second user across UI and CLI', async () => {
            const uiIndexers = await cli.json<NamedResource[]>(['indexers', 'list']);
            expect(uiIndexers).toContainEqual(expect.objectContaining({ name: 'Local Indexer' }));

            const lists = await cli.json<QualityTierList[]>(['quality-tier-lists', 'list']);
            expect(lists).toContainEqual(expect.objectContaining({ name: 'Video - HD' }));
            expect(lists).toContainEqual(expect.objectContaining({ name: 'Audio - Balanced' }));
            expect(lists).toContainEqual(expect.objectContaining({ name: 'Audio - Spoken Word' }));
            expect(lists).toContainEqual(expect.objectContaining({ name: 'E-book - Retail and Reflowable' }));
            const videoTierListId = lists.find((list) => list.name === 'Video - HD')!.id;
            const audioTierListId = lists.find((list) => list.name === 'Audio - Balanced')!.id;
            const spokenWordTierListId = lists.find((list) => list.name === 'Audio - Spoken Word')!.id;
            const ebookTierListId = lists.find((list) => list.name === 'E-book - Retail and Reflowable')!.id;
            const roots = [
                {
                    name: 'TV',
                    directory: 'tv',
                    contentType: 'tv',
                    defaults: { show: { qualityTierListId: videoTierListId } },
                },
                {
                    name: 'Music',
                    directory: 'music',
                    contentType: 'music',
                    defaults: { artist: { qualityTierListId: audioTierListId } },
                },
                {
                    name: 'Books',
                    directory: 'books',
                    contentType: 'book',
                    defaults: {
                        ebook: { qualityTierListId: ebookTierListId },
                        audiobook: { qualityTierListId: spokenWordTierListId },
                    },
                },
            ];
            for (const root of roots) {
                await mkdir(resolve(integrationEnvironment.paths.library, root.directory), { recursive: true });
                await cli.json<NamedResource>([
                    'root-folders',
                    'create',
                    '--json',
                    JSON.stringify({
                        name: root.name,
                        path: `/library/${root.directory}`,
                        contentType: root.contentType,
                        isDefault: true,
                        defaultMonitoring: 'Monitored',
                        defaults: root.defaults,
                    }),
                ]);
            }

            const movieRoot = (await cli.json<NamedResource[]>(['root-folders', 'list'])).find(
                (root) => root.name === 'Movies',
            );
            expect(movieRoot).toBeDefined();
            await cli.run([
                'root-folders',
                'patch',
                movieRoot!.id,
                '--json',
                JSON.stringify({
                    isDefault: { value: true },
                    defaultMonitoring: { value: 'Monitored' },
                    defaults: { value: { movie: { qualityTierListId: videoTierListId } } },
                }),
            ]);

            await page.goto('/admin/media-servers');
            await page.getByRole('button', { name: 'Add Server', exact: true }).click();
            const mediaServer = page.getByRole('dialog', { name: 'Add Media Server' });
            await mediaServer.getByLabel('Name').fill('Local Media Server');
            await mediaServer.getByRole('combobox', { name: 'Integration' }).click();
            await page.getByRole('option', { name: mocks.mediaServer.slug }).click();
            await mediaServer.getByLabel('Host').fill('media.invalid');
            await mediaServer.getByLabel('Port').fill('8096');
            await mediaServer.getByLabel('API Key').fill('baseline-media-secret');
            await mediaServer.locator('[data-action="save"]').click();
            await expect(mediaServer).toBeHidden();

            await cli.json<NamedResource>([
                'notification-rules',
                'create',
                '--json',
                JSON.stringify({
                    name: 'Journey notifications',
                    providerType: mocks.notification.slug,
                    enabled: true,
                    eventFilter: { eventName: { operator: 'Equals', value: 'download.imported' } },
                    settings: { target: 'mock://journeys', template: 'default' },
                }),
            ]);
            await api.post('/api/users', {
                username: 'journey-requester',
                password: 'journey-requester-pa55!',
                role: 'Requester',
                enabled: true,
            });
            await api.post('/api/event-triggers', {
                jobType: 'DownloadCleanup',
                scope: 'Administrative',
                name: 'Journey download import trigger',
                eventFilter: { eventName: { operator: 'Equals', value: 'download.imported' } },
                enabled: false,
                parameters: {},
            });
            expect(await api.get<NamedResource[]>('/api/schedule-triggers')).toContainEqual(
                expect.objectContaining({ name: 'Integration health check schedule' }),
            );

            const localIndexer = uiIndexers.find((indexer) => indexer.name === 'Local Indexer')!;
            const localConnection = (await api.get<IndexerConnection[]>(`/api/indexers/${localIndexer.id}/connections`))[0];
            expect(localConnection).toBeDefined();
            await mocks.indexer.armTestConnection({
                success: false,
                message: 'temporary indexer failure',
                failureKind: 'Transient',
            });
            await api.post(`/api/indexers/${localIndexer.id}/connections/${localConnection!.id}/test`);

            const unhealthy = await api.post<{ id: string }>(`/api/indexers/${localIndexer.id}/connections`, {
                name: 'Unhealthy indexer connection',
                enabled: true,
                settings: { apiUrl: 'https://unhealthy.indexer.invalid', apiKey: 'baseline-indexer-secret' },
            });
            await mocks.indexer.armTestConnection({
                success: false,
                message: 'fatal indexer failure',
                failureKind: 'Fatal',
            });
            await api.post(`/api/indexers/${localIndexer.id}/connections/${unhealthy.id}/test`);
            await mocks.indexer.armTestConnection({ success: true, message: 'default' });

            const connections = await api.get<IndexerConnection[]>(`/api/indexers/${localIndexer.id}/connections`);
            expect(connections).toHaveLength(2);
            expect(connections).toContainEqual(expect.objectContaining({ id: localConnection!.id, status: 'Degraded' }));
            expect(connections).toContainEqual(
                expect.objectContaining({ name: 'Unhealthy indexer connection', status: 'Unhealthy' }),
            );
            expect(
                await api.post(`/api/indexers/${localIndexer.id}/test?connectionId=${localConnection!.id}`),
            ).toMatchObject({ success: true, message: 'default' });

            await page.goto('/admin/root-folders');
            await expect(page.getByTitle('Movies', { exact: true })).toBeVisible();
            await expect(page.getByTitle('TV', { exact: true })).toBeVisible();
            await expect(page.getByTitle('Music', { exact: true })).toBeVisible();
            await expect(page.getByTitle('Books', { exact: true })).toBeVisible();
            await page.goto('/admin/notifications');
            await expect(page.getByRole('row').filter({ hasText: 'Journey notifications' })).toBeVisible();
        });

        await test.step('the provider configured in the wizard takes part in the first search', async () => {
            expect(configuredMetadataProviderType).toBe(mocks.hierarchyPrimary.slug);

            await mocks.hierarchyPrimary.write('advancedSearchAsync.json', [
                {
                    providerType: mocks.hierarchyPrimary.slug,
                    externalIdSource: 'tmdb',
                    externalId: 'bootstrap-preview',
                    contentType: 'movie',
                    level: 'movie',
                    title: 'Bootstrap Metadata Preview',
                    overview: 'Metadata returned by the local integration shim.',
                    language: 'en',
                },
            ]);
            await mocks.hierarchyPrimary.clearCalls();

            const query = encodeURIComponent('title contains "Bootstrap Metadata Preview"');
            const result = await api.get<{
                matches: Array<{ candidate: { providerType: string; title?: string } }>;
            }>(`/api/content-search?query=${query}&contentType=movie`);

            expect(result.matches).toContainEqual(
                expect.objectContaining({
                    candidate: expect.objectContaining({
                        providerType: mocks.hierarchyPrimary.slug,
                        title: 'Bootstrap Metadata Preview',
                    }),
                }),
            );
            expect(await mocks.hierarchyPrimary.calls('advancedSearchAsync')).not.toHaveLength(0);
        });

        await test.step('prove configured resources are usable and no secret is disclosed', async () => {
            const indexers = await api.get<NamedResource[]>('/api/indexers');
            const allIndexerConnections: IndexerConnection[] = [];
            for (const indexer of indexers) {
                const connections = await api.get<IndexerConnection[]>(`/api/indexers/${indexer.id}/connections`);
                allIndexerConnections.push(...connections);
                for (const connection of connections) {
                    expect(await api.post(`/api/indexers/${indexer.id}/connections/${connection.id}/test`)).toMatchObject({
                        success: true,
                        message: 'default',
                    });
                }
            }
            expect(
                (await api.get<IndexerConnection[]>(`/api/indexers/${indexers[0]!.id}/connections`)).map(
                    (connection) => connection.status,
                ),
            ).toEqual(['Healthy', 'Healthy']);

            const tests = [
                ['/api/download-clients', '/test', 'default'],
                ['/api/media-servers', '/test', 'default'],
                ['/api/notification-rules', '/test', 'Test notification sent to the provider.'],
            ] as const;
            for (const [listPath, action, expected] of tests) {
                const resources = await api.get<NamedResource[]>(listPath);
                expect(resources.length).toBeGreaterThan(0);
                for (const resource of resources) {
                    expect(await api.post(`${listPath}/${resource.id}${action}`)).toMatchObject({
                        success: true,
                        message: expected,
                    });
                }
            }

            const secrets = ['baseline-indexer-secret', 'baseline-download-secret', 'baseline-media-secret'];
            const serialized = JSON.stringify({
                settings: await api.get('/api/settings/groups'),
                indexers,
                indexerConnections: allIndexerConnections,
                downloadClients: await api.get('/api/download-clients'),
                mediaServers: await api.get('/api/media-servers'),
                notifications: await api.get('/api/notification-rules'),
            });
            for (const secret of secrets) {
                expect(serialized).not.toContain(secret);
            }

            for (const route of [
                '/admin/indexers',
                '/admin/download-clients',
                '/admin/media-servers',
                '/admin/notifications',
                '/admin/extensions',
            ]) {
                await page.goto(route);
                const rendered = await page.locator('body').textContent();
                for (const secret of secrets) {
                    expect(rendered).not.toContain(secret);
                }
            }
        });

        await test.step('create and verify the configured baseline checkpoint', async () => {
            const backup = await createCheckpoint(api, 'configured-baseline', [
                'library/movies',
                'library/tv',
                'library/music',
                'library/books',
                'test-fixtures/mock-indexer/_active',
            ]);
            expect(backup.name).toBe('configured-baseline');
            expect(await cli.json<Array<{ id: string; name: string }>>(['database', 'backups'])).toContainEqual(
                expect.objectContaining({ id: backup.id, name: 'configured-baseline' }),
            );
        });
    });
});
