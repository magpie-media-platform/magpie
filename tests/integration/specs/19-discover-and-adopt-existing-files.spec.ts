import { copyFile, mkdir, stat, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { integrationEnvironment } from '../helpers/environment';
import { waitForJob } from '../helpers/jobs';
import { mocks } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface Candidate {
    id: string;
    filePath: string;
    matchCount: number;
    notes?: string | null;
    status: string;
}

interface ContentItem {
    id: string;
    name: string;
    contentItem?: {
        fulfillment: string;
        qualityStatus?: string | null;
        mediaFiles: Array<{
            fileKind: string;
            filePath: string;
            mediaInfo?: { tracks?: Array<{ type: string }> } | null;
            mimeType?: string | null;
        }>;
    } | null;
}

interface ImportSubmission {
    queued: Array<{ candidateId: string; jobExecutionId: string }>;
    skipped: Array<{ candidateId: string; reason: string }>;
}

test.describe.serial('Journey 19: discover and adopt existing main files', () => {
    test('discovers, reviews and imports accepted files without touching unsupported files', async ({ page }) => {
        test.setTimeout(420_000);

        let movieRoot!: { id: string; path: string };
        const api = await restoreCheckpoint('configured-baseline', async (restored) => {
            movieRoot = (await restored.get<Array<{ id: string; name: string; path: string }>>('/api/root-folders')).find(
                (root) => root.name === 'Movies',
            )!;
            expect(movieRoot.path).toBe('/library/movies');
            expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
        });

        const fixtureFolder = resolve(integrationEnvironment.paths.library, 'movies', 'discovery-journey');
        const ambiguousName = 'Discovery Ambiguous (2024).mkv';
        const certainName = 'Discovery Certain (2025).mkv';
        const unsupportedName = 'Discovery Unsupported.xyz';
        const ambiguousHostPath = resolve(fixtureFolder, ambiguousName);
        const certainHostPath = resolve(fixtureFolder, certainName);
        const unsupportedHostPath = resolve(fixtureFolder, unsupportedName);
        const ambiguousAppPath = `/library/movies/discovery-journey/${ambiguousName}`;
        const certainAppPath = `/library/movies/discovery-journey/${certainName}`;
        const unsupportedAppPath = `/library/movies/discovery-journey/${unsupportedName}`;
        await mkdir(fixtureFolder, { recursive: true });
        await copyFile('/media-fixtures/movie-720p.mkv', ambiguousHostPath);
        await writeFile(unsupportedHostPath, 'unsupported discovery fixture', 'utf8');

        await test.step('start discovery with the CLI and inspect its completed job through the API and UI', async () => {
            await armDiscovery(
                'discovery-ambiguous',
                'Discovery Ambiguous',
                'discovery-ambiguous-good',
                true,
            );
            const executionId = await cli.json<string>([
                'jobs',
                'enqueue',
                '--json',
                JSON.stringify({ jobType: 'RootFolderContentDiscovery', parameters: { rootFolderId: movieRoot.id } }),
            ]);
            const execution = await waitForJob(api, executionId, 120_000);
            expect(execution).toMatchObject({ status: 'Completed', progress: 100 });
            expect(execution.parentExecutionId).toBeNull();

            const listed = await cli.json<Paged<{ id: string; jobType: string; status: string }>>([
                'jobs',
                'list',
                '--job-type',
                'RootFolderContentDiscovery',
                '--page-size',
                '100',
            ]);
            expect(listed.items).toContainEqual(
                expect.objectContaining({ id: executionId, jobType: 'RootFolderContentDiscovery', status: 'Completed' }),
            );

            await loginUi(page);
            await page.goto('/admin/jobs');
            await expect(page.locator(`[data-execution-id="${executionId}"]`)).toBeVisible();
            await expect(page.getByText('Root Folder Content Discovery', { exact: true }).first()).toBeVisible();
        });

        const ambiguous = await test.step('read the ambiguous candidate and correct it through the CLI review surface', async () => {
            const candidates = await api.get<Paged<Candidate>>(
                `/api/import/candidates?rootFolderId=${movieRoot.id}&pageSize=100`,
            );
            const found = candidates.items.find((candidate) => candidate.filePath === ambiguousAppPath);
            expect(found).toMatchObject({ matchCount: 0, status: 'Pending' });
            expect(found?.notes).toMatch(/results have similar scores/i);
            expect(candidates.items.some((candidate) => candidate.filePath === unsupportedAppPath)).toBe(false);

            const matched = await cli.json<Candidate>([
                'library-import',
                'match',
                found!.id,
                '--json',
                JSON.stringify({
                    providerType: mocks.hierarchyPrimary.slug,
                    externalIdKey: 'tmdb',
                    externalId: 'discovery-ambiguous-good',
                    title: 'Discovery Ambiguous',
                    levelSlug: 'movie',
                }),
            ]);
            expect(matched).toMatchObject({ id: found!.id, matchCount: 1, status: 'Pending' });
            return matched;
        });

        const firstContent = await test.step('import the reviewed candidate and prove its file semantics and location', async () => {
            const submission = await api.post<ImportSubmission>('/api/import/candidates/bulk-accept', {
                ids: [ambiguous.id],
            });
            expect(submission.skipped).toHaveLength(0);
            expect(submission.queued).toHaveLength(1);
            expect((await waitForJob(api, submission.queued[0]!.jobExecutionId, 120_000)).status).toBe('Completed');
            const imported = await findContent(api, 'Discovery Ambiguous');
            expect(imported.contentItem).toMatchObject({ fulfillment: 'Fulfilled' });
            expect(imported.contentItem?.qualityStatus).not.toBeUndefined();
            const withMediaInfo = await until(
                'Discovery Ambiguous media info',
                async () => {
                    const current = await api.get<ContentItem>(`/api/content/${imported.id}`);
                    return current.contentItem?.mediaFiles.some((file) => file.mediaInfo?.tracks?.length)
                        ? current
                        : undefined;
                },
                60_000,
                500,
            );
            expect(withMediaInfo.contentItem?.mediaFiles).toContainEqual(
                expect.objectContaining({
                    fileKind: 'Video',
                    filePath: ambiguousAppPath,
                    mediaInfo: expect.objectContaining({
                        tracks: expect.arrayContaining([expect.objectContaining({ type: 'Video' })]),
                    }),
                }),
            );
            expect((await stat(ambiguousHostPath)).size).toBeGreaterThan(20_000);

            await page.goto(`/library/${withMediaInfo.id}?tab=activity`);
            await expect(page.locator('[data-component="activity-tab"]')).toContainText(/File scanned|Import|Media info/i);
            return withMediaInfo;
        });

        await test.step('discover and import a confidently matched second candidate', async () => {
            await copyFile('/media-fixtures/movie-720p.mkv', certainHostPath);
            await armDiscovery('discovery-certain', 'Discovery Certain', 'discovery-certain', false);
            const executionId = await cli.json<string>([
                'jobs',
                'enqueue',
                '--json',
                JSON.stringify({ jobType: 'RootFolderContentDiscovery', parameters: { rootFolderId: movieRoot.id } }),
            ]);
            expect((await waitForJob(api, executionId, 120_000)).status).toBe('Completed');

            const candidates = await api.get<Paged<Candidate>>(
                `/api/import/candidates?rootFolderId=${movieRoot.id}&pageSize=100`,
            );
            const certain = candidates.items.find((candidate) => candidate.filePath === certainAppPath);
            expect(certain).toMatchObject({ matchCount: 1, status: 'Pending' });
            expect(candidates.items.some((candidate) => candidate.filePath === ambiguousAppPath)).toBe(false);
            expect(candidates.items.some((candidate) => candidate.filePath === unsupportedAppPath)).toBe(false);

            const submission = await api.post<ImportSubmission>('/api/import/candidates/bulk-accept', {
                ids: [certain!.id],
            });
            expect(submission.queued).toHaveLength(1);
            expect((await waitForJob(api, submission.queued[0]!.jobExecutionId, 120_000)).status).toBe('Completed');
            const imported = await findContent(api, 'Discovery Certain');
            expect(imported.contentItem?.fulfillment).toBe('Fulfilled');
            expect(imported.contentItem?.mediaFiles).toContainEqual(
                expect.objectContaining({ filePath: certainAppPath, fileKind: 'Video' }),
            );
            expect((await stat(certainHostPath)).size).toBeGreaterThan(20_000);
        });

        await test.step('prove the unsupported file created neither a candidate nor a media-file link', async () => {
            const candidates = await api.get<Paged<Candidate>>(
                `/api/import/candidates?rootFolderId=${movieRoot.id}&pageSize=100`,
            );
            expect(candidates.items.some((candidate) => candidate.filePath === unsupportedAppPath)).toBe(false);
            const content = await api.get<Paged<ContentItem>>('/api/content?pageSize=500');
            expect(
                content.items.some((item) =>
                    item.contentItem?.mediaFiles.some((file) => file.filePath === unsupportedAppPath),
                ),
            ).toBe(false);
            expect((await stat(unsupportedHostPath)).size).toBeGreaterThan(0);
            expect(firstContent.id).toBeTruthy();
        });
    });
});

async function armDiscovery(
    scenario: string,
    title: string,
    externalId: string,
    ambiguous: boolean,
): Promise<void> {
    const result = (id: string) => ({
        providerType: mocks.hierarchyPrimary.slug,
        externalIdSource: 'tmdb',
        externalId: id,
        contentType: 'movie',
        level: 'movie',
        thumbnailUrl: null,
        title,
        language: 'en',
        overview: `${title} discovery metadata.`,
        releaseDate: title.includes('Certain') ? '2025-01-01T00:00:00Z' : '2024-01-01T00:00:00Z',
    });
    await mocks.hierarchyPrimary.useScenario(scenario);
    await mocks.hierarchyPrimary.write(
        'searchAsync.json',
        ambiguous ? [result(externalId), result(`${externalId}-tie`)] : [result(externalId)],
    );
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
        providerType: mocks.hierarchyPrimary.slug,
        source: `tmdb:${externalId}`,
        title,
        overview: `${title} discovery metadata.`,
        language: 'en',
        publishDate: title.includes('Certain') ? '2025-01-01T00:00:00Z' : '2024-01-01T00:00:00Z',
        externalIds: [{ source: 'tmdb', value: externalId, ordinal: 0 }],
    });
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', []);
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', []);
    await mocks.hierarchyPrimary.clearCalls();
}

async function findContent(api: ApiClient, title: string): Promise<ContentItem> {
    return until(
        `${title} imported content`,
        async () => {
            const content = await api.get<Paged<ContentItem>>(`/api/content?search=${encodeURIComponent(title)}&pageSize=20`);
            return content.items.find(
                (item) => item.name === title && item.contentItem?.mediaFiles.length && item.contentItem.fulfillment === 'Fulfilled',
            );
        },
        90_000,
        500,
    );
}
