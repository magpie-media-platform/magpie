import { copyFile, mkdir, rm } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test, type Page } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { FIXTURES_ROOT } from '../helpers/fixtures';
import { drainDownloadRefresh } from '../helpers/jobs';
import { mocks, type ReleaseFixture } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface RootFolder {
    id: string;
    contentType: string;
}

interface ContentItem {
    id: string;
    name: string;
    contentType: string;
    effectiveQualityTierListId?: string | null;
    pendingMetadataRefresh: boolean;
    contentItem?: {
        fulfillment: string;
        qualityStatus?: string | null;
        mediaFiles: Array<{ filePath: string; mimeType?: string | null }>;
    } | null;
}

interface QualityTier {
    id: string;
    name: string;
}

interface QualityTierList {
    id: string;
    name: string;
    mimeTypes: string[];
    qualityTierRanks: Array<{
        qualityTierId: string;
        qualityTier?: QualityTier | null;
    }>;
}

interface Indexer {
    id: string;
    name: string;
    providerType: string;
}

interface IndexerConnection {
    id: string;
    indexerId: string;
    name: string;
    enabled: boolean;
    status: string;
    settings: Record<string, string>;
}

interface DownloadClient {
    id: string;
    name: string;
    priority: number;
}

interface MediaServer {
    id: string;
    name: string;
    contentTypes: string[];
}

interface Download {
    id: string;
    downloadClientId: string;
    externalId: string;
    outputPath?: string | null;
    status: string;
}

interface CandidateScore {
    qualityTierListId: string;
    matchedQualityTierId?: string | null;
    isAcceptable: boolean;
    isExcluded: boolean;
    reasons: string[];
}

interface InteractiveCandidate {
    release: ReleaseFixture & {
        id: string;
        identityKeys?: Array<{ authority: string; value: string; ordinal: number }>;
    };
    matchResult: string;
    indexerName?: string | null;
    score?: CandidateScore | null;
}

interface JourneyContent {
    movie: ContentItem;
    book: ContentItem;
}

interface JourneyQuality {
    movieTier: QualityTier;
    movieList: QualityTierList;
    bookTier: QualityTier;
    bookList: QualityTierList;
}

interface SearchAuditRequest {
    search: { title?: string | null; categories: string[] };
    settings: Record<string, string>;
}

interface DownloadAuditRequest {
    settings: Record<string, string>;
    title: string;
}

interface MediaServerAuditRequest {
    serverId: string;
    contentType: string;
}

const metadataScenario = 'journey-08-metadata';
const indexerScenario = 'journey-08-indexers';
const emptyIndexerScenario = 'journey-08-empty';
const bookIndexerScenario = 'journey-08-book-indexers';
const movieDownloadScenario = 'journey-08-movie-download';
const bookDownloadScenario = 'journey-08-book-download';
const mediaServerScenario = 'journey-08-media';

const movieTitle = 'Interactive Journey Movie';
const bookTitle = 'Interactive Journey Book';
const betterReleaseTitle = 'Interactive Journey Movie 2026 1080p WEB-DL';
const lowerReleaseTitle = 'Interactive Journey Movie 2026 720p WEB-DL';
const wrongCategoryTitle = 'Interactive Journey Movie 2026 480p WEB-DL Wrong Category';
const bookReleaseTitle = 'Interactive Journey Book 2026 PDF';
const movieDownloadExternalId = 'journey-08-movie-download';
const bookDownloadExternalId = 'journey-08-book-download';
const remoteDownloadRoot = '/remote/downloads';
const localDownloadRoot = '/downloads';
const movieMappedDirectory = 'journey-08-mapped';
const movieRemoteOutputPath = `${remoteDownloadRoot}/movie`;

const metadataFixtures = [
    {
        contentType: 'movie',
        option: 'Movie',
        level: 'movie',
        title: movieTitle,
        externalId: 'journey-08-movie',
    },
    {
        contentType: 'book',
        option: 'Book',
        level: 'ebook',
        title: bookTitle,
        externalId: 'journey-08-book',
    },
] as const;

