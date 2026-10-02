import { expect, test } from '@playwright/test';

import { ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { restartHost } from '../helpers/host';
import { mocks } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';

interface SettingsGroup {
    name: string;
    extensionId: string | null;
    settings: Array<{ key: string; value: string }>;
}

interface RootFolder {
    id: string;
    name: string;
    path: string;
    contentType: string;
}

interface DiskSpace {
    folders: Array<{
        id: string;
        name: string;
        path: string;
        totalBytes: number;
        freeBytes: number;
        usedBytes: number;
    }>;
    totalBytes: number;
    freeBytes: number;
    usedBytes: number;
}

interface ContentType {
    slug: string;
    levelDefinitions: Array<{ slug: string }>;
}

interface MimeMapping {
    extension: string;
    mimeType: string;
}

interface QualityTierList {
    id: string;
    name: string;
    qualityTierRanks: Array<{
        qualityTierId: string;
        ordinal: number;
        isCutoff: boolean;
        qualityTier: {
            name: string;
            criteria: {
                and: Array<{
                    items: Record<string, { operator: string; value?: string; values: string[]; negate: boolean }>;
                }>;
            };
        } | null;
    }>;
}

test.describe.serial('Journey 4: make it ours', () => {
    test('tunes the instance across settings, storage, file policy and quality tiers', async ({ page }) => {
        test.setTimeout(420_000);

        let api: ApiClient;

        await test.step('restore the configured baseline and read every administrator route', async () => {
            api = await restoreCheckpoint('configured-baseline', async (restored) => {
                expect((await restored.get<RootFolder[]>('/api/root-folders')).map((root) => root.name).sort()).toEqual([
                    'Books',
                    'Movies',
                    'Music',
                    'TV',
                ]);
            });
            await loginUi(page);

            const coreRoutes: Array<[string, string]> = [
                ['/admin/general', 'general'],
                ['/admin/notifications', 'notifications'],
                ['/admin/users', 'users'],
                ['/admin/indexers', 'indexers'],
                ['/admin/download-clients', 'download-clients'],
                ['/admin/remote-path-mappings', 'remote-path-mappings'],
                ['/admin/media-servers', 'media-servers'],
                ['/admin/root-folders', 'root-folders'],
                ['/admin/file-types', 'file-types'],
                ['/admin/content-types', 'content-types'],
                ['/admin/quality-tiers', 'quality-tiers'],
                ['/admin/jobs', 'jobs'],
                ['/admin/extensions', 'extensions'],
                ['/admin/database', 'backups'],
                ['/admin/system', 'system-status'],
            ];
            for (const [path, pageName] of coreRoutes) {
                await page.goto(path);
                await expect(page.locator(`[data-page="${pageName}"]`), `administrator route ${path}`).toBeVisible();
            }
        });

        await test.step('save general settings in the UI, read them back through the CLI, and patch them back', async () => {
            await page.goto('/admin/general');
            await page.getByLabel('Instance Name').fill('Journey Tune');
            await page.getByLabel('Base URL').fill('https://tune.magpie.invalid');
            await page.locator('[data-action="save-settings"]').click();
            await expect(page.getByText('Magpie updated the general settings.', { exact: true })).toBeVisible();

            expect(
                Object.fromEntries(
                    (await cli.json<SettingsGroup>(['settings', 'get-system', 'general'])).settings.map((setting) => [
                        setting.key,
                        setting.value,
                    ]),
                ),
            ).toMatchObject({ instanceName: 'Journey Tune', baseUrl: 'https://tune.magpie.invalid' });

            await cli.run([
                'settings',
                'patch-system',
                'general',
                '--json',
                JSON.stringify({
                    settings: {
                        update: {
                            instanceName: 'Journey Tune from CLI',
                            baseUrl: 'https://cli.tune.magpie.invalid',
                        },
                    },
                }),
            ]);
            await expect(page.getByLabel('Instance Name')).toHaveValue('Journey Tune from CLI');
            await expect(page.getByLabel('Base URL')).toHaveValue('https://cli.tune.magpie.invalid');
        });

        await test.step('invalid input is refused with a field message and nothing is persisted', async () => {
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

        await test.step('every settings group the instance advertises reads and patches', async () => {
            const groups = await api.get<SettingsGroup[]>('/api/settings/groups');
            expect(groups.length).toBeGreaterThan(0);
            for (const group of groups) {
                const path = group.extensionId
                    ? `/api/settings/groups/${group.extensionId}/${encodeURIComponent(group.name)}`
                    : `/api/settings/groups/system/${encodeURIComponent(group.name)}`;
                expect(await api.get<SettingsGroup>(path), `settings group ${group.name} reads`).toMatchObject({
                    name: group.name,
                    extensionId: group.extensionId,
                });
                await expect(
                    api.patch(path, { settings: { add: { journey04Probe: 'read-write' } } }),
                    `settings group ${group.name} patches`,
                ).resolves.toBeUndefined();
                expect(
                    (await api.get<SettingsGroup>(path)).settings,
                    `settings group ${group.name} returns its patch`,
                ).toContainEqual(expect.objectContaining({ key: 'journey04Probe', value: 'read-write' }));
                await api.patch(path, { settings: { remove: ['journey04Probe'] } });
            }
        });

        await test.step('renaming the instance shows up in the product', async () => {
            await api.patch('/api/settings/groups/system/general', {
                settings: {
                    update: {
                        instanceName: 'Make It Ours',
                        baseUrl: 'https://ours.magpie.invalid',
                    },
                },
            });
            await page.goto('/');
        });

        await test.step("the extension's settings group survives a host restart", async () => {
            const group = (await api.get<SettingsGroup[]>('/api/settings/groups')).find(
                (candidate) => candidate.name === 'Primary hierarchy shim',
            );
            expect(group).toBeDefined();
            expect(group!.extensionId).toBeTruthy();
            const originalProvider = group!.settings.find((setting) => setting.key === 'providerType')?.value;
            expect(originalProvider).toBe(mocks.hierarchyPrimary.slug);
            const path = `/api/settings/groups/${group!.extensionId}/${encodeURIComponent(group!.name)}`;
            await api.patch(path, {
                settings: { update: { providerType: mocks.hierarchySecondary.slug } },
            });

            api = await restartHost(api);

            expect(await api.get<SettingsGroup>(path)).toMatchObject({
                settings: [expect.objectContaining({ key: 'providerType', value: mocks.hierarchySecondary.slug })],
            });
            await api.patch(path, { settings: { update: { providerType: originalProvider! } } });
        });

        await test.step('the root folder form refuses a path that does not exist or cannot be written', async () => {
            const before = (await api.get<RootFolder[]>('/api/root-folders')).length;
            await page.goto('/admin/root-folders');
            await page.locator('[data-action="add-folder"]').first().click();
            const form = page.getByRole('dialog', { name: 'Add Root Folder' });
            await form.locator('[data-field="name"]').fill('Missing Journey Root');
            await form.getByLabel('Path').fill('/library/journey-04-missing');
            await form.getByRole('combobox', { name: 'Content Type' }).click();
            await page.getByRole('option', { name: 'Movie', exact: true }).click();
            await form.getByRole('combobox', { name: 'Default Quality Tiers' }).click();
            await page.getByRole('option', { name: 'Video - HD - video/*', exact: true }).click();

            const rejectedSave = page.waitForResponse(
                (response) => response.url().endsWith('/api/root-folders') && response.request().method() === 'POST',
            );
            await form.locator('[data-action="save"]').click();
            const response = await rejectedSave;
            expect(response.status()).toBe(400);
            expect(await response.json()).toMatchObject({ detail: expect.stringContaining('Directory does not exist.') });
            expect((await api.get<RootFolder[]>('/api/root-folders')).length).toBe(before);
            await expect(form).toBeVisible();
        });

        await test.step('the path picker browses the filesystem and refuses to escape its roots', async () => {
            const form = page.getByRole('dialog', { name: 'Add Root Folder' });
            await form.getByLabel('Path').fill('/library');
            await form.locator('[data-action="browse"]').click();
            const browser = page.locator('[data-component="file-browser"]');
            await expect(browser).toBeVisible();
            for (const path of ['/library/books', '/library/movies', '/library/music', '/library/tv']) {
                await expect(browser.locator(`[data-entry-path="${path}"]`), `filesystem entry ${path}`).toBeVisible();
            }

            await browser.locator('[data-action="cancel"]').click();
            await form.locator('[data-action="cancel"]').click();
            await page.locator('[data-component="unsaved-changes-dialog"] [data-action="discard"]').click();
            await expect(form).toBeHidden();
        });

        await test.step('each root folder reports its free space', async () => {
            const roots = await api.get<RootFolder[]>('/api/root-folders');
            const diskSpace = await api.get<DiskSpace>('/api/system/disk-space');
            expect(diskSpace.folders).toHaveLength(roots.length);
            for (const root of roots) {
                const folder = diskSpace.folders.find((candidate) => candidate.id === root.id);
                expect(folder, `disk space for ${root.name}`).toBeDefined();
                expect(folder!.name).toBe(root.name);
                expect(folder!.path).toBe(root.path);
                expect(folder!.freeBytes + folder!.usedBytes).toBe(folder!.totalBytes);

                const row = page.locator(`[data-component="disk-space-widget"] [data-folder-id="${root.id}"]`);
                await expect(row, `disk-space row for ${root.name}`).toBeVisible();
                await expect(row).toContainText(root.name);
                await expect(row).toContainText(root.path);
                await expect(row).toContainText(/\d+(?:\.\d+)? (?:B|KB|MB|GB|TB|PB) free/);
            }
        });

        await test.step('the content types page shows every level and where its files live', async () => {
            const contentTypes = await api.get<ContentType[]>('/api/content-types');
            expect(contentTypes.map((contentType) => contentType.slug).sort()).toEqual(['book', 'movie', 'music', 'tv']);
            await page.goto('/admin/content-types');
            for (const contentType of contentTypes) {
                const card = page.locator(
                    `[data-testid="content-types__type"][data-content-type="${contentType.slug}"]`,
                );
                await expect(card, `content type ${contentType.slug}`).toBeVisible();
                for (const level of contentType.levelDefinitions) {
                    await expect(
                        card.locator(`[data-testid="content-types__level"][data-level-slug="${level.slug}"]`),
                        `${contentType.slug} level ${level.slug}`,
                    ).toBeVisible();
                }
            }
        });

        await test.step('add, change and remove a mime mapping', async () => {
            await page.goto('/admin/file-types');
            await page.locator('[data-tab="mime-registry"]').click();
            const section = page.locator('[data-section="mime-registry"]');
            await section.locator('[data-action="add-mime-mapping"]').click();
            const addForm = page.locator('[data-form="add-mime-mapping"]');
            await addForm.locator('[data-field="mime-extension"]').fill('journey04');
            await addForm.locator('[data-field="mime-type"]').fill('application/x-magpie-journey');
            await addForm.locator('[data-action="submit"]').click();
            await expect(addForm).toBeHidden();
            expect(await api.get<MimeMapping[]>('/api/system/file-types/mime')).toContainEqual(
                expect.objectContaining({
                    extension: '.journey04',
                    mimeType: 'application/x-magpie-journey',
                }),
            );

            let row = section.getByRole('row').filter({ hasText: '.journey04' });
            const pager = section.locator('[data-component="mime-pagination"]');
            const pageIndicator = pager.getByText(/^Page \d+ of \d+$/);
            const pageCountText = (await pager.count()) === 0 ? 'Page 1 of 1' : await pageIndicator.textContent();
            const pageCount = Number(pageCountText?.match(/of (\d+)$/)?.[1]);
            expect(pageCount).toBeGreaterThan(0);
            for (let pageNumber = 1; pageNumber < pageCount && (await row.count()) === 0; pageNumber++) {
                await pager.locator('[data-action="next-page"]').click();
                await expect(pageIndicator).toHaveText(`Page ${pageNumber + 1} of ${pageCount}`);
            }
            await expect(row).toBeVisible();
            await row.locator('[data-action="edit"]').click();
            const editForm = page.locator('[data-form="edit-mime-mapping"]');
            await editForm.locator('[data-field="mime-edit-input"]').fill('application/x-magpie-tuned');
            await editForm.locator('[data-action="save"]').click();
            await expect(editForm).toBeHidden();
            expect(await api.get<MimeMapping[]>('/api/system/file-types/mime')).toContainEqual(
                expect.objectContaining({
                    extension: '.journey04',
                    mimeType: 'application/x-magpie-tuned',
                }),
            );

            row = section.getByRole('row').filter({ hasText: '.journey04' });
            await row.locator('[data-action="delete"]').click();
            await page.locator('[data-component="delete-confirm-dialog"] [data-action="confirm-delete"]').click();
            await expect(row).toBeHidden();
            expect(
                (await api.get<MimeMapping[]>('/api/system/file-types/mime')).some(
                    (mapping) => mapping.extension === '.journey04',
                ),
            ).toBe(false);
        });

        await test.step('build a quality tier in the editor, clone it, give it a cutoff, and save', async () => {
            await page.goto('/admin/quality-tiers');
            await page.locator('[data-action="add-tier-list"]').click();
            const listForm = page.locator('[data-component="tier-list-form"]');
            await listForm.locator('[data-field="name"]').fill('Journey Tune Tiers');
            await listForm.locator('[data-action="save"]').click();
            await expect(page).toHaveURL(/\/admin\/quality-tiers\/[0-9a-f-]+$/);

            const tierList = (await api.get<QualityTierList[]>('/api/quality-tier-lists')).find(
                (candidate) => candidate.name === 'Journey Tune Tiers',
            );
            expect(tierList).toBeDefined();
            await page.locator('[data-component="tier-list-detail-header"] [data-action="add-tier"]').first().click();
            const tierForm = page.locator('[data-component="tier-form"]');
            await tierForm.locator('[data-field="name"]').fill('Journey HD');
            await tierForm.locator('[data-action="add-criterion"]').click();
            const criterion = tierForm.locator('[data-component="criteria-editor"]');
            await criterion.locator('[data-field="field"]').fill('resolution');
            await criterion.locator('[data-field="value"]').fill('1080p');
            await tierForm.locator('[data-action="save"]').click();
            await expect(tierForm).toBeHidden();

            const created = await api.get<QualityTierList>(`/api/quality-tier-lists/${tierList!.id}`);
            const source = created.qualityTierRanks.find((rank) => rank.qualityTier?.name === 'Journey HD');
            expect(source).toBeDefined();
            const sourceCard = page.locator(`[data-tier-id="${source!.qualityTierId}"]`);
            await sourceCard.locator('[data-action="clone-tier"]').click();
            await expect(tierForm).toBeVisible();
            await tierForm.locator('[data-field="name"]').fill('Journey UHD');
            await tierForm.locator('[data-component="criteria-editor"] [data-field="value"]').fill('2160p');
            const cutoff = tierForm.locator('[data-field="isCutoff"]');
            await cutoff.click();
            await expect(cutoff).toHaveAttribute('aria-checked', 'true');
            await tierForm.locator('[data-action="save"]').click();
            await expect(tierForm).toBeHidden();

            const saved = await api.get<QualityTierList>(`/api/quality-tier-lists/${tierList!.id}`);
            expect(
                [...saved.qualityTierRanks]
                    .sort((left, right) => left.ordinal - right.ordinal)
                    .map((rank) => ({
                        ordinal: rank.ordinal,
                        isCutoff: rank.isCutoff,
                        name: rank.qualityTier?.name,
                        criteria: rank.qualityTier?.criteria,
                    })),
            ).toEqual([
                {
                    ordinal: 0,
                    isCutoff: false,
                    name: 'Journey HD',
                    criteria: {
                        and: [
                            {
                                items: {
                                    resolution: { operator: 'Equals', value: '1080p', values: [], negate: false },
                                },
                            },
                        ],
                    },
                },
                {
                    ordinal: 1,
                    isCutoff: true,
                    name: 'Journey UHD',
                    criteria: {
                        and: [
                            {
                                items: {
                                    resolution: { operator: 'Equals', value: '2160p', values: [], negate: false },
                                },
                            },
                        ],
                    },
                },
            ]);
        });
    });
});
