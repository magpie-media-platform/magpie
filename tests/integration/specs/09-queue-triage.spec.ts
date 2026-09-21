import { rm, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { FIXTURES_ROOT } from '../helpers/fixtures';
import { drainDownloadRefresh, runImmediateJob, waitForJob, type JobExecution } from '../helpers/jobs';
import { mocks, type LandedFileFixture, type ReleaseFixture } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface RootFolder {
    id: string;
    contentType: string;
    defaults: Record<string, { qualityTierListId: string }>;
}

interface ContentItem {
    id: string;
    name: string;
    pendingMetadataRefresh: boolean;
    contentItem?: {
        fulfillment: string;
        mediaFiles: Array<{ filePath: string }>;
    } | null;
}

interface Download {
    id: string;
    releaseId?: string | null;
    externalId: string;
    failureReason?: string | null;
    lastProgressAt?: string | null;
    progress?: number | null;
    status: string;
    statusReason?: string | null;
    title: string;
}

interface InteractiveCandidate {
    release: ReleaseFixture & {
        id: string;
        externalId: string;
    };
}

interface RunJobResponse {
    executionId: string;
    status: string;
}

interface BulkResult {
    count: number;
    error?: string | null;
}

interface DomainFixture {
    categories: string[];
    contentType: string;
    externalId: string;
    level: string;
    title: string;
}

interface DownloadFixture {
    contentType: string;
    downloadExternalId: string;
    land?: LandedFileFixture[];
    progressPercent: number;
    publishDate: string;
    releaseExternalId: string;
    releaseTitle: string;
    status: string;
    statusReason?: string;
}

const metadataScenario = 'journey-09-metadata';
const indexerScenario = 'journey-09-indexer';
const downloadScenario = 'journey-09-download';

const domains: DomainFixture[] = [
    {
        contentType: 'movie',
        level: 'movie',
        title: 'Queue Journey Movie',
        externalId: 'journey-09-movie',
        categories: ['movies'],
    },
    {
        contentType: 'tv',
        level: 'show',
        title: 'Queue Journey Series',
        externalId: 'journey-09-series',
        categories: ['tv'],
    },
    {
        contentType: 'music',
        level: 'artist',
        title: 'Queue Journey Artist',
        externalId: 'journey-09-artist',
        categories: ['music'],
    },
    {
        contentType: 'book',
        level: 'ebook',
        title: 'Queue Journey Book',
        externalId: 'journey-09-book',
        categories: ['books'],
    },
];

const refused = downloadFixture(
    'music',
    'refused',
    'Queue Journey Artist - Client Refused',
    'Failed',
    '2030-01-08T00:00:00Z',
    'The download client refused this release.',
);
const forceStarted = downloadFixture(
    'movie',
    'force-start',
    'Queue Journey Movie 2026 Force Start',
    'Queued',
    '2026-01-08T00:00:00Z',
);
const stuck = downloadFixture(
    'tv',
    'stuck',
    'Queue Journey Series 2026 Stuck',
    'Stalled',
    '2030-01-09T00:00:00Z',
    'No peers are reporting progress.',
    41,
);
const keepFiles = {
    ...downloadFixture(
        'book',
        'keep-files',
        'Queue Journey Book 2026 Keep Files',
        'Queued',
        '2026-01-07T00:00:00Z',
    ),
    land: [{ source: 'book.pdf', target: 'Queue Journey Book Keep.pdf' }],
};
const removeFiles = {
    ...downloadFixture(
        'movie',
        'remove-files',
        'Queue Journey Movie 2026 Remove Files',
        'Queued',
        '2026-01-06T00:00:00Z',
    ),
    land: [
        {
            source: 'movie-720p.mkv',
            target: 'Queue Journey Movie Remove/Queue Journey Movie Remove.mkv',
        },
    ],
};
const importFailure = {
    ...downloadFixture(
        'movie',
        'import-failure',
        'Queue Journey Movie 2030 Import Failure',
        'Queued',
        '2030-01-10T00:00:00Z',
    ),
    land: [{ source: 'book.pdf', target: 'Queue Journey Movie Wrong Kind.pdf' }],
};
const cleanup = {
    ...downloadFixture(
        'book',
        'cleanup',
        'Queue Journey Book 2026 Cleanup',
        'Queued',
        '2026-01-05T00:00:00Z',
    ),
    land: [{ source: 'book.pdf', target: 'Queue Journey Book Cleanup.pdf' }],
};
const bulkDownloads = Array.from({ length: 26 }, (_, index) =>
    downloadFixture(
        'movie',
        `bulk-${index + 1}`,
        `Queue Journey Movie 2026 Bulk ${index + 1}`,
        'Queued',
        `2026-01-${String(index + 2).padStart(2, '0')}T00:00:00Z`,
    ),
);
const pagingQueue = Array.from({ length: 26 }, (_, index) =>
    downloadFixture(
        'movie',
        `page-queue-${index + 1}`,
        `Queue Journey Movie Queue Page ${String(index + 1).padStart(2, '0')}`,
        'Queued',
        `2025-11-${String(index + 1).padStart(2, '0')}T00:00:00Z`,
    ),
);
const pagingHistory = Array.from({ length: 26 }, (_, index) =>
    downloadFixture(
        'movie',
        `page-history-${index + 1}`,
        `Queue Journey Movie History Page ${String(index + 1).padStart(2, '0')}`,
        'Failed',
        `2025-10-${String(index + 1).padStart(2, '0')}T00:00:00Z`,
        'The paging fixture failed in the client.',
    ),
);
const allDownloadFixtures = [
    refused,
    forceStarted,
    stuck,
    keepFiles,
    removeFiles,
    importFailure,
    cleanup,
    ...bulkDownloads,
    ...pagingQueue,
    ...pagingHistory,
];

test.describe.serial('Journey 9: the night the downloads went wrong', () => {
    test('triages a queue full of failed and stalled downloads', async ({ page }) => {
        test.setTimeout(900_000);

        let api!: ApiClient;
        const content = new Map<string, ContentItem>();
        let refusedDownload!: Download;
        let forceDownload!: Download;
        let stuckDownload!: Download;
        let importFailedDownload!: Download;

        try {
            await test.step('restore the configured baseline and set up several items to acquire', async () => {
                let roots!: RootFolder[];
                api = await restoreCheckpoint('configured-baseline', async (restored) => {
                    roots = await restored.get<RootFolder[]>('/api/root-folders');
                    expect(roots.map((root) => root.contentType).sort()).toEqual(['book', 'movie', 'music', 'tv']);
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
                });
                await loginUi(page);

                await armMetadataFixtures();
                for (const domain of domains) {
                    const root = roots.find((candidate) => candidate.contentType === domain.contentType)!;
                    const id = await api.post<string>(
                        `/api/content/from-external-id?monitoring=false&rootFolderId=${root.id}` +
                            `&qualityTierListId=${root.defaults[domain.level]!.qualityTierListId}`,
                        {
                            providerType: mocks.hierarchyPrimary.slug,
                            externalIdKey: 'journey',
                            externalId: domain.externalId,
                            contentType: domain.contentType,
                            levelSlug: domain.level,
                        },
                    );
                    const added = await until(
                        `${domain.title} metadata refresh`,
                        async () => {
                            const current = await api.get<ContentItem>(`/api/content/${id}`);
                            return current.pendingMetadataRefresh ? undefined : current;
                        },
                        60_000,
                        500,
                    );
                    expect(added).toMatchObject({ id, name: domain.title, contentItem: { fulfillment: 'Missing' } });
                    content.set(domain.contentType, added);
                }

                await armAllDownloadFixtures();
                await armIndexerFixtures();
            });

            await test.step('a grab the client refuses is recorded as a failed download with its reason', async () => {
                const contentId = content.get(refused.contentType)!.id;
                const fulfillmentBefore = (await api.get<ContentItem>(`/api/content/${contentId}`)).contentItem!
                    .fulfillment;
                await mocks.indexer.useScenario(indexerScenario);
                await mocks.downloadClient.useScenario(downloadScenario);
                await mocks.downloadClient.write(`DownloadReleaseAsync-${refused.releaseExternalId}.json`, {
                    externalId: refused.downloadExternalId,
                    status: refused.status,
                    statusReason: refused.statusReason,
                    progressPercent: refused.progressPercent,
                });

                refusedDownload = await grab(api, contentId, refused);

                expect(refusedDownload).toMatchObject({
                    externalId: refused.downloadExternalId,
                    status: 'Failed',
                    statusReason: 'The download client refused this release.',
                });
                expect(await api.get<ContentItem>(`/api/content/${contentId}`)).toMatchObject({
                    contentItem: { fulfillment: fulfillmentBefore },
                });

                await page.goto('/downloads?tab=history&hstate=Failed');
                const row = page.locator(`[data-row-id="${refusedDownload.id}"]`);
                await expect(row).toBeVisible();
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(`[data-action="view-details"][data-target="${refusedDownload.id}"]`).click();
                await expect(page.getByTestId('download-details-dialog')).toContainText(
                    'The download client refused this release.',
                );
                await page.keyboard.press('Escape');
            });

            await test.step('force-start a queued download', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                forceDownload = await grab(api, content.get(forceStarted.contentType)!.id, forceStarted);
                expect(forceDownload.status).toBe('Queued');

                await page.goto('/downloads');
                const row = page.locator(`[data-row-id="${forceDownload.id}"]`);
                await expect(row).toBeVisible();
                const response = page.waitForResponse(
                    (candidate) =>
                        candidate.url().endsWith(`/api/downloads/${forceDownload.id}/force-start`) &&
                        candidate.request().method() === 'POST',
                );
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(`[data-action="force-start"][data-target="${forceDownload.id}"]`).click();
                expect((await response).status()).toBe(204);

                await armStatus([forceStarted], 'Downloading', 0);
                await drainDownloadRefresh(api);
                expect(await api.get<Download>(`/api/downloads/${forceDownload.id}`)).toMatchObject({
                    status: 'Downloading',
                    progress: 0,
                });
                const calls = await mocks.downloadClient.calls<{ externalId: string }>('ForceStartDownloadAsync');
                expect(calls).toContainEqual(
                    expect.objectContaining({ request: expect.objectContaining({ externalId: forceStarted.downloadExternalId }) }),
                );
            });

            await test.step('a download that stops progressing is flagged as stuck', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                stuckDownload = await grab(api, content.get(stuck.contentType)!.id, stuck);
                expect(stuckDownload).toMatchObject({ status: 'Stalled', progress: 41 });
                expect(stuckDownload.lastProgressAt).toBeTruthy();

                const progressSnapshot = await api.get<Download>(`/api/downloads/${stuckDownload.id}`);
                expect(progressSnapshot.lastProgressAt).toBeTruthy();
                const thresholdAt = new Date(progressSnapshot.lastProgressAt!).getTime() + 60_250;
                const thresholdWaitMs = Math.max(2_000, thresholdAt - Date.now() + 2_000);
                await until(
                    'the one-minute stalled threshold to elapse',
                    async () => (Date.now() >= thresholdAt ? true : undefined),
                    thresholdWaitMs,
                    250,
                );
                let detectedDownload: Download | undefined;
                for (let attempt = 0; attempt < 2 && !detectedDownload; attempt++) {
                    const started = await api.post<RunJobResponse>('/api/jobs/execute-immediate', {
                        jobType: 'DownloadStuckDetection',
                        parameters: { stalledMinutes: '1' },
                    });
                    const execution =
                        started.status === 'Completed'
                            ? { id: started.executionId, jobType: 'DownloadStuckDetection', status: started.status }
                            : await waitForJob(api, started.executionId);
                    expect(execution.status).toBe('Completed');

                    try {
                        detectedDownload = await until(
                            'the stalled download to be marked stuck',
                            async () => {
                                const current = await api.get<Download>(`/api/downloads/${stuckDownload.id}`);
                                return current.status === 'Failed' ? current : undefined;
                            },
                            30_000,
                            250,
                        );
                    } catch (error) {
                        if (attempt === 1) {
                            throw error;
                        }
                    }
                }
                stuckDownload = detectedDownload!;
                expect(stuckDownload).toMatchObject({
                    status: 'Failed',
                    failureReason: 'DownloadStuck',
                    progress: 41,
                });
                expect(stuckDownload.statusReason).toMatch(/^No progress reported since .+ \(threshold 00:01:00\)\.$/);

                await page.goto('/downloads?tab=history&hstate=Failed');
                const row = page.locator(`[data-row-id="${stuckDownload.id}"]`);
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(`[data-action="view-details"][data-target="${stuckDownload.id}"]`).click();
                const details = page.getByTestId('download-details-dialog');
                await expect(details).toContainText('Stuck download');
                await expect(details).toContainText('No progress reported since');
                await page.keyboard.press('Escape');
            });

            await test.step('cancel one download and keep its files', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                const download = await grab(api, content.get(keepFiles.contentType)!.id, keepFiles);
                const landedFile = resolve(
                    integrationEnvironment.paths.downloads,
                    keepFiles.downloadExternalId,
                    'Queue Journey Book Keep.pdf',
                );
                await stat(landedFile);

                await api.post(`/api/downloads/${download.id}/cancel`);
                expect((await api.get<Download>(`/api/downloads/${download.id}`)).status).toBe('Cancelled');
                await api.delete(`/api/downloads/${download.id}`);
                await expect(api.get(`/api/downloads/${download.id}`)).rejects.toMatchObject({ status: 404 });
                await stat(landedFile);
            });

            await test.step('cancel another and take its files with it', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                const download = await grab(api, content.get(removeFiles.contentType)!.id, removeFiles);
                const landedFolder = resolve(
                    integrationEnvironment.paths.downloads,
                    removeFiles.downloadExternalId,
                );
                await stat(landedFolder);

                await api.post(`/api/downloads/${download.id}/cancel`);
                await api.delete(`/api/downloads/${download.id}/files`);
                await expect(api.get(`/api/downloads/${download.id}`)).rejects.toMatchObject({ status: 404 });
                await expect(stat(landedFolder)).rejects.toMatchObject({ code: 'ENOENT' });
                const calls = await mocks.downloadClient.calls<{ deleteFiles: boolean; downloadId: string }>(
                    'RemoveDownloadAsync',
                );
                expect(calls).toContainEqual(
                    expect.objectContaining({
                        request: expect.objectContaining({
                            deleteFiles: true,
                            downloadId: removeFiles.downloadExternalId,
                        }),
                    }),
                );
            });

            await test.step('a download whose import fails says why, and the item is not marked fulfilled', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                importFailedDownload = await grab(api, content.get(importFailure.contentType)!.id, importFailure);
                await armStatus([importFailure], 'Downloaded', 100);
                await drainDownloadRefresh(api);

                importFailedDownload = await until(
                    'the wrong-kind movie download to fail import',
                    async () => {
                        const current = await api.get<Download>(`/api/downloads/${importFailedDownload.id}`);
                        return current.status === 'Failed' ? current : undefined;
                    },
                    120_000,
                    500,
                );
                expect(importFailedDownload).toMatchObject({
                    status: 'Failed',
                    failureReason: 'NoUsableContent',
                    statusReason:
                        'No usable content: the download contained no files matching the accepted file types for ' +
                        'content type movie.',
                });
                expect(await api.get<ContentItem>(`/api/content/${content.get('movie')!.id}`)).toMatchObject({
                    contentItem: { fulfillment: 'Missing', mediaFiles: [] },
                });

                await page.goto('/downloads?tab=history&hstate=Failed');
                const row = page.locator(`[data-row-id="${importFailedDownload.id}"]`);
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(
                    `[data-action="view-details"][data-target="${importFailedDownload.id}"]`,
                ).click();
                const details = page.getByTestId('download-details-dialog');
                await expect(details).toContainText('No usable content');
                await expect(details).toContainText('content type movie');
                await page.keyboard.press('Escape');
            });

            await test.step('select the whole queue and pause, resume, retry, cancel, stop and delete it', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                const active = await Promise.all(
                    bulkDownloads.map((fixture) => grab(api, content.get(fixture.contentType)!.id, fixture)),
                );
                const activeFixtures = [forceStarted, ...bulkDownloads];
                const activeIds = [forceDownload.id, ...active.map((download) => download.id)];
                const activeExternalIds = activeFixtures.map((fixture) => fixture.downloadExternalId).sort();

                await page.goto('/downloads');
                const panel = page.locator('[role="tabpanel"][data-state="active"]');
                await panel.locator('[data-action="select-all"]').click();
                const bulkBar = panel.locator('[data-component="downloads-bulk-bar-queue"]');
                await bulkBar.getByRole('button', { name: `Select all ${activeIds.length} across all pages` }).click();
                await expect(bulkBar).toContainText(`${activeIds.length} selected`);
                await mocks.downloadClient.clearCalls();
                const pauseResponse = page.waitForResponse(
                    (response) =>
                        response.url().includes('/api/downloads/bulk/pause') &&
                        response.request().method() === 'POST',
                );
                await bulkBar.locator('[data-action="bulk-pause"]').click();
                const pauseResult = await pauseResponse;
                expect(pauseResult.ok()).toBe(true);
                expect(((await pauseResult.json()) as BulkResult).count).toBe(27);
                await armStatus(activeFixtures, 'Paused', 0);
                await drainDownloadRefresh(api);
                const pauseCalls = await mocks.downloadClient.calls<{ externalId: string }>('PauseDownloadAsync');
                expect(pauseCalls.map((call) => call.request.externalId).sort()).toEqual(activeExternalIds);
                expect(
                    (await Promise.all(activeIds.map((id) => api.get<Download>(`/api/downloads/${id}`)))).map(
                        (download) => download.status,
                    ),
                ).toEqual(activeIds.map(() => 'Paused'));

                await panel.locator('[data-action="select-all"]').click();
                await bulkBar.getByRole('button', { name: `Select all ${activeIds.length} across all pages` }).click();
                await expect(bulkBar).toContainText(`${activeIds.length} selected`);
                await mocks.downloadClient.clearCalls();
                const resumeResponse = page.waitForResponse(
                    (response) =>
                        response.url().includes('/api/downloads/bulk/resume') &&
                        response.request().method() === 'POST',
                );
                await bulkBar.locator('[data-action="bulk-resume"]').click();
                const resumeResult = await resumeResponse;
                expect(resumeResult.ok()).toBe(true);
                expect(((await resumeResult.json()) as BulkResult).count).toBe(27);
                await armStatus(activeFixtures, 'Downloading', 0);
                await drainDownloadRefresh(api);
                const resumeCalls = await mocks.downloadClient.calls<{ externalId: string }>('ResumeDownloadAsync');
                expect(resumeCalls.map((call) => call.request.externalId).sort()).toEqual(activeExternalIds);
                expect(
                    (await Promise.all(activeIds.map((id) => api.get<Download>(`/api/downloads/${id}`)))).map(
                        (download) => download.status,
                    ),
                ).toEqual(activeIds.map(() => 'Downloading'));

                const knownAutoGrabIds = new Set(
                    (await api.get<Paged<JobExecution>>('/api/jobs?jobType=ReleaseAutoGrabItem&pageSize=250')).items.map(
                        (execution) => execution.id,
                    ),
                );
                const knownDownloadJobIds = new Set(
                    (await api.get<Paged<JobExecution>>('/api/jobs?jobType=ReleaseDownloadItem&pageSize=250')).items.map(
                        (execution) => execution.id,
                    ),
                );
                const knownDownloadIds = new Set(
                    (await api.get<Paged<Download>>('/api/downloads?pageSize=250')).items.map((download) => download.id),
                );
                expect(
                    await api.post<BulkResult>('/api/downloads/bulk/retry', {
                        ids: [refusedDownload.id, stuckDownload.id],
                    }),
                ).toEqual({ count: 2, error: null });
                expect((await api.get<Download>(`/api/downloads/${refusedDownload.id}`)).status).toBe('Cancelled');
                expect((await api.get<Download>(`/api/downloads/${stuckDownload.id}`)).status).toBe('Cancelled');

                const autoGrabs = await until(
                    'retried downloads auto-grab executions to become terminal',
                    async () => {
                        const executions = await api.get<Paged<JobExecution>>(
                            '/api/jobs?jobType=ReleaseAutoGrabItem&pageSize=250',
                        );
                        const fresh = executions.items.filter((execution) => !knownAutoGrabIds.has(execution.id));
                        return fresh.length === 2 &&
                            fresh.every((execution) => ['Completed', 'Failed', 'Cancelled'].includes(execution.status))
                            ? fresh
                            : undefined;
                    },
                    120_000,
                    250,
                );
                expect(autoGrabs.every((execution) => execution.status === 'Completed')).toBe(true);

                const downloadJobs = (
                    await api.get<Paged<JobExecution>>('/api/jobs?jobType=ReleaseDownloadItem&pageSize=250')
                ).items.filter((execution) => !knownDownloadJobIds.has(execution.id));
                const completedDownloadJobs = await Promise.all(
                    downloadJobs.map((execution) =>
                        ['Completed', 'Failed', 'Cancelled'].includes(execution.status)
                            ? execution
                            : waitForJob(api, execution.id),
                    ),
                );
                expect(completedDownloadJobs.every((execution) => execution.status === 'Completed')).toBe(true);
                const retryDownloadIds = (
                    await api.get<Paged<Download>>('/api/downloads?pageSize=250')
                ).items.filter((download) => !knownDownloadIds.has(download.id)).map((download) => download.id);

                expect(
                    await api.post<BulkResult>('/api/downloads/bulk/cancel', { ids: [activeIds[0]!] }),
                ).toEqual({ count: 1, error: null });
                expect((await api.get<Download>(`/api/downloads/${activeIds[0]}`)).status).toBe('Cancelled');

                await page.goto('/downloads');
                await panel.locator('[data-action="select-all"]').click();
                await bulkBar
                    .getByRole('button', { name: `Select all ${activeIds.length - 1} across all pages` })
                    .click();
                await expect(bulkBar).toContainText(`${activeIds.length - 1} selected`);
                const stopResponse = page.waitForResponse(
                    (response) =>
                        response.url().includes('/api/downloads/bulk/stop') &&
                        response.request().method() === 'POST',
                );
                await bulkBar.locator('[data-action="bulk-stop"]').click();
                expect((await stopResponse).ok()).toBe(true);
                expect(
                    (await Promise.all(activeIds.slice(1).map((id) => api.get<Download>(`/api/downloads/${id}`)))).map(
                        (download) => download.status,
                    ),
                ).toEqual(activeIds.slice(1).map(() => 'Cancelled'));

                const deleteIds = [
                    ...activeIds,
                    refusedDownload.id,
                    stuckDownload.id,
                    ...retryDownloadIds,
                ];
                expect(
                    await api.post<BulkResult>('/api/downloads/bulk/delete', {
                        ids: deleteIds,
                    }),
                ).toEqual({ count: deleteIds.length, error: null });

                for (const id of deleteIds) {
                    await expect(api.get(`/api/downloads/${id}`)).rejects.toMatchObject({ status: 404 });
                }
            });

            await test.step('completed downloads are cleaned out of the client per policy', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                await mocks.downloadClient.useScenario(downloadScenario);
                await mocks.downloadClient.armRemove(true);
                expect((await runImmediateJob(api, 'DownloadCleanup')).status).toBe('Completed');
                await mocks.downloadClient.clearCalls();

                const download = await grab(api, content.get(cleanup.contentType)!.id, cleanup);
                await armStatus([cleanup], 'Downloaded', 100);
                await drainDownloadRefresh(api);
                await until(
                    'the cleanup-policy download to import',
                    async () => {
                        const current = await api.get<Download>(`/api/downloads/${download.id}`);
                        return current.status === 'Imported' ? current : undefined;
                    },
                    120_000,
                    500,
                );
                const execution = await runImmediateJob(api, 'DownloadCleanup');
                expect(execution.status).toBe('Completed');

                const calls = await mocks.downloadClient.waitForCalls<{
                    deleteFiles: boolean;
                    downloadId: string;
                }>(1, {
                    method: 'RemoveDownloadAsync',
                    predicate: (call) => call.request.downloadId === cleanup.downloadExternalId,
                });
                expect(calls).toContainEqual(
                    expect.objectContaining({
                        request: expect.objectContaining({
                            deleteFiles: true,
                            downloadId: cleanup.downloadExternalId,
                        }),
                    }),
                );
            });

            await test.step('the queue and history tabs filter and page independently', async () => {
                const movieId = content.get('movie')!.id;
                await mocks.indexer.useScenario(indexerScenario);
                const pagingFixtures = [...pagingQueue, ...pagingHistory];
                for (const batch of [pagingQueue, pagingHistory]) {
                    const releases = batch.map(releaseFor);
                    await mocks.indexer.armSearch('Queue Journey Movie', releases);
                    await mocks.indexer.armSearchFallback(releases);
                    const candidates = await api.post<InteractiveCandidate[]>(
                        `/api/interactive-search/${movieId}`,
                    );
                    const byExternalId = new Map(
                        candidates.map((candidate) => [candidate.release.externalId, candidate]),
                    );
                    await Promise.all(
                        batch.map(async (fixture) => {
                            const candidate = byExternalId.get(fixture.releaseExternalId);
                            expect(candidate).toBeDefined();
                            await api.post(`/api/interactive-search/${movieId}/grab`, candidate!.release);
                        }),
                    );
                }

                await until(
                    'all paging downloads to be persisted',
                    async () => {
                        const downloads = await api.get<Paged<Download>>('/api/downloads?pageSize=250');
                        const ids = new Set(downloads.items.map((download) => download.externalId));
                        return pagingFixtures.every((fixture) => ids.has(fixture.downloadExternalId))
                            ? true
                            : undefined;
                    },
                    180_000,
                    500,
                );

                await page.goto('/downloads');
                let panel = page.locator('[role="tabpanel"][data-state="active"]');
                await panel.getByRole('button', { name: 'Filter by Status' }).click();
                await page.getByRole('menuitemradio', { name: 'Queued', exact: true }).click();
                await expect(panel.locator('[data-component="pager"]')).toContainText('Page 1 of 2');
                await panel.locator('[data-action="next-page"]').click();
                await expect(panel.locator('[data-component="pager"]')).toContainText('Page 2 of 2');
                await expect.poll(() => new URL(page.url()).searchParams.get('qstate')).toBe('Queued');
                await expect.poll(() => new URL(page.url()).searchParams.get('qpage')).toBe('2');

                await page.getByRole('tab', { name: 'History' }).click();
                panel = page.locator('[role="tabpanel"][data-state="active"]');
                await panel.getByRole('button', { name: 'Filter by Status' }).click();
                await page.getByRole('menuitemradio', { name: 'Failed', exact: true }).click();
                await expect(panel.locator('[data-component="pager"]')).toContainText('Page 1 of 2');
                await panel.locator('[data-action="next-page"]').click();
                await expect(panel.locator('[data-component="pager"]')).toContainText('Page 2 of 2');

                const historyParams = new URL(page.url()).searchParams;
                expect(historyParams.get('qstate')).toBe('Queued');
                expect(historyParams.get('qpage')).toBe('2');
                expect(historyParams.get('hstate')).toBe('Failed');
                expect(historyParams.get('hpage')).toBe('2');

                await page.getByRole('tab', { name: 'Active' }).click();
                panel = page.locator('[role="tabpanel"][data-state="active"]');
                await expect(panel.locator('[data-component="pager"]')).toContainText('Page 2 of 2');
                await expect(panel.getByRole('button', { name: 'Status filtered, edit filter' })).toBeVisible();

                await page.getByRole('tab', { name: 'History' }).click();
                panel = page.locator('[role="tabpanel"][data-state="active"]');
                await expect(panel.locator('[data-component="pager"]')).toContainText('Page 2 of 2');
                await expect(panel.getByRole('button', { name: 'Status filtered, edit filter' })).toBeVisible();
            });
        } finally {
            const cleanupResults: PromiseSettledResult<unknown>[] = [];
            cleanupResults.push(
                ...(await Promise.allSettled([
                    mocks.downloadClient.useScenario('default'),
                    mocks.indexer.useScenario('default'),
                    mocks.hierarchyPrimary.useScenario('default'),
                ])),
            );
            cleanupResults.push(
                ...(await Promise.allSettled([
                    rm(resolve(FIXTURES_ROOT, mocks.downloadClient.slug, 'scenarios', downloadScenario), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(FIXTURES_ROOT, mocks.indexer.slug, 'scenarios', indexerScenario), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(FIXTURES_ROOT, mocks.hierarchyPrimary.slug, 'scenarios', metadataScenario), {
                        recursive: true,
                        force: true,
                    }),
                    ...allDownloadFixtures.map((fixture) =>
                        rm(resolve(integrationEnvironment.paths.downloads, fixture.downloadExternalId), {
                            recursive: true,
                            force: true,
                        }),
                    ),
                ])),
            );
            expect(
                cleanupResults
                    .filter((result) => result.status === 'rejected')
                    .map((result) => String((result as PromiseRejectedResult).reason)),
            ).toEqual([]);
        }
    });
});

