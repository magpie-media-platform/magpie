import { rm, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { FIXTURES_ROOT } from '../helpers/fixtures';
import { drainDownloadRefresh, drainReleaseAcquisitionJobs, type JobExecution, waitForJob } from '../helpers/jobs';
import { mocks, type ReleaseFixture } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface ContentItem {
    id: string;
    name: string;
    effectiveMonitoring: boolean;
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
    status: string;
}

interface JourneyJobExecution extends JobExecution {
    parameters: Record<string, string>;
}

interface ContentRelease {
    acquisitionStatus: string;
    title: string;
}

const wantedSearchScenario = 'journey-13-wanted-searches';
const upgradeScenario = 'journey-13-upgrades';
const upgradeReleaseExternalId = 'journey-13-feature-1080p';
const upgradeDownloadExternalId = 'journey-13-feature-upgrade-download';
const cutoffReleaseExternalId = 'journey-13-track-competing';
const upgradedFilePath = '/library/movies/Journey Feature (2024)/Journey Feature 1080p.mkv';

const upgradeRelease: ReleaseFixture = {
    title: 'Journey Feature 2024 1080p BluRay',
    protocol: 'torrent',
    externalId: upgradeReleaseExternalId,
    infoUrl: `https://indexer.journey.invalid/releases/${upgradeReleaseExternalId}`,
    size: 1_468_006_400,
    publishDate: '2026-07-01T00:00:00Z',
    categories: ['movies'],
    languages: ['en'],
};

const cutoffRelease: ReleaseFixture = {
    title: 'Journey Artist Journey Album Track 01 FLAC',
    protocol: 'torrent',
    externalId: cutoffReleaseExternalId,
    infoUrl: `https://indexer.journey.invalid/releases/${cutoffReleaseExternalId}`,
    size: 20_971_520,
    publishDate: '2026-07-01T00:00:00Z',
    categories: ['music'],
    languages: ['en'],
};

test.describe.serial("Journey 13: chase what's missing and upgrade what's mediocre", () => {
    test('searches wanted items and upgrades only content below cutoff', async ({ page }) => {
        test.setTimeout(900_000);

        let api!: ApiClient;
        let allContent!: ContentItem[];
        let movie!: ContentItem;
        let track!: ContentItem;
        let movieBefore!: ContentItem;
        let trackBefore!: ContentItem;
        let upgradeDownload!: Download;
        let switchedWantedSearchScenario = false;
        let switchedUpgradeIndexerScenario = false;
        let switchedUpgradeDownloadScenario = false;
        let switchedUpgradeMediaScenario = false;

        try {
            await test.step('restore the rich library at its post-import checkpoint', async () => {
                api = await restoreCheckpoint('journey-rich-library', async (restored) => {
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(87);
                    expect(
                        (
                            await restored.get<Paged<ContentItem>>(
                                '/api/content?monitoring=true&fulfillment=Missing&pageSize=100',
                            )
                        ).totalItems,
                    ).toBe(79);
                    expect(
                        (
                            await restored.get<Paged<ContentItem>>(
                                '/api/content?monitoring=true&fulfillment=Upgradable&pageSize=100',
                            )
                        ).totalItems,
                    ).toBe(3);
                    expect(
                        (
                            await restored.get<Paged<ContentItem>>(
                                '/api/content?monitoring=true&fulfillment=Fulfilled&pageSize=100',
                            )
                        ).totalItems,
                    ).toBe(1);
                    expect(
                        (
                            await restored.get<Paged<ContentItem>>(
                                '/api/content?monitoring=true&fulfillment=Partial&pageSize=100',
                            )
                        ).totalItems,
                    ).toBe(4);
                });
                allContent = (await api.get<Paged<ContentItem>>('/api/content?pageSize=100')).items;
                expect(allContent).toHaveLength(87);
                expect(allContent.every((item) => item.effectiveMonitoring)).toBe(true);
            });

            await test.step('open the wanted list with upgrades included', async () => {
                await loginUi(page);
                await page.setViewportSize({ width: 1365, height: 900 });
                await page.goto('/wanted');
                await expect(page.getByRole('heading', { name: 'Wanted', exact: true })).toBeVisible();
                const rows = page.getByRole('grid').locator('tbody tr[data-row-id]');
                await expect(rows).toHaveCount(25);
                await expect(page.getByRole('navigation', { name: 'Pagination' }).locator('.pager__page')).toHaveText(
                    'Page 1 of 3',
                );
                await expect(page.getByText('Journey Catalog 26', { exact: true })).toBeVisible();
            });

            await test.step('filter the wanted list by content type', async () => {
                await page.getByRole('button', { name: 'Filter by Type' }).click();
                await page.getByRole('menuitemradio', { name: 'Movie', exact: true }).click();

                const rows = page.getByRole('grid').locator('tbody tr[data-row-id]');
                await expect(rows).toHaveCount(25);
                await expect(page.getByRole('navigation', { name: 'Pagination' }).locator('.pager__page')).toHaveText(
                    'Page 1 of 2',
                );
                for (const row of await rows.all()) {
                    await expect(row).toContainText('Movie');
                }
            });

            await test.step('start a search for one item from its row', async () => {
                switchedWantedSearchScenario = true;
                await mocks.indexer.useScenario(wantedSearchScenario);
                await mocks.indexer.armSearchFallback([]);
                await mocks.indexer.clearCalls();

                const target = allContent.find((item) => item.name === 'Journey Catalog 26');
                expect(target).toBeDefined();
                const before = await api.get<Paged<JourneyJobExecution>>(
                    '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                );
                const knownIds = new Set(before.items.map((job) => job.id));

                await page
                    .locator(`[data-row-id="${target!.id}"] [data-action="search-now"]`)
                    .click();

                const execution = await until(
                    'the wanted row search execution',
                    async () => {
                        const jobs = await api.get<Paged<JourneyJobExecution>>(
                            '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                        );
                        return jobs.items.find(
                            (job) =>
                                !knownIds.has(job.id) &&
                                job.parameters['contentHierarchyItemId'] === target!.id,
                        );
                    },
                    60_000,
                    250,
                );
                const rowSearch = await waitForJob(api, execution.id);
                expect(rowSearch.status).toBe('Completed');
                expect(rowSearch.statusMessage ?? '').not.toContain('Skipped:');
            });

            await test.step('select several rows and search for all of them at once', async () => {
                const rows = page.getByRole('grid').locator('tbody tr[data-row-id]');
                const catalogIds = new Set(
                    allContent.filter((item) => /^Journey Catalog \d{2}$/.test(item.name)).map((item) => item.id),
                );
                const visibleIds = await rows.evaluateAll((elements) =>
                    elements.map((element) => element.getAttribute('data-row-id')).filter((id): id is string => !!id),
                );
                const selectedIds = visibleIds.filter((id) => catalogIds.has(id)).slice(0, 12);
                expect(selectedIds).toHaveLength(12);
                for (const id of selectedIds) {
                    await page.locator(`[data-row-id="${id}"]`).getByRole('checkbox').click();
                }
                await expect(page.locator('[data-component="bulk-actions-toolbar"]')).toContainText('12 selected');

                const before = await api.get<Paged<JourneyJobExecution>>(
                    '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                );
                const knownIds = new Set(before.items.map((job) => job.id));
                await page.locator('[data-action="bulk-search-now"]').click();

                const completed = await until(
                    'one bulk search execution per selected wanted item',
                    async () => {
                        const jobs = await api.get<Paged<JourneyJobExecution>>(
                            '/api/jobs?jobType=ReleaseSearchItem&pageSize=250',
                        );
                        const relevant = jobs.items.filter(
                            (job) =>
                                !knownIds.has(job.id) &&
                                selectedIds.includes(job.parameters['contentHierarchyItemId']!),
                        );
                        return relevant.length >= 12 &&
                            relevant.every((job) => ['Completed', 'Failed', 'Cancelled'].includes(job.status))
                            ? relevant
                            : undefined;
                    },
                    120_000,
                    250,
                );
                expect(completed).toHaveLength(12);
                for (const id of selectedIds) {
                    expect(completed.filter((job) => job.parameters['contentHierarchyItemId'] === id)).toHaveLength(1);
                }
                expect(completed.filter((job) => (job.statusMessage ?? '').startsWith('Skipped:'))).toEqual([]);
            });

            await test.step('a second manual search for the same item is coalesced with the first', async () => {
                const target = allContent.find((item) => item.name === 'Journey Catalog 26');
                const parameters = { contentHierarchyItemId: target!.id, force: 'true' };
                const first = await api.post<string>('/api/jobs/enqueue', { jobType: 'ReleaseSearchItem', parameters });
                const second = await api.post<string>('/api/jobs/enqueue', { jobType: 'ReleaseSearchItem', parameters });
                expect((await waitForJob(api, first)).status).toBe('Completed');
                if (second !== first) {
                    const execution = await waitForJob(api, second);
                    expect(execution.status).toBe('Completed');
                    expect(execution.statusMessage ?? '').toContain('Skipped:');
                }
            });

            await test.step(
                'arm a better release for the item that is below cutoff and a competing one for the item ' +
                    'that is already at cutoff',
                async () => {
                    movie = allContent.find((item) => item.name === 'Journey Feature')!;
                    track = allContent.find((item) => item.name === 'Journey Track 1')!;
                    expect(movie).toBeDefined();
                    expect(track).toBeDefined();
                    movieBefore = await api.get<ContentItem>(`/api/content/${movie.id}`);
                    trackBefore = await api.get<ContentItem>(`/api/content/${track.id}`);
                    expect(movieBefore.contentItem).toMatchObject({
                        fulfillment: 'Upgradable',
                        qualityStatus: 'UpgradeWanted',
                    });
                    expect(movieBefore.contentItem!.mediaFiles.map((file) => file.filePath).sort()).toEqual([
                        '/library/movies/Journey Feature (2024)/Journey Feature.en.srt',
                        '/library/movies/Journey Feature (2024)/Journey Feature.mkv',
                    ]);
                    expect(trackBefore.contentItem).toMatchObject({ fulfillment: 'Fulfilled', qualityStatus: 'Met' });
                    expect(trackBefore.contentItem!.mediaFiles.map((file) => file.filePath)).toEqual([
                        '/library/music/Journey Artist/Journey Album (2024)/01 - Journey Track 1.flac',
                    ]);

                    switchedUpgradeIndexerScenario = true;
                    await mocks.indexer.useScenario(upgradeScenario);
                    await mocks.indexer.armSearchFallback([upgradeRelease, cutoffRelease]);
                    await mocks.indexer.clearCalls();

                    switchedUpgradeDownloadScenario = true;
                    await mocks.downloadClient.useScenario(upgradeScenario);
                    await mocks.downloadClient.armGrab({
                        externalId: upgradeDownloadExternalId,
                        status: 'Queued',
                        progressPercent: 0,
                        land: [
                            {
                                source: 'movie-720p.mkv',
                                target: 'Journey Feature Upgrade/Journey Feature 1080p.mkv',
                            },
                        ],
                    });
                    await mocks.downloadClient.armStatus(upgradeDownloadExternalId, [
                        { externalId: upgradeDownloadExternalId, status: 'Downloading', progressPercent: 50 },
                        { externalId: upgradeDownloadExternalId, status: 'Downloaded', progressPercent: 100 },
                    ]);
                    await mocks.downloadClient.armRemove(true);
                    await mocks.downloadClient.clearCalls();

                    switchedUpgradeMediaScenario = true;
                    await mocks.mediaServer.useScenario(upgradeScenario);
                    await mocks.mediaServer.armRefresh(true);
                    await mocks.mediaServer.clearCalls();
                },
            );

            await test.step('the item already at its cutoff is left alone', async () => {
                const downloadsBefore = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                const searchId = await api.post<string>('/api/jobs/enqueue', {
                    jobType: 'ReleaseSearchItem',
                    parameters: { contentHierarchyItemId: track.id, force: 'true' },
                });
                expect((await waitForJob(api, searchId)).status).toBe('Completed');
                await drainReleaseAcquisitionJobs(api);
                expect(await mocks.downloadClient.calls('DownloadReleaseAsync')).toHaveLength(0);
                expect((await api.get<Paged<Download>>('/api/downloads?pageSize=100')).totalItems).toBe(
                    downloadsBefore.totalItems,
                );

                const unchanged = await api.get<ContentItem>(`/api/content/${track.id}`);
                expect(unchanged.contentItem).toMatchObject({ fulfillment: 'Fulfilled', qualityStatus: 'Met' });
                expect(unchanged.contentItem!.mediaFiles.map((file) => file.filePath)).toEqual(
                    trackBefore.contentItem!.mediaFiles.map((file) => file.filePath),
                );
            });

            await test.step('the item below cutoff with upgrades allowed takes the better release', async () => {
                const searchId = await api.post<string>('/api/jobs/enqueue', {
                    jobType: 'ReleaseSearchItem',
                    parameters: { contentHierarchyItemId: movie.id, force: 'true' },
                });
                expect((await waitForJob(api, searchId)).status).toBe('Completed');
                const autoGrab = await until(
                    'the below-cutoff item auto-grab decision',
                    async () => {
                        const jobs = await api.get<Paged<JourneyJobExecution>>(
                            '/api/jobs?jobType=ReleaseAutoGrabItem&pageSize=250',
                        );
                        return jobs.items.find(
                            (job) =>
                                job.parentExecutionId === searchId &&
                                ['Completed', 'Failed', 'Cancelled'].includes(job.status),
                        );
                    },
                    120_000,
                    250,
                );
                expect(autoGrab.status).toBe('Completed');

                upgradeDownload = await until(
                    'the upgrade download to appear',
                    async () => {
                        const downloads = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                        return downloads.items.find((download) => download.externalId === upgradeDownloadExternalId);
                    },
                    60_000,
                    250,
                );
                for (let attempt = 0; attempt < 5; attempt += 1) {
                    const current = await api.get<Download>(`/api/downloads/${upgradeDownload.id}`);
                    if (current.status === 'Imported') {
                        break;
                    }
                    await drainDownloadRefresh(api);
                }
                upgradeDownload = await until(
                    'the quality upgrade to import',
                    async () => {
                        const current = await api.get<Download>(`/api/downloads/${upgradeDownload.id}`);
                        return current.status === 'Imported' ? current : undefined;
                    },
                    120_000,
                    500,
                );
                expect(upgradeDownload.status).toBe('Imported');
                expect(await mocks.downloadClient.calls<{ title: string }>('DownloadReleaseAsync')).toEqual([
                    expect.objectContaining({
                        request: expect.objectContaining({
                            title: upgradeRelease.title,
                            infoUrl: upgradeRelease.infoUrl,
                        }),
                    }),
                ]);
                expect(await api.get<ContentRelease[]>(`/api/content/${movie.id}/releases`)).toContainEqual(
                    expect.objectContaining({
                        title: upgradeRelease.title,
                        acquisitionStatus: 'Imported',
                    }),
                );
            });

            await test.step('the upgraded item ends with one file, not two', async () => {
                const upgraded = await until(
                    'the upgraded item file accounting',
                    async () => {
                        const current = await api.get<ContentItem>(`/api/content/${movie.id}`);
                        return current.contentItem?.mediaFiles.length === 1 ? current : undefined;
                    },
                    60_000,
                    500,
                );
                expect(upgraded.contentItem).toMatchObject({ fulfillment: 'Fulfilled', qualityStatus: 'Met' });
                expect(upgraded.contentItem!.mediaFiles.map((file) => file.filePath)).toEqual([upgradedFilePath]);

                await stat(
                    resolve(
                        integrationEnvironment.paths.library,
                        upgradedFilePath.slice('/library/'.length),
                    ),
                );
                for (const prior of movieBefore.contentItem!.mediaFiles) {
                    await expect(
                        stat(
                            resolve(
                                integrationEnvironment.paths.library,
                                prior.filePath.slice('/library/'.length),
                            ),
                        ),
                    ).rejects.toThrow();
                }
                expect(await api.get('/api/content/stats')).toEqual({
                    total: 87,
                    missing: 79,
                    upgradable: 2,
                    fulfilled: 2,
                    fulfilledExternally: 0,
                    partial: 4,
                });
            });
        } finally {
            const cleanupActions: Promise<unknown>[] = [];
            if (switchedWantedSearchScenario || switchedUpgradeIndexerScenario) {
                cleanupActions.push(mocks.indexer.useScenario('default'));
            }
            if (switchedUpgradeDownloadScenario) {
                cleanupActions.push(mocks.downloadClient.useScenario('default'));
            }
            if (switchedUpgradeMediaScenario) {
                cleanupActions.push(mocks.mediaServer.useScenario('default'));
            }
            if (switchedWantedSearchScenario) {
                cleanupActions.push(
                    rm(resolve(FIXTURES_ROOT, mocks.indexer.slug, 'scenarios', wantedSearchScenario), {
                        recursive: true,
                        force: true,
                    }),
                );
            }
            if (switchedUpgradeIndexerScenario) {
                cleanupActions.push(
                    rm(resolve(FIXTURES_ROOT, mocks.indexer.slug, 'scenarios', upgradeScenario), {
                        recursive: true,
                        force: true,
                    }),
                );
            }
            if (switchedUpgradeDownloadScenario) {
                cleanupActions.push(
                    rm(resolve(FIXTURES_ROOT, mocks.downloadClient.slug, 'scenarios', upgradeScenario), {
                        recursive: true,
                        force: true,
                    }),
                );
                cleanupActions.push(
                    rm(resolve(integrationEnvironment.paths.downloads, upgradeDownloadExternalId), {
                        recursive: true,
                        force: true,
                    }),
                );
            }
            if (switchedUpgradeMediaScenario) {
                cleanupActions.push(
                    rm(resolve(FIXTURES_ROOT, mocks.mediaServer.slug, 'scenarios', upgradeScenario), {
                        recursive: true,
                        force: true,
                    }),
                );
            }
            const cleanupResults = await Promise.allSettled(cleanupActions);
            const cleanupErrors = cleanupResults
                .filter((result) => result.status === 'rejected')
                .map((result) => String((result as PromiseRejectedResult).reason));
            expect(cleanupErrors).toEqual([]);
        }
    });
});
