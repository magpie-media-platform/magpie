import { rm, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { waitForJob } from '../helpers/jobs';
import { loginUi } from '../helpers/ui';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface MediaFile {
    id: string;
    fileKind: string;
    filePath: string;
}

interface ContentItem {
    id: string;
    name: string;
    monitoring: boolean | null;
    contentItem?: {
        fulfillment: string;
        mediaFiles: MediaFile[];
    } | null;
}

interface RootFolder {
    id: string;
    contentType: string;
    name: string;
    path: string;
    renameTemplate?: string | null;
}

interface BulkResult {
    count: number;
    error?: string | null;
}

const movieMainAppPath = '/library/movies/Journey Feature (2024)/Journey Feature.mkv';
const movieSubtitleAppPath = '/library/movies/Journey Feature (2024)/Journey Feature.en.srt';
const episodeAppPath =
    '/library/tv/Journey Series (2024)/Journey Series - S01E01 - Journey Episode 01.mkv';
const trackAppPath = '/library/music/Journey Artist/Journey Album (2024)/01 - Journey Track 1.flac';
const bookAppPath = '/library/books/ebooks/Journey Book (2024)/Journey Book (2024).pdf';

const movieMainHostPath = resolve(
    integrationEnvironment.paths.library,
    'movies',
    'Journey Feature (2024)',
    'Journey Feature.mkv',
);
const movieSubtitleHostPath = resolve(
    integrationEnvironment.paths.library,
    'movies',
    'Journey Feature (2024)',
    'Journey Feature.en.srt',
);
const episodeHostPath = resolve(
    integrationEnvironment.paths.library,
    'tv',
    'Journey Series (2024)',
    'Journey Series - S01E01 - Journey Episode 01.mkv',
);
const trackHostPath = resolve(
    integrationEnvironment.paths.library,
    'music',
    'Journey Artist',
    'Journey Album (2024)',
    '01 - Journey Track 1.flac',
);
const bookHostPath = resolve(
    integrationEnvironment.paths.library,
    'books',
    'ebooks',
    'Journey Book (2024)',
    'Journey Book (2024).pdf',
);

test.describe.serial('Journey 14: make room', () => {
    test('prunes files and content while preserving the remaining library', async ({ page }) => {
        test.setTimeout(420_000);

        let api!: ApiClient;
        let movie!: ContentItem;
        let episode!: ContentItem;
        let series!: ContentItem;
        let season!: ContentItem;
        let artist!: ContentItem;
        let album!: ContentItem;
        let track!: ContentItem;
        let book!: ContentItem;
        let catalog44!: ContentItem;
        let catalog45!: ContentItem;
        let catalog46!: ContentItem;
        let catalog47!: ContentItem;
        let movieRoot!: RootFolder;
        let musicRoot!: RootFolder;
        let movieMain!: MediaFile;
        let movieSubtitle!: MediaFile;
        let episodeFile!: MediaFile;
        let trackFile!: MediaFile;

        await test.step('restore the rich library and sign in as the manager', async () => {
            api = await restoreCheckpoint('journey-rich-library', async (restored) => {
                expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(87);
            });
            const library = await api.get<Paged<ContentItem>>('/api/content?pageSize=100');
            const byName = new Map(library.items.map((item) => [item.name, item]));
            movie = byName.get('Journey Feature')!;
            episode = byName.get('Journey Episode 01')!;
            series = byName.get('Journey Series')!;
            season = byName.get('Journey Season 1')!;
            artist = byName.get('Journey Artist')!;
            album = byName.get('Journey Album')!;
            track = byName.get('Journey Track 1')!;
            book = byName.get('Journey Book')!;
            catalog44 = byName.get('Journey Catalog 44')!;
            catalog45 = byName.get('Journey Catalog 45')!;
            catalog46 = byName.get('Journey Catalog 46')!;
            catalog47 = byName.get('Journey Catalog 47')!;
            expect([
                movie,
                episode,
                series,
                season,
                artist,
                album,
                track,
                book,
                catalog44,
                catalog45,
                catalog46,
                catalog47,
            ]).not.toContain(undefined);

            const roots = await api.get<RootFolder[]>('/api/root-folders');
            movieRoot = roots.find((root) => root.name === 'Movies')!;
            musicRoot = roots.find((root) => root.name === 'Music')!;
            expect(movieRoot).toMatchObject({ contentType: 'movie', path: '/library/movies' });
            expect(musicRoot).toMatchObject({ contentType: 'music', path: '/library/music', renameTemplate: null });

            movieMain = movie.contentItem!.mediaFiles.find((file) => file.filePath === movieMainAppPath)!;
            movieSubtitle = movie.contentItem!.mediaFiles.find((file) => file.filePath === movieSubtitleAppPath)!;
            episodeFile = episode.contentItem!.mediaFiles.find((file) => file.filePath === episodeAppPath)!;
            trackFile = track.contentItem!.mediaFiles.find((file) => file.filePath === trackAppPath)!;
            expect(movieMain).toMatchObject({ fileKind: 'Video', filePath: movieMainAppPath });
            expect(movieSubtitle).toMatchObject({ filePath: movieSubtitleAppPath });
            expect(episodeFile).toMatchObject({ fileKind: 'Video', filePath: episodeAppPath });
            expect(trackFile).toMatchObject({ fileKind: 'Audio', filePath: trackAppPath });
            expect(book.contentItem?.mediaFiles).toContainEqual(expect.objectContaining({ filePath: bookAppPath }));

            expect((await stat(movieMainHostPath)).size).toBeGreaterThan(20_000);
            expect((await stat(movieSubtitleHostPath)).size).toBeGreaterThan(0);
            expect((await stat(episodeHostPath)).size).toBeGreaterThan(20_000);
            expect((await stat(trackHostPath)).size).toBeGreaterThan(0);
            expect((await stat(bookHostPath)).size).toBeGreaterThan(0);
            await loginUi(page);
        });

        await test.step('unlink a file from its item and leave it on disk', async () => {
            await page.goto(`/library/${movie.id}?tab=files`);
            await page.locator(`[data-action="unlink-file"][data-file-id="${movieSubtitle.id}"]`).click();
            const dialog = page.locator('[data-component="delete-confirm-dialog"]');
            await expect(dialog).toContainText('The host file stays on disk.');
            const unlinked = page.waitForResponse(
                (response) =>
                    response.request().method() === 'POST' &&
                    response.url().endsWith(`/api/content/${movie.id}/files/${movieSubtitle.id}/unlink`),
            );
            await dialog.locator('[data-action="confirm-delete"]').click();
            expect((await unlinked).status()).toBe(204);
            expect((await stat(movieSubtitleHostPath)).size).toBeGreaterThan(0);
            expect(
                (await api.get<ContentItem>(`/api/content/${movie.id}`)).contentItem?.mediaFiles.some(
                    (file) => file.id === movieSubtitle.id,
                ),
            ).toBe(false);
        });

        await test.step('delete another file from its item and prove it is gone from disk', async () => {
            await page.goto(`/library/${series.id}/${season.id}/${episode.id}?tab=files`);
            await page.locator(`[data-action="delete-file"][data-file-id="${episodeFile.id}"]`).click();
            const dialog = page.locator('[data-component="delete-confirm-dialog"]');
            await expect(dialog).toContainText('This removes the file from disk and the library.');
            const deleted = page.waitForResponse(
                (response) =>
                    response.request().method() === 'DELETE' &&
                    response.url().endsWith(`/api/content/${episode.id}/files/${episodeFile.id}`),
            );
            await dialog.locator('[data-action="confirm-delete"]').click();
            expect((await deleted).status()).toBe(204);
            await expect(stat(episodeHostPath)).rejects.toMatchObject({ code: 'ENOENT' });
            const current = await api.get<ContentItem>(`/api/content/${episode.id}`);
            expect(current.contentItem).toMatchObject({ fulfillment: 'Missing' });
            expect(current.contentItem?.mediaFiles.some((file) => file.id === episodeFile.id)).toBe(false);
        });

        await test.step('remove one item and keep its files', async () => {
            await page.goto(`/library/${book.id}`);
            await page.locator('[data-action="remove-from-library"]').first().click();
            const dialog = page.locator('[data-component="delete-confirm-dialog"]');
            await expect(dialog.getByRole('switch', { name: 'Also delete files from disk' })).toHaveAttribute(
                'aria-checked',
                'false',
            );
            const removed = page.waitForResponse(
                (response) =>
                    response.request().method() === 'DELETE' &&
                    response.url().endsWith(`/api/content/${book.id}?deleteFromLibrary=false`),
            );
            await dialog.locator('[data-action="confirm-delete"]').click();
            expect((await removed).status()).toBe(204);
            await expect(api.get(`/api/content/${book.id}`)).rejects.toMatchObject({ status: 404 });
            expect((await stat(bookHostPath)).size).toBeGreaterThan(0);
        });

        await test.step('remove another item after its file was deleted', async () => {
            await page.goto(`/library/${series.id}/${season.id}/${episode.id}`);
            await page.locator('[data-action="remove-from-library"]').first().click();
            const dialog = page.locator('[data-component="delete-confirm-dialog"]');
            await dialog.getByRole('switch', { name: 'Also delete files from disk' }).click();
            await expect(dialog.getByRole('switch', { name: 'Also delete files from disk' })).toHaveAttribute(
                'aria-checked',
                'true',
            );
            const removed = page.waitForResponse(
                (response) =>
                    response.request().method() === 'DELETE' &&
                    response.url().endsWith(`/api/content/${episode.id}?deleteFromLibrary=true`),
            );
            await dialog.locator('[data-action="confirm-delete"]').click();
            expect((await removed).status()).toBe(204);
            await expect(api.get(`/api/content/${episode.id}`)).rejects.toMatchObject({ status: 404 });
            await expect(stat(episodeHostPath)).rejects.toMatchObject({ code: 'ENOENT' });
        });

        await test.step('a stale link to the removed item renders a not-found state', async () => {
            await page.goto(`/library/${series.id}/${season.id}/${episode.id}`);
            await expect(page.getByRole('heading', { name: 'Content Not Found', level: 1, exact: true })).toBeVisible();
            await expect(
                page.getByText('The content you are looking for does not exist.', { exact: true }),
            ).toBeVisible();
            await expect(page.getByRole('link', { name: 'Back to Library' })).toHaveAttribute('href', '/library');
        });

        await test.step('unmonitor an item and prove it remains in the library', async () => {
            await page.goto('/library/all');
            const grid = page.getByRole('grid');
            await page.getByRole('button', { name: 'Filter by Title' }).click();
            await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey Catalog 47');
            await page.getByRole('button', { name: 'Apply' }).click();
            const rows = grid.locator('tbody tr[data-row-id]');
            await expect(rows).toHaveCount(1);
            await expect(rows.first()).toHaveAttribute('data-row-id', catalog47.id);
            await page.getByLabel('Select Journey Catalog 47', { exact: true }).click();
            const bulkBar = page.locator('[data-component="bulk-actions-toolbar"]');
            await expect(bulkBar).toContainText('1 selected');
            const unmonitored = page.waitForResponse(
                (response) =>
                    response.request().method() === 'PATCH' && response.url().endsWith('/api/content/bulk'),
            );
            await bulkBar.locator('[data-action="bulk-unmonitor"]').click();
            const response = await unmonitored;
            expect(response.ok()).toBe(true);
            expect(response.request().postDataJSON()).toEqual({
                ids: [catalog47.id],
                patch: { monitoring: { value: false } },
            });
            expect((await response.json()) as BulkResult).toEqual({ count: 1, error: null });
            expect(await api.get<ContentItem>(`/api/content/${catalog47.id}`)).toMatchObject({
                id: catalog47.id,
                monitoring: false,
            });
            await expect(grid.locator(`[data-row-id="${catalog47.id}"]`)).toBeVisible();
        });

        await test.step('select several items and delete them in one action', async () => {
            await page.goto('/library/all');
            const grid = page.getByRole('grid');
            await page.getByRole('button', { name: 'Filter by Title' }).click();
            await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey Catalog 4');
            await page.getByRole('button', { name: 'Apply' }).click();
            const rows = grid.locator('tbody tr[data-row-id]');
            await expect(rows).toHaveCount(9);
            await page.getByLabel('Select Journey Catalog 44', { exact: true }).click();
            await page.getByLabel('Select Journey Catalog 45', { exact: true }).click();
            await page.getByLabel('Select Journey Catalog 46', { exact: true }).click();
            const bulkBar = page.locator('[data-component="bulk-actions-toolbar"]');
            await expect(bulkBar).toContainText('3 selected');
            await bulkBar.locator('[data-action="bulk-delete"]').click();
            const dialog = page.locator('[data-component="delete-confirm-dialog"]');
            await expect(dialog).toContainText('3 items');
            const deleted = page.waitForResponse(
                (response) =>
                    response.request().method() === 'POST' && response.url().endsWith('/api/content/bulk/delete'),
            );
            await dialog.locator('[data-action="confirm-delete"]').click();
            const response = await deleted;
            expect(response.ok()).toBe(true);
            expect(response.request().postDataJSON()).toEqual({
                ids: [catalog44.id, catalog45.id, catalog46.id],
                deleteFromLibrary: false,
            });
            expect((await response.json()) as BulkResult).toEqual({ count: 3, error: null });
            await expect(rows).toHaveCount(6);
            for (const id of [catalog44.id, catalog45.id, catalog46.id]) {
                await expect(api.get(`/api/content/${id}`)).rejects.toMatchObject({ status: 404 });
            }
        });

        await test.step("rename an already-canonical file without moving it and keep the link valid", async () => {
            expect(musicRoot.renameTemplate).toBeNull();
            expect((await stat(trackHostPath)).size).toBeGreaterThan(0);
            const executionId = await api.post<string>('/api/jobs/enqueue', {
                jobType: 'MediaFileRename',
                parameters: { mediaFileId: trackFile.id },
            });
            expect((await waitForJob(api, executionId)).status).toBe('Completed');
            expect((await stat(trackHostPath)).size).toBeGreaterThan(0);
            const renamed = await api.get<ContentItem>(`/api/content/${track.id}`);
            expect(renamed.contentItem).toMatchObject({ fulfillment: 'Fulfilled' });
            expect(renamed.contentItem?.mediaFiles).toContainEqual(
                expect.objectContaining({ id: trackFile.id, filePath: trackAppPath }),
            );

            await page.goto(`/library/${artist.id}/${album.id}/${track.id}?tab=files`);
            await expect(page.getByRole('tabpanel')).toContainText('01 - Journey Track 1.flac');
        });

        await test.step("delete a file behind the application's back and expose the missing reconciliation", async () => {
            await rm(movieMainHostPath);
            await expect(stat(movieMainHostPath)).rejects.toMatchObject({ code: 'ENOENT' });
            const executionId = await api.post<string>('/api/jobs/enqueue', {
                jobType: 'RootFolderContentDiscovery',
                parameters: { rootFolderId: movieRoot.id },
            });
            expect((await waitForJob(api, executionId)).status).toBe('Completed');

            const stale = await api.get<ContentItem>(`/api/content/${movie.id}`);
            expect(stale.contentItem).toMatchObject({ fulfillment: 'Upgradable' });
            expect(stale.contentItem?.mediaFiles).toContainEqual(
                expect.objectContaining({ id: movieMain.id, filePath: movieMainAppPath }),
            );
        });

        await test.step('deleting a root folder that still owns content is refused', async () => {
            await expect(api.delete(`/api/root-folders/${musicRoot.id}`)).rejects.toMatchObject({
                status: 400,
                body: expect.stringContaining('A root folder containing content cannot be deleted.'),
            });
            expect(await api.get<RootFolder>(`/api/root-folders/${musicRoot.id}`)).toMatchObject({
                id: musicRoot.id,
                path: '/library/music',
            });
            expect(await api.get<ContentItem>(`/api/content/${track.id}`)).toMatchObject({
                id: track.id,
                contentItem: {
                    fulfillment: 'Fulfilled',
                    mediaFiles: [expect.objectContaining({ filePath: trackAppPath })],
                },
            });
            expect((await api.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(82);
            expect((await stat(bookHostPath)).size).toBeGreaterThan(0);
        });
    });
});