function downloadFixture(
    contentType: string,
    slug: string,
    releaseTitle: string,
    status: string,
    publishDate: string,
    statusReason?: string,
    progressPercent = 0,
): DownloadFixture {
    return {
        contentType,
        releaseTitle,
        releaseExternalId: `journey-09-release-${slug}`,
        downloadExternalId: `journey-09-download-${slug}`,
        status,
        statusReason,
        progressPercent,
        publishDate,
    };
}

function releaseFor(fixture: DownloadFixture): ReleaseFixture {
    return {
        title: fixture.releaseTitle,
        protocol: 'torrent',
        externalId: fixture.releaseExternalId,
        infoUrl: `https://indexer.journey.invalid/releases/${fixture.releaseExternalId}`,
        size: 734_003_200,
        publishDate: fixture.publishDate,
        categories: domains.find((domain) => domain.contentType === fixture.contentType)!.categories,
        languages: ['en'],
    };
}

async function armMetadataFixtures(): Promise<void> {
    await mocks.hierarchyPrimary.useScenario(metadataScenario);
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
        providerType: mocks.hierarchyPrimary.slug,
        source: 'journey-09',
        language: 'en',
        publishDate: '2026-01-01T00:00:00Z',
    });
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', []);
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', []);
    for (const domain of domains) {
        await mocks.hierarchyPrimary.write(`tryGetMetadataAsync-${domain.externalId}.json`, {
            providerType: mocks.hierarchyPrimary.slug,
            source: 'journey-09',
            title: domain.title,
            overview: `${domain.title} queue triage fixture.`,
            language: 'en',
            publishDate: '2026-01-01T00:00:00Z',
        });
        await mocks.hierarchyPrimary.write(`hydrateAsync-${domain.externalId}.json`, {
            providerType: mocks.hierarchyPrimary.slug,
            externalIdSource: 'journey',
            externalId: domain.externalId,
            contentType: domain.contentType,
            level: domain.level,
            title: domain.title,
            year: 2026,
            releaseDate: '2026-01-01T00:00:00Z',
            overview: `${domain.title} queue triage fixture.`,
            language: 'en',
        });
    }
}

