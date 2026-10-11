import { mkdir, rm, stat, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test, type Page } from '@playwright/test';

import { ApiClient, loginAdmin } from '../helpers/api';
import { createCheckpoint, restoreCheckpoint } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { integrationEnvironment } from '../helpers/environment';
import { waitForJob, type JobExecution } from '../helpers/jobs';
import { mocks } from '../helpers/mocks';
import { restartHost } from '../helpers/host';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Named {
    id: string;
    name: string;
}

interface Provider extends Named {
    enabled: boolean;
    providerType: string;
    status: string;
    lastChecked?: string | null;
    settings: Record<string, string>;
}

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface ExtensionInstallResult {
    extension: {
        id: string;
        name: string;
        version: string;
        enabled: boolean;
        installed: boolean;
    };
    restartRequired: boolean;
}

const REQUESTER_USERNAME = 'journey-requester';
const REQUESTER_PASSWORD = 'journey-requester-pa55!';

test.describe.serial('Journey 16: administer and restore the application', () => {
    test('covers administration, authorization, diagnostics, secrets and runtime restoration', async ({ page, request }) => {
        test.setTimeout(900_000);

        let api = await restoreCheckpoint('configured-baseline', async (restored) => {
            const users = await restored.get<Paged<{ username: string; role: string }>>('/api/users?pageSize=100');
            expect(users.items).toContainEqual(expect.objectContaining({ username: 'magpie-admin', role: 'Admin' }));
            expect(users.items).toContainEqual(
                expect.objectContaining({ username: REQUESTER_USERNAME, role: 'Requester' }),
            );
            expect((await restored.get<Named[]>('/api/root-folders')).map((root) => root.name).sort()).toEqual([
                'Books',
                'Movies',
                'Music',
                'TV',
            ]);
            expect(await restored.get<Named[]>('/api/indexers')).toContainEqual(
                expect.objectContaining({ name: 'Local Indexer' }),
            );
            expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
        });

        await loginUi(page);

        await test.step('read every core administrator route', async () => {
            const routes: Array<[string, string]> = [
                ['/admin/general', 'Advanced'],
                ['/admin/notifications', 'Notifications'],
                ['/admin/users', 'Users'],
                ['/admin/indexers', 'Indexers'],
                ['/admin/download-clients', 'Download Clients'],
                ['/admin/remote-path-mappings', 'Remote Path Mappings'],
                ['/admin/media-servers', 'Media Servers'],
                ['/admin/root-folders', 'Root Folders'],
                ['/admin/file-types', 'File Types'],
                ['/admin/content-types', 'Content Types'],
                ['/admin/quality-tiers', 'Quality Tiers'],
                ['/admin/jobs', 'System Jobs'],
                ['/admin/extensions', 'Extensions'],
                ['/admin/database', 'Database'],
                ['/admin/system', 'System Status'],
            ];
            for (const [path, heading] of routes) {
                await page.goto(path);
                await expect(page.getByRole('heading', { name: heading, exact: true }).first()).toBeVisible();
            }
        });

        await test.step('cross-read general settings and reject invalid input without a partial save', async () => {
            await page.goto('/admin/general');
            await page.getByLabel('Instance Name').fill('Journey administration');
            await page.getByLabel('Base URL').fill('https://admin.journey.invalid');
            await page.locator('[data-action="save-settings"]').click();
            await expect(page.getByText(/ updated the general settings\.$/)).toBeVisible();

            expect(settingsRecord(await cli.json<SettingsGroup>(['settings', 'get-system', 'general']))).toMatchObject({
                instanceName: 'Journey administration',
                baseUrl: 'https://admin.journey.invalid',
            });

            await cli.run([
                'settings',
                'patch-system',
                'general',
                '--json',
                JSON.stringify({ settings: { update: { instanceName: 'Journey administration from CLI' } } }),
            ]);
            await page.reload();
            await expect(page.getByLabel('Instance Name')).toHaveValue('Journey administration from CLI');

            expect(
                (await cli.json<SettingsGroup[]>(['settings', 'list'])).some((group) => group.name === 'job-retry'),
            ).toBe(false);
            await page.getByLabel('Base delay (seconds)').fill('0');
            await page.locator('[data-action="save-job-retry"]').click();
            await expect(page.getByText('Base delay must be at least 1.', { exact: true })).toBeVisible();
            expect(
                (await cli.json<SettingsGroup[]>(['settings', 'list'])).some((group) => group.name === 'job-retry'),
            ).toBe(false);
        });

        await test.step('create, update, test, toggle and delete a notification rule', async () => {
            await mocks.notification.clearCalls();
            const rule = await api.post<Provider>('/api/notification-rules', {
                name: 'Administration notification',
                providerType: mocks.notification.slug,
                enabled: true,
                eventFilter: { eventName: { operator: 'Equals', value: 'apiKey.created' } },
                settings: { target: 'mock://administration', template: 'admin' },
            });
            await api.patch(`/api/notification-rules/${rule.id}`, {
                name: { value: 'Administration notification updated' },
                enabled: { value: false },
            });
            expect(await api.get<Provider>(`/api/notification-rules/${rule.id}`)).toMatchObject({
                name: 'Administration notification updated',
                enabled: false,
            });
            await api.patch(`/api/notification-rules/${rule.id}`, { enabled: { value: true } });

            await page.goto('/admin/notifications');
            const row = page.getByRole('row').filter({ hasText: 'Administration notification updated' });
            await row.getByRole('button', { name: 'Test Administration notification updated' }).click();
            await mocks.notification.waitForCalls(1, { method: 'send' });
            await expect(page.getByText('Test Sent', { exact: true })).toBeVisible();

            await api.patch(`/api/notification-rules/${rule.id}`, { enabled: { value: false } });
            expect((await api.get<Provider>(`/api/notification-rules/${rule.id}`)).enabled).toBe(false);
            await api.delete(`/api/notification-rules/${rule.id}`);
            await expect(api.get(`/api/notification-rules/${rule.id}`)).rejects.toMatchObject({ status: 404 });
        });

        await test.step('administer a local user, its password, lockout and API key', async () => {
            const user = await api.post<{ id: string; username: string; role: string; enabled: boolean }>('/api/users', {
                username: 'administration-user',
                password: 'administration-user-pa55!',
                role: 'Viewer',
                enabled: true,
            });
            await api.patch(`/api/users/${user.id}`, {
                role: { value: 'Manager' },
                enabled: { value: false },
            });
            expect(await api.get(`/api/users/${user.id}`)).toMatchObject({ role: 'Manager', enabled: false });
            await api.patch(`/api/users/${user.id}`, { enabled: { value: true } });

            await api.post(`/api/users/${user.id}/password`, { newPassword: 'administration-user-new-pa55!' });
            await expect(loginAdmin('administration-user', 'administration-user-pa55!')).rejects.toMatchObject({
                status: 401,
            });
            const userLogin = await request.post('/api/auth/login', {
                data: { username: 'administration-user', password: 'administration-user-new-pa55!' },
            });
            expect(userLogin.status()).toBe(200);
            const userSession = (await userLogin.json()) as { accessToken: string };
            const userLogout = await request.post('/api/auth/logout', {
                headers: { Authorization: `Bearer ${userSession.accessToken}` },
            });
            expect(userLogout.status()).toBe(204);

            const key = await api.post<{ key: { id: string; userId: string }; plaintextKey: string }>(
                '/api/api-keys?username=administration-user',
                { name: 'Administration user key' },
            );
            expect(key.key.userId).toBe(user.id);
            expect(await api.get<Array<{ id: string }>>(`/api/users/${user.id}/api-keys`)).toContainEqual(
                expect.objectContaining({ id: key.key.id }),
            );
            await api.delete(`/api/api-keys/${key.key.id}`);

            for (let attempt = 0; attempt < 3; attempt += 1) {
                await new ApiClient()
                    .post('/api/auth/login', { username: user.username, password: 'wrong-password' })
                    .catch(() => undefined);
            }
            expect((await api.get<{ failedLoginAttempts: number }>(`/api/users/${user.id}`)).failedLoginAttempts).toBe(
                3,
            );
            await api.post(`/api/users/${user.id}/unlock`);
            expect(await api.get(`/api/users/${user.id}`)).toMatchObject({ failedLoginAttempts: 0, lockedUntil: null });
            await api.delete(`/api/users/${user.id}`);
        });

        await test.step('administer indexer connections and persist their health', async () => {
            const indexer = (await api.get<Named[]>('/api/indexers')).find((item) => item.name === 'Local Indexer')!;
            await mocks.indexer.armTestConnection({ success: true, message: 'administration indexer healthy' });
            const connection = await api.post<Provider>(`/api/indexers/${indexer.id}/connections`, {
                name: 'Administration indexer connection',
                enabled: true,
                settings: { apiUrl: 'https://administration.indexer.invalid', apiKey: 'admin-indexer-secret' },
            });
            const result = await api.post<{ success: boolean; message: string }>(
                `/api/indexers/${indexer.id}/connections/${connection.id}/test`,
            );
            expect(result).toEqual({
                success: true,
                message: 'administration indexer healthy',
                failureKind: 'None',
            });
            const healthy = await api.get<Provider[]>(`/api/indexers/${indexer.id}/connections`);
            expect(healthy).toContainEqual(
                expect.objectContaining({
                    id: connection.id,
                    status: 'Healthy',
                    lastChecked: expect.any(String),
                }),
            );
            await api.patch(`/api/indexers/${indexer.id}/connections/${connection.id}`, {
                name: { value: 'Administration indexer connection updated' },
                enabled: { value: false },
            });
            await api.patch(`/api/indexers/${indexer.id}/connections/${connection.id}`, {
                enabled: { value: true },
            });
            await api.delete(`/api/indexers/${indexer.id}/connections/${connection.id}`);
        });

        await test.step('create, update, test and delete an indexer feed', async () => {
            await mocks.indexerFeed.clearCalls();
            const feed = await api.post<Provider>('/api/indexer-feeds', {
                name: 'Administration feed',
                description: 'local deterministic feed',
                providerType: mocks.indexerFeed.slug,
                enabled: true,
                settings: { endpoint: 'local://feed', apiKey: 'feed-secret' },
            });
            const tested = await api.post<{ success: boolean; message: string }>(`/api/indexer-feeds/${feed.id}/test`);
            expect(tested).toMatchObject({ success: true, message: 'Connection succeeded.' });
            expect(await mocks.indexerFeed.calls('TestConnectionAsync')).toHaveLength(1);
            await api.patch(`/api/indexer-feeds/${feed.id}`, {
                name: { value: 'Administration feed updated' },
                enabled: { value: false },
            });
            expect(await api.get<Provider[]>('/api/indexer-feeds')).toContainEqual(
                expect.objectContaining({ id: feed.id, name: 'Administration feed updated', enabled: false }),
            );
            await api.patch(`/api/indexer-feeds/${feed.id}`, { enabled: { value: true } });
            await api.delete(`/api/indexer-feeds/${feed.id}`);
        });

        await test.step('administer download client, mapping, media server and root folder connections', async () => {
            await mocks.downloadClient.armTestConnection({ success: true, message: 'administration download healthy' });
            const download = await api.post<Provider>('/api/download-clients', {
                name: 'Administration download client',
                providerType: mocks.downloadClient.slug,
                enabled: true,
                settings: { host: 'administration-download', port: '8080', apiKey: 'download-secret' },
            });
            expect(await api.post(`/api/download-clients/${download.id}/test`)).toMatchObject({
                success: true,
                message: 'administration download healthy',
            });
            expect(await api.get(`/api/download-clients/${download.id}`)).toMatchObject({
                status: 'Healthy',
                lastChecked: expect.any(String),
            });
            await api.patch(`/api/download-clients/${download.id}`, {
                name: { value: 'Administration download client updated' },
                enabled: { value: false },
            });
            await api.patch(`/api/download-clients/${download.id}`, { enabled: { value: true } });

            expect(await api.post('/api/remote-path-mappings/test', { localPath: '/library/movies' })).toMatchObject({
                success: true,
            });
            const mapping = await api.post<Named & { host: string; remotePath: string; localPath: string }>(
                '/api/remote-path-mappings',
                {
                    host: 'administration-download',
                    remotePath: '/remote/downloads',
                    localPath: '/library/movies',
                },
            );
            await api.patch(`/api/remote-path-mappings/${mapping.id}`, { remotePath: { value: '/remote/complete' } });
            expect(await api.get(`/api/remote-path-mappings/${mapping.id}`)).toMatchObject({
                host: 'administration-download',
                remotePath: '/remote/complete',
                localPath: '/library/movies',
            });

            await mocks.mediaServer.armTestConnection({ success: true, message: 'administration media healthy' });
            const media = await api.post<Provider>('/api/media-servers', {
                name: 'Administration media server',
                providerType: mocks.mediaServer.slug,
                enabled: true,
                contentTypes: ['movie'],
                settings: { host: 'administration-media', port: '8096', apiKey: 'media-secret' },
            });
            expect(await api.post(`/api/media-servers/${media.id}/test`)).toMatchObject({
                success: true,
                message: 'administration media healthy',
            });
            expect(await api.get(`/api/media-servers/${media.id}`)).toMatchObject({
                status: 'Healthy',
                lastChecked: expect.any(String),
            });
            await api.patch(`/api/media-servers/${media.id}`, {
                name: { value: 'Administration media server updated' },
                enabled: { value: false },
            });
            await api.patch(`/api/media-servers/${media.id}`, { enabled: { value: true } });

            const qualityList = (await api.get<Named[]>('/api/quality-tier-lists')).find(
                (list) => list.name === 'Video - HD',
            )!;
            await mkdir(resolve(integrationEnvironment.paths.library, 'administration'), { recursive: true });
            const root = await api.post<Named>('/api/root-folders', {
                name: 'Administration root',
                path: '/library/administration',
                contentType: 'movie',
                defaults: { movie: { qualityTierListId: qualityList.id } },
            });
            await api.patch(`/api/root-folders/${root.id}`, { name: { value: 'Administration root updated' } });
            expect(await api.get(`/api/root-folders/${root.id}`)).toMatchObject({
                name: 'Administration root updated',
            });

            await api.delete(`/api/root-folders/${root.id}`);
            await api.delete(`/api/media-servers/${media.id}`);
            await api.delete(`/api/remote-path-mappings/${mapping.id}`);
            await api.delete(`/api/download-clients/${download.id}`);
        });

        await test.step('validate a root-folder form without persisting a partial record', async () => {
            const before = (await api.get<Named[]>('/api/root-folders')).length;
            await page.goto('/admin/root-folders');
            await page.getByRole('button', { name: 'Add Folder', exact: true }).click();
            const form = page.getByRole('dialog', { name: 'Add Root Folder' });
            await form.locator('[data-action="save"]').click();
            await expect(form.getByText('Name is required', { exact: true })).toBeVisible();
            await expect(form.getByText('Path is required', { exact: true })).toBeVisible();
            await expect(form.getByText('Content type is required', { exact: true })).toBeVisible();
            expect((await api.get<Named[]>('/api/root-folders')).length).toBe(before);
            await form.locator('[data-action="cancel"]').click();
        });

        await test.step('read and update file policy, content hierarchy and quality ranking', async () => {
            const beforeMappings = await api.get<Array<{ extension: string; mimeType: string }>>(
                '/api/system/file-types/mime',
            );
            expect(beforeMappings.length).toBeGreaterThan(0);
            const extension = 'journeyadmin';
            expect(
                await api.put(`/api/system/file-types/mime/${extension}`, {
                    mimeType: 'application/x-journey-admin',
                }),
            ).toMatchObject({ extension: `.${extension}`, mimeType: 'application/x-journey-admin' });
            expect(
                await api.put(`/api/system/file-types/mime/${extension}`, {
                    mimeType: 'application/x-journey-admin-updated',
                }),
            ).toMatchObject({ mimeType: 'application/x-journey-admin-updated' });
            await api.delete(`/api/system/file-types/mime/${extension}`);

            const contentTypes = await api.get<Array<{ slug: string; levelDefinitions: Array<{ slug: string }> }>>(
                '/api/content-types',
            );
            expect(contentTypes.find((type) => type.slug === 'tv')?.levelDefinitions.map((level) => level.slug)).toEqual(
                ['show', 'season', 'episode'],
            );

            const firstTier = await api.post<Named>('/api/quality-tiers', {
                name: 'Administration tier one',
                criteria: { and: [] },
            });
            const secondTier = await api.post<Named>('/api/quality-tiers', {
                name: 'Administration tier two',
                criteria: { and: [] },
            });
            await api.patch(`/api/quality-tiers/${firstTier.id}`, {
                name: { value: 'Administration tier one updated' },
            });
            const list = await api.post<Named>('/api/quality-tier-lists', {
                name: 'Administration quality list',
                mimeTypes: ['video/*'],
                upgradeAllowed: true,
                qualityTierRanks: [
                    { qualityTierId: firstTier.id, ordinal: 0, isCutoff: false },
                    { qualityTierId: secondTier.id, ordinal: 1, isCutoff: true },
                ],
            });
            await api.patch(`/api/quality-tier-lists/${list.id}`, {
                name: { value: 'Administration quality list updated' },
                upgradeAllowed: { value: false },
                qualityTierRanks: {
                    update: [
                        {
                            qualityTierId: { value: firstTier.id },
                            ordinal: { value: 1 },
                            isCutoff: { value: true },
                        },
                        {
                            qualityTierId: { value: secondTier.id },
                            ordinal: { value: 0 },
                            isCutoff: { value: false },
                        },
                    ],
                },
            });
            const ranked = await api.get<{
                name: string;
                upgradeAllowed: boolean;
                qualityTierRanks: Array<{ qualityTierId: string; ordinal: number; isCutoff: boolean }>;
            }>(`/api/quality-tier-lists/${list.id}`);
            expect(ranked.name).toBe('Administration quality list updated');
            expect(ranked.upgradeAllowed).toBe(false);
            expect(ranked.qualityTierRanks.sort((a, b) => a.ordinal - b.ordinal)).toEqual([
                expect.objectContaining({ qualityTierId: secondTier.id, ordinal: 0, isCutoff: false }),
                expect.objectContaining({ qualityTierId: firstTier.id, ordinal: 1, isCutoff: true }),
            ]);
            await api.delete(`/api/quality-tier-lists/${list.id}`);
            await api.delete(`/api/quality-tiers/${firstTier.id}`);
            await api.delete(`/api/quality-tiers/${secondTier.id}`);
        });

        await test.step('administer triggers and prove job completion, progress, parent links and cancellation', async () => {
            const schedule = await api.post<Named>('/api/schedule-triggers', {
                jobType: 'IntegrationHealthCheck',
                scope: 'Administrative',
                name: 'Administration schedule',
                schedule: '17 3 * * *',
                executionMode: 'Queue',
                enabled: true,
                parameters: {},
            });
            await api.patch(`/api/schedule-triggers/${schedule.id}`, {
                name: { value: 'Administration schedule updated' },
                schedule: { value: '23 4 * * *' },
                enabled: { value: false },
            });
            expect(await api.get(`/api/schedule-triggers/${schedule.id}`)).toMatchObject({
                name: 'Administration schedule updated',
                schedule: '23 4 * * *',
                enabled: false,
            });
            await api.patch(`/api/schedule-triggers/${schedule.id}`, { enabled: { value: true } });

            const event = await api.post<Named>('/api/event-triggers', {
                jobType: 'DownloadCleanup',
                scope: 'Administrative',
                name: 'Administration event',
                eventFilter: { eventName: { operator: 'Equals', value: 'download.failed' } },
                enabled: true,
                parameters: {},
            });
            await api.patch(`/api/event-triggers/${event.id}`, {
                name: { value: 'Administration event updated' },
                enabled: { value: false },
            });
            expect(await api.get(`/api/event-triggers/${event.id}`)).toMatchObject({
                name: 'Administration event updated',
                enabled: false,
            });
            await api.patch(`/api/event-triggers/${event.id}`, { enabled: { value: true } });

            const specs = await api.get<Array<{ jobType: string; onDemand: boolean }>>('/api/jobs/specs');
            expect(specs).toContainEqual(expect.objectContaining({ jobType: 'IntegrationHealthCheck' }));
            const completedId = await api.post<string>('/api/jobs/enqueue', { jobType: 'IntegrationHealthCheck' });
            const completed = await waitForJob(api, completedId, 150_000);
            expect(completed.status, completed.statusMessage ?? undefined).toBe('Completed');
            expect(completed.progress).toBe(100);
            const tree = await api.get<{ root: JobExecution & { children: JobExecution[] }; totalCount: number }>(
                `/api/jobs/${completedId}/execution-tree`,
            );
            expect(tree.root.id).toBe(completedId);
            expect(tree.root.parentExecutionId).toBeNull();
            expect(tree.totalCount).toBe(1);

            await mocks.indexer.setLatency({ testConnection: 30_000 });
            await mocks.downloadClient.setLatency({ TestConnectionAsync: 30_000 });
            await mocks.mediaServer.setLatency({ TestConnectionAsync: 30_000 });
            const cancellableClient = (await api.get<Named[]>('/api/download-clients')).find(
                (client) => client.name === 'Local Download Client',
            )!;
            await api.patch(`/api/download-clients/${cancellableClient.id}`, { lastChecked: { value: null } });
            const cancelledId = await api.post<string>('/api/jobs/enqueue', {
                jobType: 'IntegrationHealthCheck',
                parameters: { timeoutSeconds: '60' },
            });
            await until(
                `job ${cancelledId} to run`,
                async () => ((await api.get<JobExecution>(`/api/jobs/${cancelledId}`)).status === 'Running' ? true : undefined),
                30_000,
                100,
            );
            await api.post(`/api/jobs/${cancelledId}/cancel`);
            expect((await waitForJob(api, cancelledId, 60_000)).status).toBe('Cancelled');
            await mocks.indexer.setLatency({});
            await mocks.downloadClient.setLatency({});
            await mocks.mediaServer.setLatency({});
            await api.post(`/api/download-clients/${cancellableClient.id}/test`);

            const executions = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=100');
            expect(executions.items.map((execution) => execution.id)).toEqual(
                expect.arrayContaining([completedId, cancelledId]),
            );
            await api.delete(`/api/event-triggers/${event.id}`);
            await api.delete(`/api/schedule-triggers/${schedule.id}`);
        });

        await test.step('administer a package source and a local extension lifecycle', async () => {
            const source = await api.post<Named>('/api/extensions/package-sources', {
                name: 'Administration package source',
                sourceUrl: 'https://packages.administration.invalid/v3/index.json',
                enabled: false,
                searchFilterKind: 'None',
                credentialKind: 'None',
            });
            await api.patch(`/api/extensions/package-sources/${source.id}`, {
                name: { value: 'Administration package source updated' },
            });
            expect(await api.get(`/api/extensions/package-sources/${source.id}`)).toMatchObject({
                name: 'Administration package source updated',
                enabled: false,
            });
            await api.delete(`/api/extensions/package-sources/${source.id}`);

            const installed = await cli.json<ExtensionInstallResult>([
                'extensions',
                'upload',
                '--file',
                '/app/test-packages/Magpie.Extensions.OpenLibrary.1.0.0.nupkg',
            ]);
            expect(installed.restartRequired).toBe(true);
            api = await restartHost(api);

            await page.goto('/admin/ext/openlibrary');
            await expect(page.getByRole('heading', { name: 'Open Library Settings', level: 1 })).toBeVisible();

            await api.patch(`/api/settings/groups/${installed.extension.id}/magpie.openlibrary`, {
                settings: { add: { contact: 'integration@example.invalid' } },
            });
            expect(await api.get(`/api/settings/groups/${installed.extension.id}/magpie.openlibrary`)).toMatchObject({
                settings: [expect.objectContaining({ key: 'contact', value: 'integration@example.invalid' })],
            });

            await api.post(`/api/extensions/${installed.extension.id}/disable`);
            api = await restartHost(api);
            expect(await api.get(`/api/extensions/${installed.extension.id}`)).toMatchObject({ enabled: false });
            await api.post(`/api/extensions/${installed.extension.id}/enable`);
            api = await restartHost(api);
            await page.goto('/admin/ext/openlibrary');
            await expect(page.getByRole('heading', { name: 'Open Library Settings', level: 1 })).toBeVisible();

            const upgraded = await cli.json<ExtensionInstallResult>([
                'extensions',
                'upload',
                '--file',
                '/app/test-packages/Magpie.Extensions.OpenLibrary.1.1.0.nupkg',
            ]);
            expect(upgraded.extension.id).toBe(installed.extension.id);
            expect(upgraded.extension.version).toMatch(/^1\.1\.0/);
            api = await restartHost(api);
            expect(await api.get<{ version: string }>(`/api/extensions/${installed.extension.id}`)).toMatchObject({
                version: expect.stringMatching(/^1\.1\.0/),
            });

            await api.post(`/api/extensions/${installed.extension.id}/disable`);
            api = await restartHost(api);
            expect(await api.get(`/api/extensions/${installed.extension.id}`)).toMatchObject({ enabled: false });
        });

        await test.step('read status and run supported Doctor diagnosis and repair', async () => {
            const system = await api.get<{
                version: string;
                nodeId: string;
                restartSupported: boolean;
                startedAt: string;
            }>('/api/system');
            const health = await api.get<{ status: string }>('/api/system/health');
            const disk = await api.get<{
                folders: Array<{ totalBytes: number; freeBytes: number; usedBytes: number }>;
            }>('/api/system/disk-space');
            expect(system.version).toBeTruthy();
            expect(system.nodeId).toBeTruthy();
            expect(system.restartSupported).toBe(true);
            expect(health.status).toMatch(/Healthy|Degraded|Unhealthy/);
            for (const folder of disk.folders) {
                expect(folder.freeBytes + folder.usedBytes).toBe(folder.totalBytes);
            }

            const schedules = (await api.get<Array<Named & { jobType: string }>>('/api/schedule-triggers')).filter(
                (trigger) => trigger.jobType === 'IntegrationHealthCheck',
            );
            expect(schedules.length).toBeGreaterThan(0);
            for (const schedule of schedules) {
                await api.delete(`/api/schedule-triggers/${schedule.id}`);
            }
            const scan = await api.post<{ executionId: string; status: string }>('/api/jobs/execute-immediate', {
                jobType: 'DiagnosticCheck',
            });
            expect(scan.status).toBe('Completed');
            const findings = await api.get<{
                findings: Array<{ id: string; affectedEntity: string | null; repairId: string | null }>;
                lastScannedAt: string | null;
            }>('/api/diagnostics/findings');
            const missingSchedule = findings.findings.find(
                (finding) => finding.affectedEntity === 'Job:IntegrationHealthCheck',
            );
            expect(missingSchedule).toMatchObject({ repairId: 'core.config.reseed-default-job' });
            expect(findings.lastScannedAt).toBeTruthy();
            expect(await api.post(`/api/diagnostics/findings/${missingSchedule!.id}/repair`)).toMatchObject({
                success: true,
            });
            await api.post('/api/jobs/execute-immediate', { jobType: 'DiagnosticCheck' });
            expect(
                (
                    await api.get<{ findings: Array<{ affectedEntity: string | null }> }>('/api/diagnostics/findings')
                ).findings.some((finding) => finding.affectedEntity === 'Job:IntegrationHealthCheck'),
            ).toBe(false);
        });

        await test.step('prove one matching notification is delivered once and an unrelated event is ignored', async () => {
            await mocks.notification.clearCalls();
            const rule = await api.post<Named>('/api/notification-rules', {
                name: 'Exactly once notification',
                providerType: mocks.notification.slug,
                enabled: true,
                eventFilter: { eventName: { operator: 'Equals', value: 'apiKey.created' } },
                settings: { target: 'mock://exactly-once', template: 'default' },
            });
            await mkdir(resolve(integrationEnvironment.paths.library, 'unrelated-notification'), { recursive: true });
            const notificationQualityList = (await api.get<Named[]>('/api/quality-tier-lists')).find(
                (list) => list.name === 'Video - HD',
            )!;
            const root = await api.post<Named>('/api/root-folders', {
                name: 'Unrelated notification root',
                path: '/library/unrelated-notification',
                contentType: 'movie',
                defaults: { movie: { qualityTierListId: notificationQualityList.id } },
            });

            const key = await api.post<{ key: { id: string } }>('/api/api-keys', { name: 'Matching notification key' });
            await mocks.notification.waitForCalls(1, { method: 'send' });
            await page.waitForTimeout(1_000);
            const sends = await mocks.notification.calls<{ event: { name: string } }>('send');
            expect(sends).toHaveLength(1);
            expect(sends[0]!.request.event.name).toBe('apiKey.created');
            await api.delete(`/api/api-keys/${key.key.id}`);
            await api.delete(`/api/root-folders/${root.id}`);
            await api.delete(`/api/notification-rules/${rule.id}`);
        });

        await test.step('exercise the masked secret replacement contract without disclosure', async () => {
            const client = (await api.get<Provider[]>('/api/download-clients')).find(
                (item) => item.name === 'Local Download Client',
            )!;
            await mocks.downloadClient.armTestConnection({
                success: true,
                message: 'original secret accepted',
                expectedSettings: { apiKey: 'baseline-download-secret' },
            });
            expect(await api.post(`/api/download-clients/${client.id}/test`)).toMatchObject({
                success: true,
                message: 'original secret accepted',
            });

            await page.goto('/admin/download-clients');
            await page.getByRole('button', { name: `Edit ${client.name}` }).click();
            const form = page.getByRole('dialog', { name: 'Edit Download Client' });
            const secret = form.locator('input[data-field="apiKey"]');
            await expect(secret).toHaveValue('');
            await expect(secret).toHaveAttribute('readonly', '');
            await expect(secret).toHaveAttribute('placeholder', '********');
            await expect(form.getByRole('button', { name: 'Replace API Key' })).toBeVisible();

            await form.getByLabel('Host').fill('download-unrelated-update.invalid');
            await form.locator('[data-action="save"]').click();
            await expect(form).toBeHidden();
            expect(await api.post(`/api/download-clients/${client.id}/test`)).toMatchObject({ success: true });

            await page.getByRole('button', { name: `Edit ${client.name}` }).click();
            const blankReplacement = page.getByRole('dialog', { name: 'Edit Download Client' });
            const blankSecret = blankReplacement.locator('input[data-field="apiKey"]');
            await blankReplacement.getByRole('button', { name: 'Replace API Key' }).click();
            await blankSecret.blur();
            await expect(blankSecret).toHaveAttribute('readonly', '');
            await expect(blankReplacement.getByRole('button', { name: 'Replace API Key' })).toBeVisible();

            await blankReplacement.getByRole('button', { name: 'Replace API Key' }).click();
            await blankSecret.fill('replacement-download-secret');
            await blankReplacement.locator('[data-action="save"]').click();
            await expect(blankReplacement).toBeHidden();

            await mocks.downloadClient.armTestConnection({
                success: true,
                message: 'old secret only',
                expectedSettings: { apiKey: 'baseline-download-secret' },
            });
            expect(await api.post(`/api/download-clients/${client.id}/test`)).toMatchObject({ success: false });
            await mocks.downloadClient.armTestConnection({
                success: true,
                message: 'replacement secret accepted',
                expectedSettings: { apiKey: 'replacement-download-secret' },
            });
            expect(await api.post(`/api/download-clients/${client.id}/test`)).toMatchObject({
                success: true,
                message: 'replacement secret accepted',
            });
            expect(JSON.stringify(await api.get(`/api/download-clients/${client.id}`))).not.toContain(
                'replacement-download-secret',
            );
            await mocks.downloadClient.armTestConnection({ success: true, message: 'default' });
        });

        await test.step('deny administrator surfaces to the non-administrator', async () => {
            const { api: requester } = await loginAdmin(REQUESTER_USERNAME, REQUESTER_PASSWORD);
            await expect(requester.get('/api/users')).rejects.toMatchObject({ status: 403 });
            await expect(
                requester.post('/api/root-folders', {
                    name: 'Forbidden root',
                    path: '/library/forbidden',
                    contentType: 'movie',
                }),
            ).rejects.toMatchObject({ status: 403 });

            const requesterPage = await page.context().browser()!.newPage();
            const response = await requesterPage.context().request.post(`${integrationEnvironment.baseUrl}/api/auth/login`, {
                data: { username: REQUESTER_USERNAME, password: REQUESTER_PASSWORD },
            });
            const login = (await response.json()) as { accessToken: string };
            await requesterPage.addInitScript(
                ([key, value]) => localStorage.setItem(key, value),
                ['magpie_auth_token', login.accessToken] as const,
            );
            await requesterPage.goto('/admin/general');
            await expect(requesterPage).toHaveURL(/\/forbidden$/);
            await expect(requesterPage.getByText('Access denied', { exact: true })).toBeVisible();
            await requesterPage.close();
        });

        await test.step('run a scheduled backup and an optimization through database maintenance', async () => {
            const maintenanceId = await api.post<string>('/api/jobs/enqueue', {
                jobType: 'DatabaseMaintenance',
                parameters: { backupEnabled: 'true', optimizeEnabled: 'true' },
            });
            expect((await waitForJob(api, maintenanceId, 90_000)).status).toBe('Completed');
            expect(await api.get<Array<{ kind: string }>>('/api/database/backups')).toContainEqual(
                expect.objectContaining({ kind: 'Scheduled' }),
            );
            expect((await api.get<{ lastOptimizedAt: string | null }>('/api/database')).lastOptimizedAt).not.toBeNull();
        });

        await test.step('create, delete and restore a current-runtime backup', async () => {
            const fixtureAnchor = resolve(integrationEnvironment.paths.library, 'movies', 'administration-restore.txt');
            await writeFile(fixtureAnchor, 'restore fixture anchor', 'utf8');
            await createCheckpoint(api, 'administration-restore-point', [
                'library/movies/administration-restore.txt',
                'test-fixtures/mock-indexer/_active',
            ]);

            const disposableId = await cli.json<string>([
                'jobs',
                'enqueue',
                '--json',
                JSON.stringify({ jobType: 'DatabaseBackup', parameters: { name: 'administration-disposable' } }),
            ]);
            expect((await waitForJob(api, disposableId, 90_000)).status).toBe('Completed');
            const disposable = await until(
                'disposable backup to appear',
                async () =>
                    (await api.get<Array<{ id: string; name: string }>>('/api/database/backups')).find(
                        (item) => item.name === 'administration-disposable',
                    ),
                30_000,
                250,
            );
            await api.delete(`/api/database/backups/${disposable.id}`);

            await cli.run([
                'settings',
                'patch-system',
                'general',
                '--json',
                JSON.stringify({ settings: { update: { instanceName: 'Later database change' } } }),
            ]);
            await mkdir(resolve(integrationEnvironment.paths.library, 'later-root'), { recursive: true });
            const laterQualityList = (await api.get<Named[]>('/api/quality-tier-lists')).find(
                (list) => list.name === 'Video - HD',
            )!;
            await api.post('/api/root-folders', {
                name: 'Later root',
                path: '/library/later-root',
                contentType: 'movie',
                defaults: { movie: { qualityTierListId: laterQualityList.id } },
            });
            const requester = (
                await api.get<Paged<{ id: string; username: string }>>('/api/users?pageSize=100')
            ).items.find((user) => user.username === REQUESTER_USERNAME)!;
            await api.post(`/api/users/${requester.id}/password`, { newPassword: 'later-requester-pa55!' });
            await rm(fixtureAnchor, { force: true });
            await writeFile(resolve(integrationEnvironment.paths.library, 'movies', 'later-only.txt'), 'later', 'utf8');

            api = await restoreCheckpoint('administration-restore-point', async (restored) => {
                expect(settingsRecord(await cli.json<SettingsGroup>(['settings', 'get-system', 'general']))).toMatchObject({
                    instanceName: 'Journey administration from CLI',
                });
                expect((await restored.get<Named[]>('/api/root-folders')).some((root) => root.name === 'Later root')).toBe(
                    false,
                );
                expect((await loginAdmin(REQUESTER_USERNAME, REQUESTER_PASSWORD)).api).toBeDefined();
                await expect(loginAdmin(REQUESTER_USERNAME, 'later-requester-pa55!')).rejects.toMatchObject({
                    status: 401,
                });
            });
            await expect(stat(resolve(integrationEnvironment.paths.library, 'movies', 'later-only.txt'))).rejects.toThrow();

            await page.goto('/admin/general');
            await expect(page.getByLabel('Instance Name')).toHaveValue('Journey administration from CLI');
            expect(settingsRecord(await cli.json<SettingsGroup>(['settings', 'get-system', 'general']))).toMatchObject({
                instanceName: 'Journey administration from CLI',
            });
        });
    });
});

interface Setting {
    key: string;
    value: string;
}

interface SettingsGroup {
    name: string;
    settings: Setting[];
}

function settingsRecord(group: SettingsGroup): Record<string, string> {
    return Object.fromEntries(group.settings.map((setting) => [setting.key, setting.value]));
}