const movieReleases = [
    {
        title: betterReleaseTitle,
        protocol: 'torrent',
        externalId: 'journey-08-release-1080p',
        infoUrl: 'https://indexer.journey.invalid/releases/journey-08-release-1080p',
        size: 4_294_967_296,
        publishDate: '2026-08-06T00:00:00Z',
        categories: ['movies'],
        languages: ['en'],
        properties: JSON.stringify({ seeders: 120 }),
        identityKeys: [{ authority: 'infohash', value: 'journey-08-1080p', ordinal: 0 }],
    },
    {
        title: lowerReleaseTitle,
        protocol: 'torrent',
        externalId: 'journey-08-release-720p',
        infoUrl: 'https://indexer.journey.invalid/releases/journey-08-release-720p',
        size: 2_147_483_648,
        publishDate: '2026-08-05T00:00:00Z',
        categories: ['movies'],
        languages: ['en'],
        properties: JSON.stringify({ seeders: 80 }),
        identityKeys: [{ authority: 'infohash', value: 'journey-08-720p', ordinal: 0 }],
    },
    {
        title: wrongCategoryTitle,
        protocol: 'torrent',
        externalId: 'journey-08-release-wrong-category',
        infoUrl: 'https://indexer.journey.invalid/releases/journey-08-release-wrong-category',
        size: 734_003_200,
        publishDate: '2026-08-04T00:00:00Z',
        categories: ['wrong-category'],
        languages: ['en'],
        properties: JSON.stringify({ seeders: 20 }),
        identityKeys: [{ authority: 'infohash', value: 'journey-08-wrong-category', ordinal: 0 }],
    },
] satisfies Array<ReleaseFixture & { properties: string; identityKeys: unknown[] }>;

const bookRelease = {
    title: bookReleaseTitle,
    protocol: 'usenet',
    externalId: 'journey-08-release-book-pdf',
    infoUrl: 'https://indexer.journey.invalid/releases/journey-08-release-book-pdf',
    size: 12_582_912,
    publishDate: '2026-08-03T00:00:00Z',
    categories: ['books'],
    languages: ['en'],
    properties: JSON.stringify({ pages: 320 }),
    identityKeys: [{ authority: 'guid', value: 'journey-08-book-pdf', ordinal: 0 }],
} satisfies ReleaseFixture & { properties: string; identityKeys: unknown[] };

