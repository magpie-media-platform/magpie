import { rm, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { FIXTURES_ROOT } from '../helpers/fixtures';
import { drainDownloadRefresh, runImmediateJob, type JobExecution } from '../helpers/jobs';
import { mocks, type ReleaseFixture } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface RootFolder {
    id: string;
    name: string;
    defaults: Record<string, { qualityTierListId: string }>;
}

interface ContentItem {
    id: string;
    name: string;
    monitoring: 'Unmonitored' | 'Monitored' | 'Shotgun' | null;
    effectiveMonitoring: 'Unmonitored' | 'Monitored' | 'Shotgun';
    pendingMetadataRefresh: boolean;
    contentItem?: {
        id: string;
        fulfillment: string;
        qualityStatus?: string | null;
        mediaFiles: Array<{ filePath: string }>;
    } | null;
}

interface Download {
    id: string;
    externalId: string;
    failureReason?: string | null;
    progress?: number | null;
    status: string;
    title: string;
}

interface InteractiveCandidate {
    release: {
        externalId?: string;
        title: string;
        [key: string]: unknown;
    };
}

interface JourneyJobExecution extends JobExecution {
    eventTriggerId?: string | null;
    parameters: Record<string, string>;
    triggerContext: Record<string, string>;
}

interface JourneyMovie {
    externalId: string;
    monitored: boolean;
    releaseExternalId: string;
    releaseTitle: string;
    title: string;
}

const movies: JourneyMovie[] = [
    {
        title: 'Unattended Journey North',
        externalId: 'journey-07-north',
        monitored: true,
        releaseTitle: 'Unattended Journey North 2026 1080p BluRay',
        releaseExternalId: 'journey-07-release-north',
    },
    {
        title: 'Unattended Journey South',
        externalId: 'journey-07-south',
        monitored: true,
        releaseTitle: 'Unattended Journey South 2026 1080p BluRay',
        releaseExternalId: 'journey-07-release-south',
    },
    {
        title: 'Unattended Journey West',
        externalId: 'journey-07-west',
        monitored: false,
        releaseTitle: 'Unattended Journey West 2026 1080p BluRay',
        releaseExternalId: 'journey-07-release-west',
    },
];

const acquisitionScenario = 'journey-07-acquisition';
const metadataScenario = 'journey-07-metadata';

test.describe.serial('Journey 7: set it and forget it', () => {
    test('runs unattended acquisition and manages the second wave', async ({ page }) => {
        test.setTimeout(600_000);

        let api!: ApiClient;
        let movieRoot!: RootFolder;
        let initialMediaRefreshJobIds!: Set<string>;
        const contentByExternalId = new Map<string, ContentItem>();
        let firstDownload!: Download;
        let failedDownload!: Download;
        let retriedDownload!: Download;
        let completedSearchJobs!: JourneyJobExecution[];

        try {
            await test.step('restore the configured baseline and sign in', async () => {
                api = await restoreCheckpoint('configured-baseline', async (restored) => {
                    movieRoot = (await restored.get<RootFolder[]>('/api/root-folders')).find(
                        (root) => root.name === 'Movies',
                    )!;
                    expect(movieRoot.defaults['movie']?.qualityTierListId).toBeTruthy();
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
                });
                initialMediaRefreshJobIds = new Set(
                    (await api.get<Paged<JobExecution>>('/api/jobs?jobType=MediaServerRefresh&pageSize=250')).items.map(
                        (job) => job.id,
                    ),
                );
                await loginUi(page);
            });

            await test.step('add three missing movies, monitoring only the first two', async () => {
                await armMovieMetadata();
                for (const movie of movies) {
                    await armMovieSearch(movie);
                    await page.goto('/library/add');
                    await page.getByRole('combobox', { name: 'Content Type' }).click();
                    await page.getByRole('option', { name: 'Movie', exact: true }).click();
                    await page.locator('#sfb-title').fill(movie.title);
                    await page.getByRole('button', { name: /^search$/i }).click();

                    const row = page.getByRole('grid').getByRole('row').filter({ hasText: movie.title });
                    await expect(row).toBeVisible();
                    await row.getByRole('button', { name: movie.title, exact: true }).click();
                    const preview = page.getByTestId('preview-drawer');
                    await expect(preview).toContainText(`${movie.title} provider overview.`);

                    const addedResponse = page.waitForResponse(
                        (response) =>
                            response.url().includes('/api/content/from-external-id') &&
                            response.request().method() === 'POST',
                    );
                    await page.route(
                        /\/api\/content\/from-external-id(?:\?|$)/,
                        async (route) => {
                            const url = new URL(route.request().url());
                            url.searchParams.set('monitoring', String(movie.monitored));
                            url.searchParams.set('rootFolderId', movieRoot.id);
                            url.searchParams.set(
                                'qualityTierListId',
                                movieRoot.defaults['movie']!.qualityTierListId,
                            );
                            await route.continue({ url: url.toString() });
                        },
                        { times: 1 },
                    );
                    await preview.locator('[data-action="preview-drawer-add"]').click();
                    const response = await addedResponse;
                    expect(response.ok()).toBe(true);
                    const id = (await response.json()) as string;

                    const hydrated = await until(
                        `${movie.title} metadata refresh`,
                        async () => {
                            const current = await api.get<ContentItem>(`/api/content/${id}`);
                            return !current.pendingMetadataRefresh ? current : undefined;
                        },
                        60_000,
                        500,
                    );
                    await api.patch(`/api/content/${id}/metadata`, {
                        title: { value: movie.title },
                        overview: { value: `${movie.title} provider overview.` },
                    });
                    const configured = await api.get<ContentItem>(`/api/content/${id}`);
                    expect(configured).toMatchObject({
                        id: hydrated.id,
                        monitoring: movie.monitored ? 'Monitored' : 'Unmonitored',
                        effectiveMonitoring: movie.monitored ? 'Monitored' : 'Unmonitored',
                        contentItem: { fulfillment: 'Missing' },
                    });
                    expect(await api.get(`/api/content/${id}/metadata`)).toMatchObject({
                        title: movie.title,
                        overview: `${movie.title} provider overview.`,
                    });
                    contentByExternalId.set(movie.externalId, configured);
                }
                await armMovieMetadata();
            });

            await test.step('arm unattended acquisition for each movie', async () => {
                await mocks.downloadClient.useScenario(acquisitionScenario);
                for (const movie of movies.slice(0, 2)) {
                    await mocks.downloadClient.write(`DownloadReleaseAsync-${movie.releaseExternalId}.json`, {
                        externalId: '',
                        status: 'Queued',
                        progressPercent: 0,
                        land: [
                            {
                                source: 'movie-720p.mkv',
                                target: `${movie.title}/${movie.title}.mkv`,
                            },
                        ],
                    });
                }
                await mocks.downloadClient.armStatus(movies[0]!.releaseExternalId, [
                    { externalId: movies[0]!.releaseExternalId, status: 'Downloading', progressPercent: 35 },
                ]);
                await mocks.downloadClient.armStatus(movies[1]!.releaseExternalId, [
                    { externalId: movies[1]!.releaseExternalId, status: 'Downloading', progressPercent: 40 },
                ]);
                await mocks.downloadClient.armRemove(true);
                await mocks.downloadClient.write('PauseDownloadAsync.json', true);
                await mocks.downloadClient.write('ResumeDownloadAsync.json', true);
                await mocks.downloadClient.clearCalls();

                await mocks.mediaServer.useScenario(acquisitionScenario);
                await mocks.mediaServer.armRefresh(true);
                await mocks.mediaServer.clearCalls();

                await mocks.indexer.useScenario(acquisitionScenario);
                for (const movie of movies) {
                    await mocks.indexer.armSearch(movie.title, [releaseFixture(movie)]);
                }
                await mocks.indexer.armSearchFallback(movies.map(releaseFixture));
                await mocks.indexer.clearCalls();
            });

            await test.step('the scheduled release search scan finds candidates for monitored items only', async () => {
                const monitoredIds = movies
                    .filter((movie) => movie.monitored)
                    .map((movie) => contentByExternalId.get(movie.externalId)!.id);
                const before = await api.get<Paged<JourneyJobExecution>>(
                    '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                );
                const knownIds = new Set(before.items.map((job) => job.id));

                expect((await runImmediateJob(api, 'ReleaseSearchScan')).status).toBe('Completed');

                let completed: Paged<JourneyJobExecution>;
                try {
                    completed = await until(
                        'monitored release search jobs to complete',
                        async () => {
                            const jobs = await api.get<Paged<JourneyJobExecution>>(
                                '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                            );
                            return monitoredIds.every((id) =>
                                jobs.items.some(
                                    (job) =>
                                        !knownIds.has(job.id) &&
                                        job.parameters?.['contentHierarchyItemId'] === id &&
                                        job.status === 'Completed',
                                ),
                            )
                                ? jobs
                                : undefined;
                        },
                        10_000,
                        500,
                    );
                } catch {
                    expect((await runImmediateJob(api, 'ReleaseSearchScan')).status).toBe('Completed');
                    completed = await until(
                        'monitored release search jobs to complete after one retry',
                        async () => {
                            const jobs = await api.get<Paged<JourneyJobExecution>>(
                                '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                            );
                            return monitoredIds.every((id) =>
                                jobs.items.some(
                                    (job) =>
                                        !knownIds.has(job.id) &&
                                        job.parameters?.['contentHierarchyItemId'] === id &&
                                        job.status === 'Completed',
                                ),
                            )
                                ? jobs
                                : undefined;
                        },
                        120_000,
                        500,
                    );
                }

                completedSearchJobs = completed.items.filter(
                    (job) =>
                        !knownIds.has(job.id) &&
                        monitoredIds.includes(job.parameters['contentHierarchyItemId']!) &&
                        job.status === 'Completed',
                );

                const after = await api.get<Paged<JourneyJobExecution>>(
                    '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                );
                const newJobs = after.items.filter((job) => !knownIds.has(job.id));
                expect(
                    newJobs.some(
                        (job) =>
                            job.parameters?.['contentHierarchyItemId'] ===
                            contentByExternalId.get(movies[2]!.externalId)!.id,
                    ),
                ).toBe(false);
            });

            await test.step('auto-grab starts a download without any human action', async () => {
                const downloads = await until(
                    'two automatic downloads to appear',
                    async () => {
                        const current = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                        const found = movies
                            .slice(0, 2)
                            .map((movie) => current.items.find((item) => item.externalId === movie.releaseExternalId));
                        return found.every(Boolean) ? (found as Download[]) : undefined;
                    },
                    120_000,
                    500,
                );
                firstDownload = downloads[0]!;
                failedDownload = downloads[1]!;
                for (const download of downloads) {
                    expect(['Queued', 'Downloading']).toContain(download.status);
                }
                expect(
                    (await mocks.downloadClient.calls('DownloadReleaseAsync')).map(
                        (call) => (call.request as { externalId: string }).externalId,
                    ),
                ).toEqual(expect.arrayContaining([movies[0]!.releaseExternalId, movies[1]!.releaseExternalId]));

                const autoGrabJobs = await until(
                    'automatic grab executions to complete',
                    async () => {
                        const jobs = await api.get<Paged<JourneyJobExecution>>(
                            '/api/jobs?jobType=ReleaseAutoGrabItem&pageSize=250',
                        );
                        const found = movies.slice(0, 2).map((movie) =>
                            jobs.items.find(
                                (job) =>
                                    job.parameters?.['contentItemId'] ===
                                        contentByExternalId.get(movie.externalId)!.contentItem!.id &&
                                    completedSearchJobs.some((search) => search.id === job.parentExecutionId) &&
                                    job.status === 'Completed',
                            ),
                        );
                        return found.every(Boolean) ? (found as JourneyJobExecution[]) : undefined;
                    },
                    120_000,
                    500,
                );
                expect(autoGrabJobs).toHaveLength(2);

                const allDownloads = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                expect(allDownloads.items.some((item) => item.externalId === movies[2]!.releaseExternalId)).toBe(false);
            });

            await test.step('pause the first download from the queue, then resume it', async () => {
                await page.goto('/downloads');
                const row = page.locator(`[data-row-id="${firstDownload.id}"]`);
                await expect(row).toBeVisible();

                const pauseResponse = page.waitForResponse(
                    (response) =>
                        response.url().endsWith(`/api/downloads/${firstDownload.id}/pause`) &&
                        response.request().method() === 'POST',
                );
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(`[data-action="pause"][data-target="${firstDownload.id}"]`).click();
                expect((await pauseResponse).status()).toBe(204);
                await mocks.downloadClient.armStatus(movies[0]!.releaseExternalId, [
                    { externalId: movies[0]!.releaseExternalId, status: 'Paused', progressPercent: 35 },
                ]);
                await drainDownloadRefresh(api);
                await expect(row).toContainText('Paused');
                expect((await api.get<Download>(`/api/downloads/${firstDownload.id}`)).status).toBe('Paused');

                const resumeResponse = page.waitForResponse(
                    (response) =>
                        response.url().endsWith(`/api/downloads/${firstDownload.id}/resume`) &&
                        response.request().method() === 'POST',
                );
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(`[data-action="resume"][data-target="${firstDownload.id}"]`).click();
                expect((await resumeResponse).status()).toBe(204);
                await mocks.downloadClient.armStatus(movies[0]!.releaseExternalId, [
                    { externalId: movies[0]!.releaseExternalId, status: 'Downloading', progressPercent: 35 },
                ]);
                await drainDownloadRefresh(api);
                await expect(row).toContainText('Downloading');
                expect((await api.get<Download>(`/api/downloads/${firstDownload.id}`)).status).toBe('Downloading');

                expect(await mocks.downloadClient.calls('PauseDownloadAsync')).toHaveLength(1);
                expect(await mocks.downloadClient.calls('ResumeDownloadAsync')).toHaveLength(1);

                await mocks.downloadClient.armStatus(movies[0]!.releaseExternalId, [
                    { externalId: movies[0]!.releaseExternalId, status: 'Downloaded', progressPercent: 100 },
                ]);
                await drainDownloadRefresh(api);
            });

            await test.step('the second download fails mid-transfer and is retried to completion', async () => {
                const knownCleanupIds = new Set(
                    (await api.get<Paged<JobExecution>>('/api/jobs?jobType=DownloadCleanup&pageSize=250')).items.map(
                        (job) => job.id,
                    ),
                );
                await drainDownloadRefresh(api);
                expect(await api.get<Download>(`/api/downloads/${failedDownload.id}`)).toMatchObject({
                    status: 'Downloading',
                    progress: 40,
                });

                await mocks.downloadClient.armStatus(movies[1]!.releaseExternalId, [
                    {
                        externalId: movies[1]!.releaseExternalId,
                        status: 'Failed',
                        statusReason: 'Transfer interrupted',
                        progressPercent: 40,
                    },
                ]);
                await drainDownloadRefresh(api);
                const failed = await until(
                    'the second automatic download to fail',
                    async () => {
                        const current = await api.get<Download>(`/api/downloads/${failedDownload.id}`);
                        return current.status === 'Faulted' ? current : undefined;
                    },
                    30_000,
                    250,
                );
                expect(failed).toMatchObject({
                    status: 'Faulted',
                    failureReason: 'DownloadErrored',
                    progress: 40,
                });

                const cleanup = await until(
                    'failed download cleanup to become terminal',
                    async () => {
                        const jobs = await api.get<Paged<JourneyJobExecution>>(
                            '/api/jobs?jobType=DownloadCleanup&pageSize=250',
                        );
                        return jobs.items.find(
                            (job) =>
                                !knownCleanupIds.has(job.id) &&
                                ['Completed', 'Failed', 'Cancelled'].includes(job.status),
                        );
                    },
                    120_000,
                    500,
                );
                expect(cleanup.status).toBe('Completed');

                await mocks.downloadClient.armStatus(movies[1]!.releaseExternalId, [
                    { externalId: movies[1]!.releaseExternalId, status: 'Downloading', progressPercent: 40 },
                ]);
                await api.post(`/api/downloads/${failedDownload.id}/retry`);

                retriedDownload = await until(
                    'retried automatic download to appear',
                    async () => {
                        const current = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                        return current.items.find(
                            (item) =>
                                item.externalId === movies[1]!.releaseExternalId && item.id !== failedDownload.id,
                        );
                    },
                    120_000,
                    500,
                );
                await drainDownloadRefresh(api);
                expect(await api.get<Download>(`/api/downloads/${retriedDownload.id}`)).toMatchObject({
                    status: 'Downloading',
                    progress: 40,
                });

                await mocks.downloadClient.armStatus(movies[1]!.releaseExternalId, [
                    { externalId: movies[1]!.releaseExternalId, status: 'Downloaded', progressPercent: 100 },
                ]);
                await drainDownloadRefresh(api);
                const importedRetry = await until(
                    'retried automatic download to import',
                    async () => {
                        const current = await api.get<Download>(`/api/downloads/${retriedDownload.id}`);
                        return current.status === 'Imported' ? current : undefined;
                    },
                    120_000,
                    500,
                );
                expect(importedRetry.progress).toBe(100);
                expect((await api.get<Download>(`/api/downloads/${failedDownload.id}`)).status).toBe('Cancelled');
            });

            await test.step('cancel a stray download and remove its files from disk', async () => {
                const strayRelease = {
                    title: 'Unattended Journey North 2026 720p WEB-DL Stray',
                    protocol: 'torrent',
                    externalId: 'journey-07-stray',
                    infoUrl: 'https://indexer.journey.invalid/releases/journey-07-stray',
                    size: 734_003_200,
                    publishDate: '2026-02-03T00:00:00Z',
                    categories: ['movies'],
                    languages: ['en'],
                };
                await mocks.indexer.armSearch(movies[0]!.title, [strayRelease]);
                await mocks.indexer.armSearchFallback([strayRelease]);
                const { candidates } = await api.post<{ candidates: InteractiveCandidate[] }>(
                    `/api/interactive-search/${contentByExternalId.get(movies[0]!.externalId)!.id}`,
                );
                const candidate = candidates.find((item) => item.release.externalId === strayRelease.externalId);
                expect(candidate).toBeDefined();

                await mocks.downloadClient.armGrab({
                    externalId: strayRelease.externalId,
                    status: 'Queued',
                    progressPercent: 0,
                    land: [
                        {
                            source: 'movie-720p.mkv',
                            target: 'Unattended Journey North Stray/Unattended Journey North Stray.mkv',
                        },
                    ],
                });
                await api.post(
                    `/api/interactive-search/${contentByExternalId.get(movies[0]!.externalId)!.id}/grab`,
                    candidate?.release,
                );
                const stray = await until(
                    'stray download to appear',
                    async () => {
                        const current = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                        return current.items.find((item) => item.externalId === strayRelease.externalId);
                    },
                    30_000,
                    250,
                );
                const strayFolder = resolve(integrationEnvironment.paths.downloads, strayRelease.externalId);
                await stat(strayFolder);

                await page.goto('/downloads');
                let row = page.locator(`[data-row-id="${stray.id}"]`);
                await expect(row).toBeVisible();
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(`[data-action="stop"][data-target="${stray.id}"]`).click();
                await until(
                    'stray download cancellation',
                    async () => ((await api.get<Download>(`/api/downloads/${stray.id}`)).status === 'Cancelled' ? true : undefined),
                    30_000,
                    250,
                );

                await page.getByRole('tab', { name: 'History' }).click();
                row = page.locator(`[data-row-id="${stray.id}"]`);
                await expect(row).toBeVisible();
                await row.locator('[data-action="row-menu"]').click();
                await page.locator(`[data-action="remove"][data-target="${stray.id}"]`).click();
                const deleteResponse = page.waitForResponse(
                    (response) =>
                        response.url().endsWith(`/api/downloads/${stray.id}/files`) &&
                        response.request().method() === 'DELETE',
                );
                await page.locator('[data-component="delete-confirm-dialog"] [data-action="confirm-delete"]').click();
                expect((await deleteResponse).status()).toBe(204);
                await expect(api.get(`/api/downloads/${stray.id}`)).rejects.toMatchObject({ status: 404 });

                const removeCalls = await mocks.downloadClient.calls<{
                    deleteFiles: boolean;
                    downloadId: string;
                }>('RemoveDownloadAsync');
                expect(removeCalls).toContainEqual(
                    expect.objectContaining({
                        request: expect.objectContaining({ deleteFiles: true, downloadId: strayRelease.externalId }),
                    }),
                );
                await expect(stat(strayFolder)).rejects.toThrow();
            });

            await test.step('imports complete: both monitored movies are Fulfilled, and the download-imported event trigger fires its job', async () => {
                const monitored = await Promise.all(
                    movies.slice(0, 2).map((movie) =>
                        until(
                            `${movie.title} fulfillment`,
                            async () => {
                                const current = await api.get<ContentItem>(
                                    `/api/content/${contentByExternalId.get(movie.externalId)!.id}`,
                                );
                                return current.contentItem?.fulfillment === 'Fulfilled' ? current : undefined;
                            },
                            120_000,
                            500,
                        ),
                    ),
                );
                for (const item of monitored) {
                    expect(item.contentItem).toMatchObject({ fulfillment: 'Fulfilled', qualityStatus: 'Met' });
                    expect(item.contentItem!.mediaFiles.length).toBeGreaterThan(0);
                }

                const eventJob = await until(
                    'download-imported event job to become terminal',
                    async () => {
                        const jobs = await api.get<Paged<JourneyJobExecution>>(
                            '/api/jobs?jobType=MediaServerRefresh&pageSize=250',
                        );
                        return jobs.items.find(
                            (job) =>
                                !initialMediaRefreshJobIds.has(job.id) &&
                                job.eventTriggerId != null &&
                                job.triggerContext?.['event.name'] === 'download.imported' &&
                                ['Completed', 'Failed', 'Cancelled'].includes(job.status),
                        );
                    },
                    120_000,
                    500,
                );
                expect(eventJob.status).toBe('Completed');
            });

            await test.step('the next scan leaves everything alone: cutoff met, nothing re-grabbed, unmonitored still untouched', async () => {
                expect((await runImmediateJob(api, 'ReleaseAutoGrabScan')).status).toBe('Completed');

                const remaining = (await api.get<Paged<Download>>('/api/downloads?pageSize=100')).items;
                expect(remaining).toEqual(
                    expect.arrayContaining([
                        expect.objectContaining({ id: firstDownload.id, status: 'Imported' }),
                        expect.objectContaining({ id: failedDownload.id, status: 'Cancelled' }),
                        expect.objectContaining({ id: retriedDownload.id, status: 'Imported' }),
                    ]),
                );
                expect(remaining.filter((item) => item.externalId.startsWith('journey-07-'))).toHaveLength(3);

                for (const movie of movies.slice(0, 2)) {
                    expect(
                        await api.get<ContentItem>(`/api/content/${contentByExternalId.get(movie.externalId)!.id}`),
                    ).toMatchObject({ contentItem: { fulfillment: 'Fulfilled', qualityStatus: 'Met' } });
                }
                const unmonitored = await api.get<ContentItem>(
                    `/api/content/${contentByExternalId.get(movies[2]!.externalId)!.id}`,
                );
                expect(unmonitored).toMatchObject({
                    effectiveMonitoring: 'Unmonitored',
                    contentItem: { fulfillment: 'Missing', mediaFiles: [] },
                });
            });
        } finally {
            await Promise.all([
                mocks.hierarchyPrimary.useScenario('default'),
                mocks.indexer.useScenario('default'),
                mocks.downloadClient.useScenario('default'),
                mocks.mediaServer.useScenario('default'),
            ]);
            await Promise.all(
                [
                    [mocks.hierarchyPrimary.slug, metadataScenario] as const,
                    [mocks.indexer.slug, acquisitionScenario] as const,
                    [mocks.downloadClient.slug, acquisitionScenario] as const,
                    [mocks.mediaServer.slug, acquisitionScenario] as const,
                ].map(([slug, scenario]) =>
                    rm(resolve(FIXTURES_ROOT, slug, 'scenarios', scenario), { recursive: true, force: true }),
                ),
            );
            await Promise.all(
                [...movies.map((movie) => movie.releaseExternalId), 'journey-07-stray'].map((directory) =>
                    rm(resolve(integrationEnvironment.paths.downloads, directory), { recursive: true, force: true }),
                ),
            );
        }
    });
});

async function armMovieMetadata(): Promise<void> {
    await mocks.hierarchyPrimary.useScenario(metadataScenario);
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
        providerType: mocks.hierarchyPrimary.slug,
        source: 'journey-07',
        language: 'en',
        publishDate: '2026-02-01T00:00:00Z',
    });
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', []);
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', []);
    for (const movie of movies) {
        await mocks.hierarchyPrimary.write(`hydrateAsync-${movie.externalId}.json`, movieMetadataCandidate(movie));
    }
}

async function armMovieSearch(movie: JourneyMovie): Promise<void> {
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', movieMetadataCandidate(movie));
    await mocks.hierarchyPrimary.write('advancedSearchAsync.json', [movieMetadataCandidate(movie)]);
}

function movieMetadataCandidate(movie: JourneyMovie): Record<string, unknown> {
    return {
        providerType: mocks.hierarchyPrimary.slug,
        externalIdSource: 'tmdb',
        externalId: movie.externalId,
        contentType: 'movie',
        level: 'movie',
        title: movie.title,
        year: 2026,
        releaseDate: '2026-02-01T00:00:00Z',
        overview: `${movie.title} provider overview.`,
        language: 'en',
        thumbnailUrl: `https://assets.journey.invalid/${movie.externalId}.jpg`,
    };
}

function releaseFixture(movie: JourneyMovie): ReleaseFixture {
    return {
        title: movie.releaseTitle,
        protocol: 'torrent',
        externalId: movie.releaseExternalId,
        infoUrl: `https://indexer.journey.invalid/releases/${movie.releaseExternalId}`,
        size: 4_294_967_296,
        publishDate: '2026-02-02T00:00:00Z',
        categories: ['movies'],
        languages: ['en'],
    };
}
