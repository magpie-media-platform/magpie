import { rm } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { ApiError, type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { FIXTURES_ROOT } from '../helpers/fixtures';
import { type JobExecution, runImmediateJob, waitForJob } from '../helpers/jobs';
import { mocks } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface ContentItem {
    id: string;
    name: string;
}

interface Image {
    id: string;
    type: string;
    url: string;
    isPrimary: boolean;
}

interface Relationship {
    id: string;
    kind: string;
    qualifier?: string | null;
    relatedMetadataContainerId?: string | null;
}

interface MetadataContribution {
    id: string;
    providerType: string;
    title?: string | null;
    relationships: Relationship[];
}

interface Metadata {
    title?: string | null;
    overview?: string | null;
    birthDate?: string | null;
    images: Image[];
}

interface Subject {
    id: string;
    kind: string;
    metadataContainerId: string;
    dateLastRefreshed?: string | null;
    metadataContainer?: {
        contributions?: MetadataContribution[];
    } | null;
}

interface SavedFilter {
    id: string;
    name: string;
    criteria: unknown;
}

interface SubjectType {
    slug: string;
    displayName: string;
    pluralDisplayName: string;
}

interface RichIds {
    movie: string;
    artist: string;
    book: string;
    subject: string;
}

interface SubjectMetadataRequest {
    subjectId: string;
    subjectKind: string;
}

const subjectScenario = 'journey-12-subject-metadata';
const contentFilterName = 'Journey followed items';
const editedContentFilterName = 'Journey followed library';
const subjectFilterName = 'Journey people';
const followedTag = 'journey-follow';
const primaryImageUrl = 'https://assets.journey.invalid/journey-performer-primary.jpg';
const alternateImageUrl = 'https://assets.journey.invalid/journey-performer-alternate.jpg';

test.describe.serial('Journey 12: follow a person through the library', () => {
    test('manages subjects, metadata, tags, queries, and saved views', async ({ page }) => {
        test.setTimeout(900_000);

        let api!: ApiClient;
        let ids!: RichIds;
        let contentFilterId: string | undefined;
        let subjectFilterId: string | undefined;
        let switchedSubjectScenario = false;

        try {
            await test.step('restore the rich library and sign in', async () => {
                api = await restoreCheckpoint('journey-rich-library', async (restored) => {
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(87);
                    expect((await restored.get<Paged<unknown>>('/api/subjects?pageSize=1')).totalItems).toBe(1);
                });
                ids = await findRichIds(api);
                await loginUi(page);
            });

            await test.step('list and filter subjects with a query', async () => {
                await page.goto('/library/subjects/all');
                const rows = page.getByRole('grid').locator('tbody tr[data-row-id]');
                await expect(rows).toHaveCount(1);
                await expect(rows.first()).toHaveAttribute('data-row-id', ids.subject);

                await page.locator('[data-action="switch-to-advanced"]').click();
                const editor = page.locator('[data-component="filter-query-editor"] .cm-content');
                await editor.fill('title = "Journey Performer"');
                await expect(rows).toHaveCount(1);
                await expect(rows.first()).toHaveAttribute('data-row-id', ids.subject);
                await editor.fill('title = "No Such Journey Subject"');
                await expect(rows).toHaveCount(0);
                await expect(page.getByText('No matches found', { exact: true })).toBeVisible();
            });

            await test.step('subject types scope the subjects page', async () => {
                const types = await api.get<SubjectType[]>('/api/subject-types');
                expect(types.map((type) => type.slug).sort()).toEqual([
                    'book-publisher',
                    'music-label',
                    'person',
                    'studio',
                ]);

                await page.goto('/library/subjects/person');
                await expect(page.getByRole('heading', { name: 'People', exact: true })).toBeVisible();
                let rows = page.getByRole('grid').locator('tbody tr[data-row-id]');
                await expect(rows).toHaveCount(1);
                await expect(rows.first()).toHaveAttribute('data-row-id', ids.subject);

                await page.goto('/library/subjects/studio');
                await expect(page.getByRole('heading', { name: 'Studios', exact: true })).toBeVisible();
                rows = page.getByRole('grid').locator('tbody tr[data-row-id]');
                await expect(rows).toHaveCount(0);
                await expect(page.getByText('No studios yet', { exact: true })).toBeVisible();
            });

            await test.step('refresh a subject from its metadata provider', async () => {
                switchedSubjectScenario = true;
                await mocks.subjectMetadata.useScenario(subjectScenario);
                await mocks.subjectMetadata.armMetadata('person', subjectMetadataFixture());
                await mocks.subjectMetadata.clearCalls();

                const scanId = await api.post<string>('/api/jobs/enqueue', {
                    jobType: 'SubjectMetadataRefreshScan',
                });
                expect((await waitForJob(api, scanId)).status).toBe('Completed');
                await waitForNoActiveSubjectRefreshes(api);
                await refreshSubject(api, ids.subject);

                const calls = await mocks.subjectMetadata.waitForCalls<SubjectMetadataRequest>(1, {
                    method: 'tryGetMetadata',
                });
                expect(calls).toEqual(
                    expect.arrayContaining([
                        expect.objectContaining({
                            request: expect.objectContaining({
                                subjectId: ids.subject,
                                subjectKind: 'person',
                            }),
                        }),
                    ]),
                );
                const metadata = await api.get<Metadata>(`/api/subjects/${ids.subject}/metadata`);
                expect(metadata).toEqual(
                    expect.objectContaining({
                        title: 'Journey Performer',
                        overview: 'The subject metadata shim refreshed this performer.',
                        birthDate: '1985-07-08T00:00:00Z',
                    }),
                );
                expect(metadata.images.map((image) => image.url)).toEqual([primaryImageUrl, alternateImageUrl]);
                expect((await api.get<Subject>(`/api/subjects/${ids.subject}`)).dateLastRefreshed).toBeTruthy();
            });

            await test.step("set a subject's primary image and see it render", async () => {
                const metadata = await api.get<Metadata>(`/api/subjects/${ids.subject}/metadata`);
                const alternate = metadata.images.find((image) => image.url === alternateImageUrl);
                expect(alternate).toBeDefined();
                await api.post(`/api/subjects/${ids.subject}/images/${alternate!.id}/primary`);
                expect((await api.get<Metadata>(`/api/subjects/${ids.subject}/metadata`)).images).toContainEqual(
                    expect.objectContaining({ id: alternate!.id, isPrimary: true }),
                );

                await page.goto(`/subjects/${ids.subject}?tab=images`);
                await expect(page.locator('[data-component="subject-detail-header"] img')).toHaveAttribute(
                    'src',
                    alternateImageUrl,
                );
                const galleryItem = page
                    .locator('[data-component="images-gallery"] .images-gallery__item')
                    .filter({ has: page.locator(`img[src="${alternateImageUrl}"]`) });
                await expect(galleryItem).toHaveAttribute('data-primary', 'true');
            });

            await test.step('a hand-written subject override survives the next refresh', async () => {
                await api.patch(`/api/subjects/${ids.subject}/metadata`, {
                    title: { value: 'Hand Curated Journey Performer' },
                });
                await refreshSubject(api, ids.subject);
                expect((await api.get<Metadata>(`/api/subjects/${ids.subject}/metadata`)).title).toBe(
                    'Hand Curated Journey Performer',
                );
                expect(
                    await api.get<MetadataContribution[]>(`/api/subjects/${ids.subject}/metadata/contributions`),
                ).toEqual(
                    expect.arrayContaining([
                        expect.objectContaining({ providerType: 'user', title: 'Hand Curated Journey Performer' }),
                        expect.objectContaining({
                            providerType: mocks.subjectMetadata.slug,
                            title: 'Journey Performer',
                        }),
                    ]),
                );
            });

            await test.step('tag several items and filter the library on the tag', async () => {
                await Promise.all(
                    [ids.movie, ids.artist, ids.book].map((id) =>
                        api.patch(`/api/content/${id}`, { tags: { add: [followedTag] } }),
                    ),
                );
                expect(await api.get<Array<{ name: string }>>('/api/tags')).toContainEqual(
                    expect.objectContaining({ name: followedTag }),
                );
                for (const id of [ids.movie, ids.artist, ids.book]) {
                    expect(await api.get<Array<{ name: string }>>(`/api/tags?entityId=${id}`)).toContainEqual(
                        expect.objectContaining({ name: followedTag }),
                    );
                }

                await page.goto(`/library/all?filter=${encodeURIComponent(`tag has "${followedTag}"`)}`);
                const rows = page.getByRole('grid').locator('tbody tr');
                await expect(rows).toHaveCount(3);
                for (const id of [ids.movie, ids.artist, ids.book]) {
                    await expect(page.getByRole('grid').locator(`[data-row-id="${id}"]`)).toBeVisible();
                }
            });

            await test.step('build a filter in the simple editor, switch to advanced, and switch back', async () => {
                await page.goto('/library/all');
                const rows = page.getByRole('grid').locator('tbody tr');
                await page.locator('#sfb-title').fill('Journey');
                await page.locator('#sfb-tag').fill(followedTag);
                await expect(rows).toHaveCount(3);
                const query = `title contains "Journey" and tag has "${followedTag}"`;
                await expect(page.locator('.filter-bar__query-text')).toHaveText(query);

                await page.locator('[data-action="switch-to-advanced"]').click();
                const editor = page.locator('[data-component="filter-query-editor"] .cm-content');
                await expect(editor).toHaveText(query);
                await page.locator('[data-action="switch-to-simple"]').click();
                await expect(page.locator('#sfb-title')).toHaveValue('Journey');
                await expect(page.locator('#sfb-tag')).toHaveValue(followedTag);
                await expect(rows).toHaveCount(3);

                await page.locator('[data-action="save-filter"]').click();
                const dialog = page.getByRole('dialog', { name: 'Save Filter' });
                await dialog.getByLabel('Filter Name').fill(contentFilterName);
                await dialog.locator('[data-action="save-new-filter"]').click();
                await expect(dialog).toBeHidden();
                contentFilterId = await until(
                    'the followed-items content filter',
                    async () =>
                        (await api.get<SavedFilter[]>('/api/content-filters')).find(
                            (filter) => filter.name === contentFilterName,
                        )?.id,
                    30_000,
                    250,
                );
            });

            await test.step('edit and delete the filter from the filters page', async () => {
                await page.goto('/filters');
                let row = page.locator(`[data-filter-id="${contentFilterId}"]`);
                await expect(row).toContainText(contentFilterName);
                await row.locator('[data-action="edit"]').click();
                const form = page.locator('[data-component="filter-form"]');
                await form.locator('#filter-name').fill(editedContentFilterName);
                await form.locator('[data-action="save-filter"]').click();
                await expect(form).toBeHidden();
                row = page.locator(`[data-filter-id="${contentFilterId}"]`);
                await expect(row).toContainText(editedContentFilterName);

                await row.locator('[data-action="more-actions"]').click();
                await page.locator(`[data-action="delete"][data-target="${contentFilterId}"]`).click();
                const confirm = page.locator('[data-component="delete-confirm-dialog"]');
                await confirm.locator('[data-action="confirm-delete"]').click();
                await expect(row).toHaveCount(0);
                expect(await api.get<SavedFilter[]>('/api/content-filters')).not.toContainEqual(
                    expect.objectContaining({ id: contentFilterId }),
                );
                contentFilterId = undefined;
            });

            await test.step('the parse and format endpoints accept a good query and locate a bad one', async () => {
                const contentQuery = `title contains "Journey" and tag has "${followedTag}"`;
                const contentCriteria = await api.post<unknown>('/api/magpie-ql/content/parse', {
                    query: contentQuery,
                });
                expect(await api.post<{ query: string }>('/api/magpie-ql/content/format', contentCriteria)).toEqual({
                    query: `Title contains "Journey" and Tag has "${followedTag}"`,
                });

                const subjectQuery = 'kind = "person" and title contains "Hand Curated"';
                const subjectCriteria = await api.post<unknown>('/api/magpie-ql/subject/parse', {
                    query: subjectQuery,
                });
                expect(await api.post<{ query: string }>('/api/magpie-ql/subject/format', subjectCriteria)).toEqual({
                    query: 'Kind = "person" and Title contains "Hand Curated"',
                });

                let parseError: ApiError | undefined;
                try {
                    await api.post('/api/magpie-ql/subject/parse', { query: 'title =' });
                } catch (error) {
                    if (error instanceof ApiError) {
                        parseError = error;
                    } else {
                        throw error;
                    }
                }
                expect(parseError?.status).toBe(400);
                expect(JSON.parse(parseError!.body)).toEqual(
                    expect.objectContaining({ message: 'Expected value', position: 7 }),
                );
            });

            await test.step('a subject filter scopes the subjects page', async () => {
                const query = 'kind = "person" and title contains "Hand Curated"';
                const criteria = await api.post<unknown>('/api/magpie-ql/subject/parse', { query });
                const created = await api.post<SavedFilter>('/api/subject-filters', {
                    name: subjectFilterName,
                    criteria,
                });
                subjectFilterId = created.id;
                expect(await api.get<SavedFilter[]>('/api/subject-filters')).toContainEqual(
                    expect.objectContaining({ id: created.id, name: subjectFilterName }),
                );
                const formatted = await api.post<{ query: string }>('/api/magpie-ql/subject/format', created.criteria);
                expect(formatted.query).toBe('Kind = "person" and Title contains "Hand Curated"');

                await page.goto(`/library/subjects/all?filter=${encodeURIComponent(formatted.query)}`);
                const rows = page.getByRole('grid').locator('tbody tr');
                await expect(rows).toHaveCount(1);
                await expect(rows.first()).toHaveAttribute('data-row-id', ids.subject);
            });

            await test.step('deleting a subject nulls its relationships and the shell must be removed by hand', async () => {
                const mistake = await addRelatedSubject(api, ids.book, 'Journey Mistaken Subject', 'mistake');
                await api.delete(`/api/subjects/${mistake.subject.id}`);
                await expect(api.get(`/api/subjects/${mistake.subject.id}`)).rejects.toMatchObject({ status: 404 });
                expect(
                    (
                        await api.get<Paged<Subject>>(
                            `/api/subjects?relatedContentId=${ids.book}&pageSize=100`,
                        )
                    ).items.map((subject) => subject.id),
                ).not.toContain(mistake.subject.id);
                const contributions = await api.get<MetadataContribution[]>(
                    `/api/content/${ids.book}/metadata/contributions`,
                );
                expect(
                    contributions
                        .flatMap((contribution) => contribution.relationships)
                        .find((relationship) => relationship.id === mistake.relationship.id),
                ).toEqual(
                    expect.objectContaining({
                        id: mistake.relationship.id,
                        relatedMetadataContainerId: null,
                    }),
                );
                await api.patch(`/api/content/${ids.book}/metadata`, {
                    relationships: { remove: [mistake.relationship.id] },
                });
                expect(
                    (
                        await api.get<MetadataContribution[]>(`/api/content/${ids.book}/metadata/contributions`)
                    ).flatMap((contribution) => contribution.relationships),
                ).not.toContainEqual(expect.objectContaining({ id: mistake.relationship.id }));
            });

            await test.step('the orphaned provider-derived subject is pruned', async () => {
                const protectedSubject = await addRelatedSubject(api, ids.book, 'Journey Protected Subject', 'protected');
                await api.patch(`/api/content/${ids.book}/metadata`, {
                    relationships: { remove: [protectedSubject.relationship.id] },
                });
                await api.patch(`/api/subjects/${ids.subject}/metadata`, { title: { value: null } });
                expect(
                    (await api.get<MetadataContribution[]>(`/api/subjects/${ids.subject}/metadata/contributions`))
                        .some((contribution) => contribution.providerType === 'user'),
                ).toBe(false);
                await api.delete(`/api/content/${ids.movie}`);

                const execution = await runImmediateJob(api, 'SubjectMaintenance');
                expect(execution.status, execution.statusMessage ?? undefined).toBe('Completed');
                expect((await api.get<Subject>(`/api/subjects/${protectedSubject.subject.id}`)).id).toBe(
                    protectedSubject.subject.id,
                );
                let subjectError: ApiError | undefined;
                try {
                    await api.get(`/api/subjects/${ids.subject}`);
                } catch (error) {
                    if (error instanceof ApiError) {
                        subjectError = error;
                    } else {
                        throw error;
                    }
                }
                expect(subjectError?.status).toBe(404);

                await api.delete(`/api/subjects/${protectedSubject.subject.id}`);
            });
        } finally {
            const cleanupActions: Promise<unknown>[] = [];
            if (switchedSubjectScenario) {
                cleanupActions.push(mocks.subjectMetadata.useScenario('default'));
                cleanupActions.push(
                    rm(resolve(FIXTURES_ROOT, mocks.subjectMetadata.slug, 'scenarios', subjectScenario), {
                        recursive: true,
                        force: true,
                    }),
                );
            }
            if (api && contentFilterId) {
                cleanupActions.push(api.delete(`/api/content-filters/${contentFilterId}`));
            }
            if (api && subjectFilterId) {
                cleanupActions.push(api.delete(`/api/subject-filters/${subjectFilterId}`));
            }
            const cleanupResults = await Promise.allSettled(cleanupActions);
            expect(
                cleanupResults
                    .filter((result) => result.status === 'rejected')
                    .map((result) => String((result as PromiseRejectedResult).reason)),
            ).toEqual([]);
        }
    });
});

async function findRichIds(api: ApiClient): Promise<RichIds> {
    const byName = async (name: string): Promise<string> => {
        const page = await api.get<Paged<ContentItem>>(`/api/content?search=${encodeURIComponent(name)}&pageSize=100`);
        const item = page.items.find((candidate) => candidate.name === name);
        if (!item) {
            throw new Error(`Journey rich-library anchor ${name} was not found.`);
        }
        return item.id;
    };
    const [movie, artist, book] = await Promise.all([
        byName('Journey Feature'),
        byName('Journey Artist'),
        byName('Journey Book'),
    ]);
    const subjects = await api.get<Paged<Subject>>(
        `/api/subjects?filter=${encodeURIComponent('title = "Journey Performer"')}&pageSize=10`,
    );
    const subject = subjects.items.find((item) =>
        item.metadataContainer?.contributions?.some((contribution) => contribution.title === 'Journey Performer'),
    );
    if (!subject) {
        throw new Error('Journey rich-library subject anchor Journey Performer was not found.');
    }
    return { movie, artist, book, subject: subject.id };
}

function subjectMetadataFixture(): Record<string, unknown> {
    return {
        providerType: mocks.subjectMetadata.slug,
        source: 'imdb:nm-rich-performer',
        title: 'Journey Performer',
        overview: 'The subject metadata shim refreshed this performer.',
        language: 'en',
        birthDate: '1985-07-08T00:00:00Z',
        externalIds: [
            { source: 'imdb', value: 'nm-rich-performer', ordinal: 0 },
            { source: 'tmdb', value: 'rich-performer', ordinal: 1 },
        ],
        images: [
            {
                type: 'poster',
                mimeType: 'image/jpeg',
                url: primaryImageUrl,
                source: 'journey-12',
                width: 600,
                height: 900,
                isPrimary: true,
                ordinal: 0,
            },
            {
                type: 'poster',
                mimeType: 'image/jpeg',
                url: alternateImageUrl,
                source: 'journey-12',
                width: 1200,
                height: 1800,
                isPrimary: false,
                ordinal: 1,
            },
        ],
    };
}

async function refreshSubject(api: ApiClient, subjectId: string): Promise<void> {
    const executionId = await api.post<string>('/api/jobs/enqueue', {
        jobType: 'SubjectMetadataRefreshItem',
        parameters: { subjectId },
    });
    const execution = await waitForJob(api, executionId);
    expect(execution.status, execution.statusMessage ?? undefined).toBe('Completed');
}

async function waitForNoActiveSubjectRefreshes(api: ApiClient): Promise<void> {
    await until(
        'outstanding subject metadata refresh items to complete',
        async () => {
            const active = await api.get<Paged<JobExecution>>(
                '/api/jobs?jobType=SubjectMetadataRefreshItem&status=Queued&status=Running' +
                    '&status=CancellationRequested&pageSize=100',
            );
            return active.items.length === 0 ? true : undefined;
        },
        60_000,
        250,
    );
}

async function addRelatedSubject(
    api: ApiClient,
    contentId: string,
    title: string,
    qualifier: string,
): Promise<{ relationship: Relationship; subject: Subject }> {
    await api.patch(`/api/content/${contentId}/metadata`, {
        relationships: {
            add: [
                {
                    kind: { value: 'author' },
                    qualifier: { value: qualifier },
                    subjectName: title,
                    subjectKind: 'person',
                },
            ],
        },
    });
    const subject = await until(
        title,
        async () => {
            const page = await api.get<Paged<Subject>>(
                `/api/subjects?filter=${encodeURIComponent(`title = "${title}"`)}&pageSize=10`,
            );
            return page.items[0];
        },
        30_000,
        250,
    );
    const contributions = await api.get<MetadataContribution[]>(`/api/content/${contentId}/metadata/contributions`);
    const relationship = contributions
        .flatMap((contribution) => contribution.relationships)
        .find((candidate) => candidate.qualifier === qualifier);
    if (!relationship) {
        throw new Error(`Relationship ${qualifier} was not found.`);
    }
    return { relationship, subject };
}