test.describe.serial('Journey 8: pick the right release by hand', () => {
    test('uses interactive acquisition to choose, download and import the intended releases', async ({ page }) => {
        test.setTimeout(600_000);

        let api!: ApiClient;
        let roots!: RootFolder[];
        let content!: JourneyContent;
        let quality!: JourneyQuality;
        let preferredIndexer!: Indexer;
        let secondaryIndexer!: Indexer;
        let disabledIndexer!: Indexer;
        let disabledConnectionId!: string;
        let selectedClient!: DownloadClient;
        let localMediaServer!: MediaServer;
        let bookMediaServer!: MediaServer;
        let movieCandidates!: InteractiveCandidate[];
        let movieDownload!: Download;
        let bookDownload!: Download;

        try {
            await test.step('restore the configured baseline and find an empty library and an empty dashboard', async () => {
                api = await restoreCheckpoint('configured-baseline', async (restored) => {
                    roots = await restored.get<RootFolder[]>('/api/root-folders');
                    expect(roots.map((root) => root.contentType).sort()).toEqual(['book', 'movie', 'music', 'tv']);
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
                });
                await loginUi(page);

                await page.goto('/library');
                await expect(page.locator('[data-page="library-landing"]')).toBeVisible();
                await expect(page.getByText('Your library is empty', { exact: true })).toBeVisible();
                await page.goto('/');
                await expect(page.locator('[data-page="dashboard"]')).toBeVisible();
                await expect(page.getByText('No active downloads', { exact: true })).toBeVisible();
                await expect(page.getByText('No upcoming content', { exact: true })).toBeVisible();
            });

            await test.step('add one movie and one book to chase', async () => {
                quality = await createQualityFixtures(api);
                await armMetadata();

                const added = new Map<string, ContentItem>();
                for (const fixture of metadataFixtures) {
                    const root = roots.find((candidate) => candidate.contentType === fixture.contentType)!;
                    const qualityTierListId =
                        fixture.contentType === 'book' ? quality.bookList.id : quality.movieList.id;
                    await armMetadataContribution(fixture);
                    const id = await addFromUi(page, fixture, root.id, qualityTierListId);
                    const hydrated = await until(
                        `${fixture.title} metadata refresh`,
                        async () => {
                            const current = await api.get<ContentItem>(`/api/content/${id}`);
                            return current.pendingMetadataRefresh ? undefined : current;
                        },
                        60_000,
                        500,
                    );
                    expect(hydrated).toMatchObject({
                        name: fixture.title,
                        contentType: fixture.contentType,
                        effectiveQualityTierListId: qualityTierListId,
                    });
                    added.set(fixture.contentType, hydrated);
                }

                content = {
                    movie: added.get('movie')!,
                    book: added.get('book')!,
                };
                expect(content.movie.id).toBeTruthy();
                expect(content.book.id).toBeTruthy();
            });

            await test.step('arm three indexer connections with overlapping, competing and mis-categorised releases', async () => {
                await mocks.indexer.useScenario(indexerScenario);
                await mocks.indexer.armTestConnection({ success: true, message: 'journey indexer healthy' });
                await mocks.indexer.armSearch(movieTitle, movieReleases);
                await mocks.indexer.armSearchFallback(movieReleases);

                for (const existing of await api.get<Indexer[]>('/api/indexers')) {
                    for (const connection of await api.get<IndexerConnection[]>(
                        `/api/indexers/${existing.id}/connections`,
                    )) {
                        await api.patch(`/api/indexers/${existing.id}/connections/${connection.id}`, {
                            enabled: { value: false },
                        });
                    }
                }

                preferredIndexer = await createIndexer(api, 'Journey Preferred Indexer', 'preferred.indexer.invalid');
                secondaryIndexer = await createIndexer(api, 'Journey Secondary Indexer', 'secondary.indexer.invalid');
                disabledIndexer = await createIndexer(api, 'Journey Disabled Indexer', 'disabled.indexer.invalid');

                await configureIndexerConnection(
                    api,
                    preferredIndexer.id,
                    'A preferred connection',
                    true,
                );
                await configureIndexerConnection(api, secondaryIndexer.id, 'B secondary connection', true);
                disabledConnectionId = await configureIndexerConnection(
                    api,
                    disabledIndexer.id,
                    'C disabled connection',
                    false,
                );

                const clients = await api.get<DownloadClient[]>('/api/download-clients');
                expect(clients).toHaveLength(1);
                selectedClient = await api.post<DownloadClient>('/api/download-clients', {
                    name: 'Journey Remote Download Client',
                    providerType: mocks.downloadClient.slug,
                    enabled: true,
                    priority: 10,
                    status: 'Healthy',
                    cleanupDeleteFiles: true,
                    keepFailedOnClient: false,
                    settings: {
                        host: 'remote.download.invalid',
                        port: '8080',
                        apiKey: 'journey-08-download-secret',
                        category: 'journey-08-movies',
                    },
                });
                expect(selectedClient.priority).toBeLessThan(clients[0]!.priority);

                const movieLandedPath = resolve(
                    integrationEnvironment.paths.downloads,
                    movieMappedDirectory,
                    'movie',
                );
                await mkdir(movieLandedPath, { recursive: true });
                await copyFile('/media-fixtures/movie-720p.mkv', resolve(movieLandedPath, `${movieTitle}.mkv`));
                await api.post('/api/remote-path-mappings', {
                    host: 'remote.download.invalid',
                    remotePath: remoteDownloadRoot,
                    localPath: `${localDownloadRoot}/${movieMappedDirectory}`,
                });

                await mocks.downloadClient.useScenario(movieDownloadScenario);
                await mocks.downloadClient.armRemove(true);
                await mocks.downloadClient.armTestConnection({
                    success: true,
                    message: 'journey movie download healthy',
                });
                const remoteGrab = {
                    externalId: movieDownloadExternalId,
                    status: 'Queued',
                    progressPercent: 0,
                    outputPath: movieRemoteOutputPath,
                };
                await mocks.downloadClient.armGrab(remoteGrab);
                await mocks.downloadClient.armStatus(movieDownloadExternalId, [
                    {
                        externalId: movieDownloadExternalId,
                        status: 'Queued',
                        progressPercent: 0,
                        outputPath: movieRemoteOutputPath,
                    },
                ]);
                await mocks.downloadClient.clearCalls();

                localMediaServer = (await api.get<MediaServer[]>('/api/media-servers')).find(
                    (server) => server.name === 'Local Media Server',
                )!;
                await api.patch(`/api/media-servers/${localMediaServer.id}`, { contentTypes: ['movie'] });
                bookMediaServer = await api.post<MediaServer>('/api/media-servers', {
                    name: 'Journey Books Media Server',
                    providerType: mocks.mediaServer.slug,
                    enabled: true,
                    status: 'Healthy',
                    contentTypes: ['book'],
                    settings: {
                        host: 'books.media.invalid',
                        port: '8096',
                        apiKey: 'journey-08-media-secret',
                    },
                });
                await mocks.mediaServer.useScenario(mediaServerScenario);
                await mocks.mediaServer.armTestConnection({ success: true, message: 'journey media healthy' });
                await mocks.mediaServer.armRefresh(true);
                await mocks.mediaServer.clearCalls();
                await mocks.indexer.clearCalls();
            });

            await test.step('open interactive search from the item and read the candidate table', async () => {
                await page.goto(`/library/${content.movie.id}`);
                const searchResponse = page.waitForResponse(
                    (response) =>
                        response.url().endsWith(`/api/interactive-search/${content.movie.id}`) &&
                        response.request().method() === 'POST',
                );
                await page.locator('[data-action="interactive-search"]').first().click();
                movieCandidates = (await (await searchResponse).json()) as InteractiveCandidate[];

                await expect(page).toHaveURL(`/interactive-search/${content.movie.id}`);
                const table = page.locator('[data-page="interactive-search"] [data-component="data-table"]');
                await expect(table).toBeVisible();
                await expect(table.locator('th[data-column="indexer"]')).toContainText('Indexer');
                await expect(table.locator('th[data-column="size"]')).toContainText('Size');
                await expect(table.locator('th[data-column="publishDate"]')).toContainText('Published');

                expect(movieCandidates).toHaveLength(3);
                for (const title of [betterReleaseTitle, lowerReleaseTitle, wrongCategoryTitle]) {
                    const row = table.getByRole('row').filter({ hasText: title });
                    await expect(row).toBeVisible();
                    await expect(row.locator('[data-column="indexer"]')).toContainText('Journey Preferred Indexer');
                    await expect(row.locator('[data-column="size"]')).not.toHaveText('-');
                    await expect(row.locator('[data-column="publishDate"]')).not.toHaveText('-');
                }
            });

            await test.step('sort the candidates and inspect one in the release details dialog', async () => {
                const table = page.locator('[data-page="interactive-search"] [data-component="data-table"]');
                const titles = table.locator('[data-action="release-details"]');
                const sizeSort = table.getByRole('button', { name: 'Sort by Size' });
                await sizeSort.click();
                await sizeSort.click();
                await expect(table.locator('th[data-column="size"]')).toHaveAttribute('aria-sort', 'ascending');
                await expect.poll(() => titles.allTextContents()).toEqual([
                    'Interactive Journey Movie 2026 480p WEB-DL Wrong Category',
                    'Interactive Journey Movie 2026 720p WEB-DL',
                    'Interactive Journey Movie 2026 1080p WEB-DL',
                ]);

                await table.getByRole('button', { name: betterReleaseTitle, exact: true }).click();
                const dialog = page.getByTestId('release-details-dialog');
                await expect(dialog).toBeVisible();
                await expect(dialog).toContainText(betterReleaseTitle);
                await expect(dialog.locator('[data-component="release-summary"]')).toContainText(
                    'Journey Preferred Indexer',
                );
                await expect(dialog.locator('[data-component="release-summary"]')).toContainText('movies');
                await expect(dialog.locator('[data-component="release-indexer-properties"]')).toContainText('120');
                await page.keyboard.press('Escape');
                await expect(dialog).toBeHidden();
            });

            await test.step('the better release ranks first on the tier criteria and the grab is driven from the UI', async () => {
                expect(movieCandidates[0]).toMatchObject({
                    release: { title: betterReleaseTitle },
                    score: {
                        qualityTierListId: quality.movieList.id,
                        matchedQualityTierId: quality.movieTier.id,
                    },
                });
                const better = movieCandidates.find((candidate) => candidate.release.title === betterReleaseTitle)!;
                const row = page.locator(`[data-row-id="${better.release.id}"]`);
                await row.locator('[data-action="grab"]').first().click();
                if (!['Identity', 'Confident'].includes(better.matchResult)) {
                    await page
                        .locator('[data-component="delete-confirm-dialog"] [data-action="confirm-delete"]')
                        .click();
                }
                await expect(
                    page.getByTestId('toast-title').filter({ hasText: /^Release grabbed$/ }).last(),
                ).toBeVisible();

                movieDownload = await until(
                    `${movieDownloadExternalId} download record`,
                    async () => {
                        const downloads = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                        return downloads.items.find((download) => download.externalId === movieDownloadExternalId);
                    },
                    30_000,
                    250,
                );
                expect(movieDownload.status).toBe('Queued');
            });

            await test.step('the disabled indexer is never asked', async () => {
                const calls = await mocks.indexer.calls<SearchAuditRequest>('search');
                const freshDisabledConnection = (
                    await api.get<IndexerConnection[]>(`/api/indexers/${disabledIndexer.id}/connections`)
                ).find((connection) => connection.id === disabledConnectionId)!;
                expect(calls.length).toBeGreaterThan(0);
                expect(
                    calls.some((call) => call.request.settings['apiUrl'] === 'disabled.indexer.invalid'),
                ).toBe(false);
                expect(freshDisabledConnection.enabled).toBe(false);
            });

            await test.step('the duplicate release resolves to the higher-priority indexer', async () => {
                const duplicate = movieCandidates.filter(
                    (candidate) => candidate.release.identityKeys?.[0]?.value === 'journey-08-1080p',
                );
                expect(duplicate).toHaveLength(1);
                expect(duplicate[0]?.indexerName).toBe(preferredIndexer.name);
            });

            await test.step('the download lands under a remote path and the mapping lets the import find it', async () => {
                await mocks.downloadClient.armStatus(movieDownloadExternalId, [
                    {
                        externalId: movieDownloadExternalId,
                        status: 'Downloaded',
                        progressPercent: 100,
                        outputPath: movieRemoteOutputPath,
                    },
                ]);
                await drainDownloadRefresh(api);

                movieDownload = await until(
                    `${movieDownloadExternalId} import`,
                    async () => {
                        const current = await api.get<Download>(`/api/downloads/${movieDownload.id}`);
                        return current.status === 'Imported' ? current : undefined;
                    },
                    120_000,
                    500,
                );
                const imported = await api.get<ContentItem>(`/api/content/${content.movie.id}`);
                expect(movieDownload.outputPath).toBe(movieRemoteOutputPath);
                expect(imported.contentItem).toMatchObject({ fulfillment: 'Fulfilled' });
                expect(imported.contentItem?.mediaFiles).toContainEqual(
                    expect.objectContaining({ filePath: expect.stringMatching(/Interactive Journey Movie\.mkv$/) }),
                );
            });

            await test.step("the grab carried the client's category to the client, and the right one of two clients was used", async () => {
                const calls = await mocks.downloadClient.calls<DownloadAuditRequest>('DownloadReleaseAsync');
                expect(calls).toHaveLength(1);
                expect(calls[0]?.request).toMatchObject({
                    title: betterReleaseTitle,
                    settings: {
                        host: 'remote.download.invalid',
                        category: 'journey-08-movies',
                    },
                });
                expect(movieDownload.downloadClientId).toBe(selectedClient.id);
            });

            await test.step('a search that finds nothing renders an empty state and offers a retry', async () => {
                await mocks.indexer.useScenario(emptyIndexerScenario);
                await mocks.indexer.armTestConnection({ success: true, message: 'journey empty indexer healthy' });
                await mocks.indexer.armSearch(bookTitle, []);
                await mocks.indexer.armSearchFallback([]);

                await page.goto(`/library/${content.book.id}`);
                await page.locator('[data-action="interactive-search"]').first().click();
                await expect(page).toHaveURL(`/interactive-search/${content.book.id}`);
                await expect(page.getByText('No releases found', { exact: true })).toBeVisible();
                await expect(page.locator('[data-action="search-again"]')).toBeVisible();
            });

            await test.step("the book's mime-scoped tier list applies to the book and not to the movie", async () => {
                const bookItem = await api.get<ContentItem>(`/api/content/${content.book.id}`);
                const movieItem = await api.get<ContentItem>(`/api/content/${content.movie.id}`);
                expect(bookItem.effectiveQualityTierListId).toBe(quality.bookList.id);
                expect(movieItem.effectiveQualityTierListId).toBe(quality.movieList.id);
                expect(quality.bookList.mimeTypes).toEqual(['application/pdf']);
                expect(quality.movieList.mimeTypes).toEqual(['video/*']);

                await mocks.indexer.useScenario(bookIndexerScenario);
                await mocks.indexer.armTestConnection({ success: true, message: 'journey book indexer healthy' });
                await mocks.indexer.armSearch(bookTitle, [bookRelease]);
                await mocks.indexer.armSearchFallback([bookRelease]);

                await mocks.downloadClient.useScenario(bookDownloadScenario);
                await mocks.downloadClient.armRemove(true);
                await mocks.downloadClient.armTestConnection({
                    success: true,
                    message: 'journey book download healthy',
                });
                await mocks.downloadClient.armGrab({
                    externalId: bookDownloadExternalId,
                    status: 'Queued',
                    progressPercent: 0,
                    land: [{ source: 'book.pdf', target: `${bookTitle}.pdf` }],
                });
                await mocks.downloadClient.armStatus(bookDownloadExternalId, [
                    { externalId: bookDownloadExternalId, status: 'Queued', progressPercent: 0 },
                ]);

                const response = page.waitForResponse(
                    (candidate) =>
                        candidate.url().endsWith(`/api/interactive-search/${content.book.id}`) &&
                        candidate.request().method() === 'POST',
                );
                await page.locator('[data-action="search-again"]').click();
                const candidates = (await (await response).json()) as InteractiveCandidate[];
                expect(candidates).toHaveLength(1);
                expect(candidates[0]?.score).toMatchObject({
                    qualityTierListId: quality.bookList.id,
                    matchedQualityTierId: quality.bookTier.id,
                    isAcceptable: true,
                });

                const bookCandidate = candidates[0]!;
                await page.locator(`[data-row-id="${bookCandidate.release.id}"] [data-action="grab"]`).first().click();
                if (!['Identity', 'Confident'].includes(bookCandidate.matchResult)) {
                    await page
                        .locator('[data-component="delete-confirm-dialog"] [data-action="confirm-delete"]')
                        .click();
                }
                bookDownload = await until(
                    `${bookDownloadExternalId} download record`,
                    async () => {
                        const downloads = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
                        return downloads.items.find((download) => download.externalId === bookDownloadExternalId);
                    },
                    30_000,
                    250,
                );

                await mocks.downloadClient.armStatus(bookDownloadExternalId, [
                    { externalId: bookDownloadExternalId, status: 'Downloaded', progressPercent: 100 },
                ]);
                await drainDownloadRefresh(api);
                bookDownload = await until(
                    `${bookDownloadExternalId} import`,
                    async () => {
                        const current = await api.get<Download>(`/api/downloads/${bookDownload.id}`);
                        return current.status === 'Imported' ? current : undefined;
                    },
                    120_000,
                    500,
                );
                const imported = await api.get<ContentItem>(`/api/content/${content.book.id}`);
                expect(imported.contentItem).toMatchObject({ fulfillment: 'Fulfilled', qualityStatus: 'Met' });
                expect(imported.contentItem?.mediaFiles).toContainEqual(
                    expect.objectContaining({ mimeType: 'application/pdf' }),
                );
            });

            await test.step('only the media server scoped to books is refreshed for the book', async () => {
                const calls = await until(
                    'scoped movie and book media server refreshes',
                    async () => {
                        const current = await mocks.mediaServer.calls<MediaServerAuditRequest>(
                            'RefreshForContentAsync',
                        );
                        return current.some((call) => call.request.contentType === 'movie') &&
                            current.some((call) => call.request.contentType === 'book')
                            ? current
                            : undefined;
                    },
                    60_000,
                    500,
                );
                const movieCalls = calls.filter((call) => call.request.contentType === 'movie');
                const bookCalls = calls.filter((call) => call.request.contentType === 'book');
                expect([...new Set(movieCalls.map((call) => call.request.serverId))]).toEqual([localMediaServer.id]);
                expect([...new Set(bookCalls.map((call) => call.request.serverId))]).toEqual([bookMediaServer.id]);
            });
        } finally {
            await Promise.allSettled([
                mocks.hierarchyPrimary.useScenario('default'),
                mocks.indexer.useScenario('default'),
                mocks.downloadClient.useScenario('default'),
                mocks.mediaServer.useScenario('default'),
            ]);
            await Promise.all([
                rm(resolve(FIXTURES_ROOT, mocks.hierarchyPrimary.slug, 'scenarios', metadataScenario), {
                    recursive: true,
                    force: true,
                }),
                ...[indexerScenario, emptyIndexerScenario, bookIndexerScenario].map((scenario) =>
                    rm(resolve(FIXTURES_ROOT, mocks.indexer.slug, 'scenarios', scenario), {
                        recursive: true,
                        force: true,
                    }),
                ),
                ...[movieDownloadScenario, bookDownloadScenario].map((scenario) =>
                    rm(resolve(FIXTURES_ROOT, mocks.downloadClient.slug, 'scenarios', scenario), {
                        recursive: true,
                        force: true,
                    }),
                ),
                rm(resolve(FIXTURES_ROOT, mocks.mediaServer.slug, 'scenarios', mediaServerScenario), {
                    recursive: true,
                    force: true,
                }),
                rm(resolve(integrationEnvironment.paths.downloads, movieDownloadExternalId), {
                    recursive: true,
                    force: true,
                }),
                rm(resolve(integrationEnvironment.paths.downloads, movieMappedDirectory), {
                    recursive: true,
                    force: true,
                }),
                rm(resolve(integrationEnvironment.paths.downloads, bookDownloadExternalId), {
                    recursive: true,
                    force: true,
                }),
            ]);
        }
    });
});

