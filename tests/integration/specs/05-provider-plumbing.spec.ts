import { expect, test } from '@playwright/test';

import { ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { waitForJob, type JobExecution } from '../helpers/jobs';
import { mocks } from '../helpers/mocks';
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
    settings: Record<string, string>;
}

interface Indexer extends Named {
    providerType: string;
    feedId?: string | null;
    connectionCount: number;
}

interface IndexerFeed extends Omit<Provider, 'status'> {
    description?: string | null;
    lastSync?: string | null;
}

interface Paged<T> {
    items: T[];
}

interface ListedJob extends JobExecution {
    parameters?: Record<string, string>;
    attempt?: number | null;
    originalExecutionId?: string | null;
    createdAt: string;
}

interface RunJobResponse {
    executionId: string;
    status: string;
}

interface NotificationAuditRequest {
    rule: { id: string; name: string };
    event: { name: string; context: Record<string, string> };
}

test.describe.serial('Journey 05: wire up the plumbing', () => {
    test('connects indexers, feeds, notifications and media servers', async ({ page }) => {
        test.setTimeout(600_000);

        let api: ApiClient;
        let feedId = '';

        await test.step('restore the configured baseline and sign in as the administrator', async () => {
            api = await restoreCheckpoint('configured-baseline', async (restored) => {
                expect(await restored.get<Indexer[]>('/api/indexers')).toContainEqual(
                    expect.objectContaining({ name: 'Local Indexer', providerType: mocks.indexer.slug }),
                );
                expect(await restored.get<Provider[]>('/api/media-servers')).toContainEqual(
                    expect.objectContaining({ name: 'Local Media Server', providerType: mocks.mediaServer.slug }),
                );
            });
            await loginUi(page);
        });

        await test.step('test an indexer configuration before saving it', async () => {
            await mocks.indexer.useScenario('journey-05-indexer');
            await mocks.indexer.armTestConnection({
                success: true,
                message: 'journey indexer settings accepted',
            });
            await mocks.indexer.clearCalls();

            expect(
                await api.post('/api/indexers/test', {
                    providerType: mocks.indexer.slug,
                    settings: {
                        apiUrl: 'https://provider-plumbing.indexer.invalid',
                        apiKey: 'journey-indexer-secret',
                    },
                }),
            ).toEqual({
                success: true,
                message: 'journey indexer settings accepted',
                failureKind: 'None',
            });

            const calls = await mocks.indexer.calls<{ settingsKeys: string[] }>('testConnection');
            expect(calls).toHaveLength(1);
            expect(calls[0]!.request.settingsKeys).toEqual(['apiKey', 'apiUrl']);
        });

        await test.step('the indexer form renders from the schema the server publishes', async () => {
            const indexer = (await api.get<Indexer[]>('/api/indexers')).find((item) => item.name === 'Local Indexer')!;
            const schema = await api.get<
                Array<{
                    key: string;
                    type: string;
                    displayName: string;
                    required: boolean;
                    secret: boolean;
                }>
            >(`/api/indexers/${indexer.id}/schema`);

            expect(
                schema.map(({ key, type, displayName, required, secret }) => ({
                    key,
                    type,
                    displayName,
                    required,
                    secret,
                })),
            ).toEqual([
                {
                    key: 'apiUrl',
                    type: 'String',
                    displayName: 'API URL',
                    required: true,
                    secret: false,
                },
                {
                    key: 'apiKey',
                    type: 'String',
                    displayName: 'API Key',
                    required: true,
                    secret: true,
                },
            ]);
        });

        await test.step('browse the catalog and configure an indexer from it', async () => {
            const indexer = (await api.get<Indexer[]>('/api/indexers')).find((item) => item.name === 'Local Indexer')!;
            await page.goto('/admin/indexers');
            await page.locator('[data-tab="available"]').click();
            const row = page.getByRole('row').filter({ hasText: 'Local Indexer' });
            await row.locator(`[data-action="configure"][data-indexer-id="${indexer.id}"]`).click();

            const form = page.locator('[data-component="configure-instance"]');
            await form.locator('[data-field="instance-name"]').fill('Journey Catalog Connection');
            await form.locator('input[data-field="apiUrl"]').fill('https://catalog.indexer.invalid');
            await form.locator('input[data-field="apiKey"]').fill('journey-catalog-secret');
            await form.locator('[data-action="test-instance"]').click();
            await expect(page.getByText('journey indexer settings accepted', { exact: true })).toBeVisible();
            await form.locator('[data-action="add-instance"]').click();
            await expect(form).toBeHidden();

            expect(await api.get<Named[]>(`/api/indexers/${indexer.id}/connections`)).toContainEqual(
                expect.objectContaining({ name: 'Journey Catalog Connection' }),
            );
        });

        await test.step('add a feed from the feeds tab, edit it, and sync it', async () => {
            await mocks.indexerFeed.useScenario('journey-05-feed');
            await mocks.indexerFeed.write('SyncAsync', []);
            await mocks.indexerFeed.clearCalls();

            await page.locator('[data-tab="feeds"]').click();
            await page.locator('[data-action="add-feed"]').first().click();
            let form = page.locator('[data-component="indexer-feed-form"]');
            await form.locator('[data-field="name"]').fill('Journey Provider Feed');
            await form.locator('[data-field="description"]').fill('Initial provider feed');
            await form.locator('[data-field="provider"]').click();
            await page.getByRole('option', { name: mocks.indexerFeed.slug }).click();
            await form.locator('input[data-field="endpoint"]').fill('local://journey-provider-feed');
            await form.locator('input[data-field="apiKey"]').fill('journey-feed-secret');
            await form.locator('[data-action="save"]').click();
            await expect(form).toBeHidden();

            const created = (await api.get<IndexerFeed[]>('/api/indexer-feeds')).find(
                (feed) => feed.name === 'Journey Provider Feed',
            );
            expect(created).toBeDefined();
            feedId = created!.id;
            const createdSync = await until(
                'the new indexer feed to finish the sync its creation queued',
                async () => {
                    const feed = (await api.get<IndexerFeed[]>('/api/indexer-feeds')).find(
                        (candidate) => candidate.id === feedId,
                    );
                    return feed?.lastSync ?? undefined;
                },
                30_000,
                250,
            );

            await page.locator(`[data-action="edit"][data-target="${feedId}"]`).click();
            form = page.locator('[data-component="indexer-feed-form"]');
            await form.locator('[data-field="name"]').fill('Journey Provider Feed Updated');
            await form.locator('[data-field="description"]').fill('Updated provider feed');
            await form.locator('[data-action="save"]').click();
            await expect(form).toBeHidden();

            await page.locator(`[data-action="sync"][data-target="${feedId}"]`).click();
            await expect(page.getByText('Sync started', { exact: true })).toBeVisible();
            const synced = await until(
                'the edited indexer feed to finish its manual sync',
                async () => {
                    const feed = (await api.get<IndexerFeed[]>('/api/indexer-feeds')).find(
                        (candidate) => candidate.id === feedId,
                    );
                    return feed?.lastSync && feed.lastSync !== createdSync ? feed : undefined;
                },
                30_000,
                250,
            );
            expect(synced).toMatchObject({
                name: 'Journey Provider Feed Updated',
                description: 'Updated provider feed',
                enabled: true,
            });
        });

        await test.step('the feed sync imports definitions that become selectable indexers', async () => {
            await mocks.indexerFeed.write('SyncAsync', [
                {
                    providerType: 'journey-feed-indexer',
                    name: 'Journey Feed Indexer',
                    description: 'Imported from the journey feed',
                    availability: 'Public',
                    categories: ['Movies'],
                    languages: ['en'],
                    supportedContentTypes: ['movie'],
                },
            ]);
            await mocks.indexerFeed.clearCalls();

            const priorJobs = await api.get<Paged<ListedJob>>('/api/jobs?jobType=IndexerFeedSyncItem&pageSize=100');
            const priorIds = new Set(priorJobs.items.map((job) => job.id));
            const scan = await api.post<RunJobResponse>('/api/jobs/execute-immediate', {
                jobType: 'IndexerFeedSyncScan',
            });
            expect((await waitForJob(api, scan.executionId)).status).toBe('Completed');

            const item = await until(
                'the feed sync scan to queue its per-feed item job',
                async () => {
                    const jobs = await api.get<Paged<ListedJob>>('/api/jobs?jobType=IndexerFeedSyncItem&pageSize=100');
                    return jobs.items.find(
                        (job) => !priorIds.has(job.id) && job.parameters?.['feedId'] === feedId,
                    );
                },
                30_000,
                250,
            );
            expect((await waitForJob(api, item.id)).status).toBe('Completed');

            const syncCalls = await mocks.indexerFeed.calls<unknown, Array<{ name: string }>>('SyncAsync');
            expect(syncCalls).toHaveLength(1);
            expect(syncCalls[0]!.response).toContainEqual(expect.objectContaining({ name: 'Journey Feed Indexer' }));

            const imported = (await api.get<Indexer[]>('/api/indexers')).find(
                (indexer) => indexer.name === 'Journey Feed Indexer',
            );
            expect(imported).toMatchObject({
                providerType: 'journey-feed-indexer',
                feedId,
                connectionCount: 0,
            });

            await page.locator('[data-tab="available"]').click();
            const row = page.getByRole('row').filter({ hasText: 'Journey Feed Indexer' });
            await expect(row).toBeVisible();
            await row.locator(`[data-action="configure"][data-indexer-id="${imported!.id}"]`).click();
            const form = page.locator('[data-component="configure-instance"]');
            await form.locator('[data-field="instance-name"]').fill('Journey Imported Connection');
            await form.locator('[data-action="add-instance"]').click();
            await expect(form).toBeHidden();
            expect(await api.get<Named[]>(`/api/indexers/${imported!.id}/connections`)).toContainEqual(
                expect.objectContaining({ name: 'Journey Imported Connection' }),
            );
        });

        await test.step('delete the feed and the tab reflects it', async () => {
            await page.locator('[data-tab="feeds"]').click();
            const row = page.getByRole('row').filter({ hasText: 'Journey Provider Feed Updated' });
            await row.locator(`[data-action="delete"][data-target="${feedId}"]`).click();
            await page.locator('[data-component="delete-confirm-dialog"] [data-action="confirm-delete"]').click();
            await expect
                .poll(async () => (await api.get<IndexerFeed[]>('/api/indexer-feeds')).some((feed) => feed.id === feedId))
                .toBe(false);
            await expect(row).toBeHidden();
            expect((await api.get<Indexer[]>('/api/indexers')).some((indexer) => indexer.name === 'Journey Feed Indexer')).toBe(
                false,
            );
        });

        await test.step('a rule with a compound filter fires on the right event and stays quiet on the near-miss', async () => {
            await mocks.notification.useScenario('journey-05-notification');
            await mocks.notification.write('send.json', { delivered: true });
            const rule = await api.post<Named>('/api/notification-rules', {
                name: 'Journey compound notification',
                providerType: mocks.notification.slug,
                enabled: true,
                eventFilter: {
                    eventName: { operator: 'Regex', value: '^apiKey\\.(created|updated)$' },
                    eventContext: {
                        items: {
                            'apiKey.changedFields': { operator: 'Regex', value: '(^|,)Enabled(,|$)' },
                        },
                    },
                },
                settings: { target: 'mock://journey-compound', template: 'compound' },
            });
            await mocks.notification.clearCalls();

            const key = await api.post<{ key: { id: string } }>('/api/api-keys', {
                name: 'Journey compound near miss',
            });
            await api.patch(`/api/api-keys/${key.key.id}`, { enabled: { value: false } });
            await mocks.notification.waitForCalls(1, { method: 'send' });
            const calls = await mocks.notification.calls<NotificationAuditRequest>('send');
            expect(calls).toHaveLength(1);
            expect(calls[0]!.request).toMatchObject({
                rule: { id: rule.id, name: 'Journey compound notification' },
                event: {
                    name: 'apiKey.updated',
                    context: { 'apiKey.id': key.key.id, 'apiKey.changedFields': 'Enabled' },
                },
            });

            await api.delete(`/api/api-keys/${key.key.id}`);
            await api.delete(`/api/notification-rules/${rule.id}`);
        });

        await test.step('a disabled rule delivers nothing when its event fires', async () => {
            const rule = await api.post<Named>('/api/notification-rules', {
                name: 'Journey disabled notification',
                providerType: mocks.notification.slug,
                enabled: false,
                eventFilter: { eventName: { operator: 'Equals', value: 'apiKey.created' } },
                settings: { target: 'mock://journey-disabled', template: 'disabled' },
            });
            await mocks.notification.clearCalls();

            const key = await api.post<{ key: { id: string } }>('/api/api-keys', {
                name: 'Journey disabled notification event',
            });
            await page.waitForTimeout(1_000);
            expect(await mocks.notification.calls('send')).toHaveLength(0);

            await api.delete(`/api/api-keys/${key.key.id}`);
            await api.delete(`/api/notification-rules/${rule.id}`);
        });

        await test.step('a failing delivery is retried per policy and surfaces as a failed send job', async () => {
            await api.patch('/api/settings/groups/system/job-retry', {
                settings: {
                    add: {
                        'base-seconds': '1',
                        factor: '1',
                        'cap-seconds': '1',
                        'max-attempts': '3',
                    },
                },
            });
            await mocks.notification.write('send', { delivered: false });
            const rule = await api.post<Named>('/api/notification-rules', {
                name: 'Journey failing notification',
                providerType: mocks.notification.slug,
                enabled: true,
                eventFilter: { eventName: { operator: 'Equals', value: 'apiKey.created' } },
                settings: { target: 'mock://journey-failing', template: 'failing' },
            });
            await mocks.notification.clearCalls();

            const key = await api.post<{ key: { id: string } }>('/api/api-keys', {
                name: 'Journey failing notification event',
            });
            const deliveryCalls = await mocks.notification.waitForCalls<NotificationAuditRequest, { delivered: boolean }>(
                3,
                { method: 'send', timeoutMs: 60_000 },
            );
            expect(deliveryCalls).toHaveLength(3);
            expect(deliveryCalls.map((call) => call.response.delivered)).toEqual([false, false, false]);

            const executions = await until(
                'the notification retry policy to exhaust three failed attempts',
                async () => {
                    const jobs = await api.get<Paged<ListedJob>>('/api/jobs?jobType=SendNotification&pageSize=100');
                    const matching = jobs.items
                        .filter((job) => job.parameters?.['ruleId'] === rule.id)
                        .sort((left, right) => left.createdAt.localeCompare(right.createdAt));
                    return matching.length === 3 && matching.every((job) => job.status === 'Failed')
                        ? matching
                        : undefined;
                },
                60_000,
                250,
            );
            expect(executions.map((job) => job.attempt)).toEqual([null, 2, 3]);
            expect(executions.slice(1).map((job) => job.originalExecutionId)).toEqual([
                executions[0]!.id,
                executions[0]!.id,
            ]);
            expect(executions[2]).toMatchObject({
                jobType: 'SendNotification',
                status: 'Failed',
                attempt: 3,
                originalExecutionId: executions[0]!.id,
            });
            expect(executions[2]!.statusMessage).toContain('mock notification scenario rejected the delivery');

            await api.delete(`/api/api-keys/${key.key.id}`);
            await api.delete(`/api/notification-rules/${rule.id}`);
            await api.patch('/api/settings/groups/system/job-retry', {
                settings: { remove: ['base-seconds', 'factor', 'cap-seconds', 'max-attempts'] },
            });
            await mocks.notification.useScenario('default');
        });

        await test.step("the rule form validates and renders the provider's own settings fields", async () => {
            await page.goto('/admin/notifications');
            await page.locator('[data-action="add-rule"]').first().click();
            const form = page.locator('[data-component="notification-rule-form"]');
            await form.locator('[data-field="providerType"]').click();
            await page.getByRole('option', { name: 'webhook' }).click();

            await expect(form.locator('input[data-field="url"]')).toBeVisible();
            await expect(form.locator('[data-field="method"]')).toBeVisible();
            await expect(form.locator('input[data-field="headers"]')).toBeVisible();
            await form.locator('[data-action="save"]').click();
            await expect(form.getByText('Name is required', { exact: true })).toBeVisible();
            await expect(form.locator('[data-field="name"]')).toHaveAttribute('aria-invalid', 'true');
            await form.locator('[data-action="cancel"]').click();
            await page.locator('[data-component="unsaved-changes-dialog"] [data-action="discard"]').click();
            await expect(form).toBeHidden();
        });

        await test.step('a manual media server refresh reports one outcome per server', async () => {
            const local = (await api.get<Provider[]>('/api/media-servers')).find(
                (server) => server.name === 'Local Media Server',
            )!;
            const second = await api.post<Provider>('/api/media-servers', {
                name: 'Journey Media Server',
                providerType: mocks.mediaServer.slug,
                enabled: true,
                contentTypes: ['movie'],
                settings: { host: 'journey-media.invalid', port: '8096', apiKey: 'journey-media-secret' },
            });
            await mocks.mediaServer.useScenario('journey-05-media-server');
            await mocks.mediaServer.armTestConnection({ success: true, message: 'journey media server healthy' });
            await mocks.mediaServer.armRefresh(true);
            await mocks.mediaServer.clearCalls();

            const started = await api.post<RunJobResponse>('/api/jobs/execute-immediate', {
                jobType: 'MediaServerRefresh',
            });
            expect((await waitForJob(api, started.executionId)).status).toBe('Completed');
            const calls = await mocks.mediaServer.waitForCalls<
                { serverId: string },
                { refreshed: boolean }
            >(2, { method: 'RefreshForContentAsync' });
            expect(calls).toHaveLength(2);
            expect(calls.map((call) => call.request.serverId).sort()).toEqual([local.id, second.id].sort());
            expect(calls.map((call) => call.response.refreshed)).toEqual([true, true]);
        });

        await test.step('an unrelated field change does not clobber a stored secret, and an explicit replacement takes effect', async () => {
            const client = (await api.get<Provider[]>('/api/download-clients')).find(
                (item) => item.name === 'Local Download Client',
            )!;
            await mocks.downloadClient.useScenario('journey-05-download');
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
            let form = page.getByRole('dialog', { name: 'Edit Download Client' });
            const secret = form.locator('input[data-field="apiKey"]');
            await expect(secret).toHaveValue('');
            await expect(secret).toHaveAttribute('readonly', '');
            await expect(secret).toHaveAttribute('placeholder', '********');

            await form.getByLabel('Host').fill('journey-download-update.invalid');
            await form.locator('[data-action="save"]').click();
            await expect(form).toBeHidden();
            expect(await api.post(`/api/download-clients/${client.id}/test`)).toMatchObject({ success: true });

            await page.getByRole('button', { name: `Edit ${client.name}` }).click();
            form = page.getByRole('dialog', { name: 'Edit Download Client' });
            const replacement = form.locator('input[data-field="apiKey"]');
            await form.getByRole('button', { name: 'Replace API Key' }).click();
            await replacement.fill('journey-replacement-secret');
            await form.locator('[data-action="save"]').click();
            await expect(form).toBeHidden();

            await mocks.downloadClient.armTestConnection({
                success: true,
                message: 'old secret only',
                expectedSettings: { apiKey: 'baseline-download-secret' },
            });
            expect(await api.post(`/api/download-clients/${client.id}/test`)).toMatchObject({ success: false });
            await mocks.downloadClient.armTestConnection({
                success: true,
                message: 'replacement secret accepted',
                expectedSettings: { apiKey: 'journey-replacement-secret' },
            });
            expect(await api.post(`/api/download-clients/${client.id}/test`)).toMatchObject({
                success: true,
                message: 'replacement secret accepted',
            });
            expect(JSON.stringify(await api.get(`/api/download-clients/${client.id}`))).not.toContain(
                'journey-replacement-secret',
            );
        });
    });
});