async function armIndexerFixtures(): Promise<void> {
    await mocks.indexer.useScenario(indexerScenario);
    for (const domain of domains) {
        await mocks.indexer.armSearch(
            domain.title,
            allDownloadFixtures.filter((fixture) => fixture.contentType === domain.contentType).map(releaseFor),
        );
    }
    await mocks.indexer.armSearchFallback(allDownloadFixtures.map(releaseFor));
    await mocks.indexer.clearCalls();
}

async function armAllDownloadFixtures(): Promise<void> {
    await mocks.downloadClient.useScenario(downloadScenario);
    for (const fixture of allDownloadFixtures) {
        await mocks.downloadClient.write(`DownloadReleaseAsync-${fixture.releaseExternalId}.json`, {
            externalId: fixture.downloadExternalId,
            status: fixture.status,
            statusReason: fixture.statusReason,
            progressPercent: fixture.progressPercent,
            land: fixture.land,
        });
        await mocks.downloadClient.armStatus(fixture.downloadExternalId, [
            {
                externalId: fixture.downloadExternalId,
                status: fixture.status,
                statusReason: fixture.statusReason,
                progressPercent: fixture.progressPercent,
            },
        ]);
    }
    for (const method of [
        'PauseDownloadAsync.json',
        'ResumeDownloadAsync.json',
        'ForceStartDownloadAsync.json',
    ]) {
        await mocks.downloadClient.write(method, true);
    }
    await mocks.downloadClient.armRemove(true);
    await mocks.downloadClient.clearCalls();
}