async function createQualityFixtures(api: ApiClient): Promise<JourneyQuality> {
    const movieTier = await api.post<QualityTier>('/api/quality-tiers', {
        name: 'Journey Movie 1080p',
        criteria: {
            and: [{ items: { title: { operator: 'ContainsString', value: '1080p', values: [], negate: false } } }],
        },
    });
    const movieLowerTier = await api.post<QualityTier>('/api/quality-tiers', {
        name: 'Journey Movie 720p',
        criteria: {
            and: [{ items: { title: { operator: 'ContainsString', value: '720p', values: [], negate: false } } }],
        },
    });
    const movieList = await api.post<QualityTierList>('/api/quality-tier-lists', {
        name: 'Journey Movie Tiers',
        mimeTypes: ['video/*'],
        upgradeAllowed: false,
        qualityTierRanks: [
            { qualityTierId: movieTier.id, ordinal: 0, isCutoff: true },
            { qualityTierId: movieLowerTier.id, ordinal: 1, isCutoff: false },
        ],
    });

    const bookTier = await api.post<QualityTier>('/api/quality-tiers', {
        name: 'Journey Book PDF',
        criteria: {
            and: [{ items: { title: { operator: 'ContainsString', value: 'PDF', values: [], negate: false } } }],
        },
    });
    const bookList = await api.post<QualityTierList>('/api/quality-tier-lists', {
        name: 'Journey Book Tiers',
        mimeTypes: ['application/pdf'],
        upgradeAllowed: false,
        qualityTierRanks: [{ qualityTierId: bookTier.id, ordinal: 0, isCutoff: true }],
    });

    return { movieTier, movieList, bookTier, bookList };
}

async function armMetadata(): Promise<void> {
    await mocks.hierarchyPrimary.useScenario(metadataScenario);
    await mocks.hierarchyPrimary.write(
        'advancedSearchAsync.json',
        metadataFixtures.map(metadataCandidate),
    );
    for (const fixture of metadataFixtures) {
        const candidate = metadataCandidate(fixture);
        await mocks.hierarchyPrimary.write(`hydrateAsync-${fixture.externalId}.json`, candidate);
    }
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', []);
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', []);
}

async function armMetadataContribution(fixture: (typeof metadataFixtures)[number]): Promise<void> {
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
        providerType: mocks.hierarchyPrimary.slug,
        source: `tmdb:${fixture.externalId}`,
        title: fixture.title,
        overview: `${fixture.title} provider overview.`,
        language: 'en',
        publishDate: '2026-02-01T00:00:00Z',
        externalIds: [{ source: 'tmdb', value: fixture.externalId, ordinal: 0 }],
    });
}

function metadataCandidate(fixture: (typeof metadataFixtures)[number]): Record<string, unknown> {
    return {
        providerType: mocks.hierarchyPrimary.slug,
        externalIdSource: 'tmdb',
        externalId: fixture.externalId,
        contentType: fixture.contentType,
        level: fixture.level,
        title: fixture.title,
        year: 2026,
        releaseDate: '2026-02-01T00:00:00Z',
        overview: `${fixture.title} provider overview.`,
        language: 'en',
    };
}