async function armStatus(
    fixtures: DownloadFixture[],
    status: string,
    progressPercent: number,
    statusReason?: string,
): Promise<void> {
    await mocks.downloadClient.useScenario(downloadScenario);
    for (const fixture of fixtures) {
        await mocks.downloadClient.armStatus(fixture.downloadExternalId, [
            {
                externalId: fixture.downloadExternalId,
                status,
                statusReason,
                progressPercent,
            },
        ]);
    }
}

async function grab(
    api: ApiClient,
    contentId: string,
    fixture: DownloadFixture,
): Promise<Download> {
    const candidates = await api.post<InteractiveCandidate[]>(`/api/interactive-search/${contentId}`);
    const candidate = candidates.find((item) => item.release.externalId === fixture.releaseExternalId);
    if (!candidate) {
        throw new Error(`Release ${fixture.releaseExternalId} was not returned for ${fixture.releaseTitle}.`);
    }

    await api.post(`/api/interactive-search/${contentId}/grab`, candidate.release);
    return until(
        `${fixture.downloadExternalId} download row`,
        async () => {
            const downloads = await api.get<Paged<Download>>('/api/downloads?pageSize=250');
            return downloads.items.find((item) => item.externalId === fixture.downloadExternalId);
        },
        30_000,
        250,
    );
}