async function addFromUi(
    page: Page,
    fixture: (typeof metadataFixtures)[number],
    rootFolderId: string,
    qualityTierListId: string,
): Promise<string> {
    await page.goto('/library/add');
    await page.getByRole('combobox', { name: 'Content Type' }).click();
    await page.getByRole('option', { name: fixture.option, exact: true }).click();
    await page.locator('#sfb-title').fill(fixture.title);
    await page.getByRole('button', { name: /^search$/i }).click();

    const row = page.getByRole('grid').getByRole('row').filter({ hasText: fixture.title });
    await expect(row).toBeVisible();
    await row.getByRole('button', { name: fixture.title, exact: true }).click();
    const preview = page.getByTestId('preview-drawer');
    await expect(preview).toContainText(`${fixture.title} provider overview.`);

    await page.route(
        /\/api\/content\/from-external-id(?:\?|$)/,
        async (route) => {
            const url = new URL(route.request().url());
            url.searchParams.set('monitoring', 'false');
            url.searchParams.set('rootFolderId', rootFolderId);
            url.searchParams.set('qualityTierListId', qualityTierListId);
            await route.continue({ url: url.toString() });
        },
        { times: 1 },
    );
    const response = page.waitForResponse(
        (candidate) =>
            candidate.url().includes('/api/content/from-external-id') && candidate.request().method() === 'POST',
    );
    await preview.locator('[data-action="preview-drawer-add"]').click();
    const added = await response;
    expect(added.ok()).toBe(true);
    return (await added.json()) as string;
}

async function createIndexer(api: ApiClient, name: string, apiUrl: string): Promise<Indexer> {
    return api.post<Indexer>('/api/indexers', {
        name,
        providerType: mocks.indexer.slug,
        availability: 'Public',
        categories: ['movies', 'books'],
        languages: ['en'],
        settings: { apiUrl, apiKey: 'journey-08-indexer-secret' },
    });
}

async function configureIndexerConnection(
    api: ApiClient,
    indexerId: string,
    name: string,
    enabled: boolean,
): Promise<string> {
    const connection = (await api.get<IndexerConnection[]>(`/api/indexers/${indexerId}/connections`))[0]!;
    await api.patch(`/api/indexers/${indexerId}/connections/${connection.id}`, {
        name: { value: name },
        enabled: { value: enabled },
    });
    return connection.id;
}
