import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

const mockParseMagpieQl = vi.fn();
const mockFormatMagpieQlCriteria = vi.fn();

vi.mock('@/lib/magpieQl', () => ({
    parseMagpieQl: (...args: unknown[]) => mockParseMagpieQl(...args),
    formatMagpieQlCriteria: (...args: unknown[]) => mockFormatMagpieQlCriteria(...args),
}));

const mockUseLibraryTypes = vi.fn();

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...original,
        useLibraryTypes: () => mockUseLibraryTypes(),
    };
});

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    buildUrl: (path: string, params?: Record<string, unknown>) => {
        if (!params) {
            return path;
        }
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const query = searchParams.toString();
        return query ? `${path}?${query}` : path;
    },
}));

import { apiGet } from '@/lib/api';
import type { QualityTierListOption, RootFolderOption, TagOption } from '@/types';
import type { WishlistFormValues } from './WishlistForm';
import { WishlistForm } from './WishlistForm';

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
}

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = createTestQueryClient();
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <TooltipProvider>{children}</TooltipProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

const sampleCriteria = { contentHierarchyItem: { and: [{ genre: 'Action' }] } };

const mockQualityTierLists: QualityTierListOption[] = [
    { id: 'qtl-1', name: 'HD', mimeTypes: ['video/*'] },
    { id: 'qtl-2', name: '4K Ultra HD', mimeTypes: [] },
];

const mockTags: TagOption[] = [
    { id: 'tag-1', name: 'Movies' },
    { id: 'tag-3', name: 'Documentary' },
];

const mockRootFolders: RootFolderOption[] = [
    { id: 'rf-1', name: 'Movies HD', path: '/media/movies', contentType: 'movie' },
    { id: 'rf-2', name: 'Movies 4K', path: '/media/movies-4k', contentType: 'movie' },
];

const baseInitialValues: WishlistFormValues = {
    id: 'wl-1',
    name: 'My Wishlist',
    filterId: null,
    contentType: 'movie',
    criteria: {},
    qualityTierListId: null,
    rootFolderId: null,
    enabled: true,
    monitoring: null,
    tagIds: [],
};

beforeEach(() => {
    vi.restoreAllMocks();
    vi.clearAllMocks();
    mockUseLibraryTypes.mockReturnValue({
        contentTypes: [
            { id: 'movie', name: 'Movies', slug: 'movie', levels: [] },
            { id: 'episode', name: 'Episodes', slug: 'episode', levels: [] },
        ],
        subjectTypes: [],
        isLoading: false,
        error: null,
        refetch: vi.fn(),
    });
    mockParseMagpieQl.mockImplementation(async (query: string) => {
        const match = /contentType\s*=\s*"([^"]*)"/i.exec(query ?? '');
        if (match) {
            return { criteria: { contentHierarchyItem: { and: [{ contentType: match[1] }] } } };
        }
        return { criteria: sampleCriteria };
    });
    mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'genre has "Action"' });
    vi.mocked(apiGet).mockImplementation((url: string) =>
        url.startsWith('/quality-tier-lists')
            ? Promise.resolve([
                  {
                      id: 'qtl-compatible',
                      name: 'Compatible',
                      mimeTypes: ['video/*'],
                      upgradeAllowed: true,
                      createdAt: '2026-01-01T00:00:00Z',
                      qualityTierRanks: [],
                  },
              ])
            : Promise.resolve({
                  matches: [],
                  pageItemCount: 0,
                  existsInLibraryCount: 0,
              }),
    );
});

function saveButton() {
    return document.querySelector('[data-action="save"]') as HTMLElement;
}

function previewButton() {
    return document.querySelector('[data-action="preview"]') as HTMLElement;
}

function renderForm(props: Partial<React.ComponentProps<typeof WishlistForm>> = {}) {
    return render(
        <TestWrapper>
            <WishlistForm
                open
                onOpenChange={vi.fn()}
                qualityTierLists={mockQualityTierLists}
                rootFolders={mockRootFolders}
                tags={mockTags}
                onSubmit={vi.fn()}
                {...props}
            />
        </TestWrapper>,
    );
}

describe('WishlistForm', () => {
    it('renders the inline builder controls when open', () => {
        renderForm();

        expect(screen.getByText('Add Wishlist')).toBeInTheDocument();
        expect(screen.getByText('Content Type')).toBeInTheDocument();
        expect(document.querySelector('[data-field="criteria"]')).toBeInTheDocument();
    });

    it('explains that an unmonitored wishlist still adds matching content to the library', () => {
        renderForm();

        expect(screen.getByText(/Matching content is added to the library either way/)).toBeInTheDocument();
    });

    it('makes the create filter read-only while the create request is pending', () => {
        renderForm({ isSubmitting: true });

        expect(document.querySelector('[data-component="simple-filter-builder"]')).toHaveAttribute(
            'data-readonly',
            'true',
        );
    });

    it('no longer renders the "Start from a saved filter" dropdown', () => {
        renderForm();

        expect(screen.queryByText('Start from a saved filter')).not.toBeInTheDocument();
        expect(document.querySelector('[data-field="savedFilter"]')).toBeNull();
    });

    it('renders nothing when closed', () => {
        renderForm({ open: false });
        expect(screen.queryByText('Add Wishlist')).not.toBeInTheDocument();
    });

    it('submits criteria parsed from the builder rather than a contentFilterId', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'contentType = "movie" and genre has "Action"' });
        renderForm({ onSubmit, initialValues: { ...baseInitialValues, name: '', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText(/genre has "Action"/)).toBeInTheDocument());
        await user.type(screen.getByLabelText(/name/i), 'Test Wishlist');

        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        const values = onSubmit.mock.calls[0][0];
        expect(values.name).toBe('Test Wishlist');
        expect(values.contentType).toBe('movie');
        expect(values).not.toHaveProperty('contentFilterId');
    });

    it('keeps edits made during a successful save open and dirty', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        let resolveSubmit: (() => void) | undefined;
        const onSubmit = vi.fn(
            () =>
                new Promise<void>((resolve) => {
                    resolveSubmit = resolve;
                }),
        );
        renderForm({
            onOpenChange,
            onSubmit,
            initialValues: { ...baseInitialValues, criteria: sampleCriteria },
        });

        const nameInput = screen.getByLabelText(/name/i);
        await user.clear(nameInput);
        await user.type(nameInput, 'Submitted name');
        await user.click(saveButton());
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

        await user.clear(nameInput);
        await user.type(nameInput, 'Changed during save');
        act(() => resolveSubmit?.());

        await waitFor(() => expect(nameInput).toHaveValue('Changed during save'));
        expect(onOpenChange).not.toHaveBeenCalledWith(false);

        await user.click(screen.getByRole('button', { name: /cancel/i }));
        expect(await screen.findByText('Unsaved Changes')).toBeInTheDocument();
    });

    it('closes after a successful save when the submitted draft is unchanged', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderForm({
            onOpenChange,
            onSubmit: vi.fn().mockResolvedValue(undefined),
            initialValues: { ...baseInitialValues, criteria: sampleCriteria },
        });

        await user.type(screen.getByLabelText(/name/i), ' updated');
        await user.click(saveButton());

        await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    });

    it('blocks submit when enabled is on but criteria is empty', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({ onSubmit });

        await user.type(screen.getByLabelText(/name/i), 'Monitored Empty');
        await user.click(saveButton());

        expect(await screen.findByText('Add filter criteria to monitor this wishlist')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('allows submit with empty criteria when enabled is off', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({ onSubmit });

        await user.type(screen.getByLabelText(/name/i), 'Unmonitored');
        await user.click(screen.getByLabelText(/enabled/i));
        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0].enabled).toBe(false);
    });

    it('renders enabled and monitoring controls and submits both values', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({
            onSubmit,
            initialValues: { ...baseInitialValues, criteria: sampleCriteria },
        });

        expect(screen.getByLabelText('Enabled')).toBeChecked();
        expect(screen.getByRole('radiogroup', { name: 'Monitoring override' })).toBeInTheDocument();
        await user.click(screen.getByRole('radio', { name: /^Do not monitor:/ }));
        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ enabled: true, monitoring: false });
    });

    it('labels the inherit quality option "Inherit" and not "Default quality"', async () => {
        const user = userEvent.setup();
        renderForm();

        await user.click(document.querySelector('[data-field="qualityTierListId"]') as HTMLElement);

        expect(await screen.findByRole('option', { name: 'Inherit' })).toBeInTheDocument();
        expect(screen.queryByText('Default quality')).not.toBeInTheDocument();
    });

    it('displays MIME applicability in the tier list options', async () => {
        const user = userEvent.setup();
        renderForm();

        await user.click(document.querySelector('[data-field="qualityTierListId"]') as HTMLElement);

        expect(await screen.findByRole('option', { name: 'HD - video/*' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: '4K Ultra HD - Any MIME type' })).toBeInTheDocument();
    });

    it('requests every tier list once the wishlist has a content type', async () => {
        const user = userEvent.setup();
        renderForm({
            qualityTierLists: undefined,
            initialValues: { ...baseInitialValues, name: 'Compatible policy' },
        });

        await waitFor(() => expect(apiGet).toHaveBeenCalledWith('/quality-tier-lists'));
        await user.click(document.querySelector('[data-field="qualityTierListId"]') as HTMLElement);
        expect(await screen.findByRole('option', { name: 'Compatible - video/*' })).toBeInTheDocument();
    });

    it('resolves the current assignment from the tier lists', async () => {
        vi.mocked(apiGet).mockImplementation(() =>
            Promise.resolve([
                {
                    id: 'qtl-audio',
                    name: 'Audio',
                    mimeTypes: ['audio/*'],
                    upgradeAllowed: true,
                    createdAt: '2026-01-01T00:00:00Z',
                    qualityTierRanks: [],
                },
            ]),
        );
        renderForm({
            qualityTierLists: undefined,
            initialValues: {
                ...baseInitialValues,
                name: 'Preserved quality',
                qualityTierListId: 'qtl-audio',
            },
        });

        const trigger = document.querySelector('[data-field="qualityTierListId"]') as HTMLElement;
        await waitFor(() => expect(trigger).toHaveTextContent('Audio - audio/*'));
    });

    it('renders the root folder picker defaulting to None', () => {
        renderForm();

        const trigger = document.querySelector('[data-field="rootFolderId"]') as HTMLElement;
        expect(trigger).toBeInTheDocument();
        expect(trigger).toHaveTextContent('None');
    });

    it('submits the selected root folder id', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({
            onSubmit,
            initialValues: { ...baseInitialValues, name: 'Rooted', enabled: false },
        });

        await user.click(document.querySelector('[data-field="rootFolderId"]') as HTMLElement);
        await user.click(await screen.findByRole('option', { name: /Movies 4K/ }));
        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0].rootFolderId).toBe('rf-2');
    });

    it('submits a null root folder when None is selected', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({
            onSubmit,
            initialValues: { ...baseInitialValues, name: 'Root Reset', rootFolderId: 'rf-1', enabled: false },
        });

        await user.click(document.querySelector('[data-field="rootFolderId"]') as HTMLElement);
        await user.click(await screen.findByRole('option', { name: 'None' }));
        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0].rootFolderId).toBeNull();
    });

    it('submits a null quality tier list when the Inherit option is selected', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({
            onSubmit,
            initialValues: {
                ...baseInitialValues,
                name: 'Quality Reset',
                qualityTierListId: 'qtl-1',
                enabled: false,
            },
        });

        await user.click(document.querySelector('[data-field="qualityTierListId"]') as HTMLElement);
        await user.click(await screen.findByRole('option', { name: 'Inherit' }));
        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0].qualityTierListId).toBeNull();
    });

    it('has no outer content-type field; the picker lives in the embedded filter view', async () => {
        renderForm({ initialValues: { ...baseInitialValues, name: 'Weekend Watchlist', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        expect(document.querySelector('[data-field="contentType"]')).toBeNull();
        expect(document.querySelector('#sfb-content-type')).toBeInTheDocument();
    });

    it('writes the picked content type into the query and submits it in the criteria', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({ onSubmit, initialValues: { ...baseInitialValues, name: 'Pick Type', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        const typeTrigger = document.querySelector('#sfb-content-type') as HTMLElement;
        await user.click(typeTrigger);
        await user.click(await screen.findByRole('option', { name: 'Episodes' }));

        await waitFor(() =>
            expect(document.querySelector('[data-field="criteria"]')).toHaveTextContent('contentType = "episode"'),
        );

        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        const values = onSubmit.mock.calls[0][0];
        expect(values.contentType).toBe('episode');
        expect(values.criteria).toEqual({ contentHierarchyItem: { and: [{ contentType: 'episode' }] } });
    });

    it('saves a converted filter after selecting a content type for seeded title criteria', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const queryClient = new QueryClient({
            defaultOptions: {
                queries: { retry: false },
                mutations: { retry: false },
            },
        });
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'title contains "Matrix"' });
        mockParseMagpieQl.mockImplementation(async (query: string) => {
            const criteria: unknown[] = [];
            const typeMatch = /contentType\s*=\s*"([^"]*)"/i.exec(query);
            const titleMatch = /title\s+contains\s*"([^"]*)"/i.exec(query);
            if (typeMatch) {
                criteria.push({ contentType: typeMatch[1] });
            }
            if (titleMatch) {
                criteria.push({ title: titleMatch[1] });
            }
            return { criteria: { contentHierarchyItem: { and: criteria } } };
        });
        render(
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>
                    <TooltipProvider>
                        <WishlistForm
                            open
                            onOpenChange={vi.fn()}
                            qualityTierLists={mockQualityTierLists}
                            rootFolders={mockRootFolders}
                            tags={mockTags}
                            onSubmit={onSubmit}
                            initialValues={{
                                name: 'Matrix Wishlist',
                                filterId: null,
                                contentType: '',
                                criteria: sampleCriteria,
                                qualityTierListId: null,
                                rootFolderId: null,
                                enabled: true,
                                monitoring: null,
                                tagIds: [],
                            }}
                        />
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>,
        );

        await waitFor(() => expect(screen.getByText(/title contains "Matrix"/i)).toBeInTheDocument());

        const typeTrigger = document.querySelector('#sfb-content-type') as HTMLElement;
        await user.click(typeTrigger);
        await user.click(await screen.findByRole('option', { name: 'Movies' }));

        await waitFor(() =>
            expect(document.querySelector('[data-field="criteria"]')).toHaveTextContent('contentType = "movie"'),
        );

        await user.click(document.querySelector('[data-action="save"]') as HTMLElement);

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(mockParseMagpieQl).toHaveBeenLastCalledWith(
            'contentType = "movie" and title contains "Matrix"',
            'content',
        );
        expect(onSubmit.mock.calls[0][0].contentType).toBe('movie');
        expect(screen.queryByText('Select a content type to monitor this wishlist')).not.toBeInTheDocument();
    });

    it('blocks submit when enabled is on but no content type is selected, surfacing the error on the filter', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({
            onSubmit,
            initialValues: { ...baseInitialValues, name: 'No Type', contentType: '', criteria: sampleCriteria },
        });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        await user.click(saveButton());

        const error = await screen.findByText('Select a content type to monitor this wishlist');
        expect(error).toBeInTheDocument();
        expect(error).toHaveAttribute('data-field', 'contentType-error');
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('expands the embedded filter view by default', () => {
        renderForm();
        expect(document.querySelector('[data-component="filter-bar"]')).toHaveAttribute('data-expanded', 'true');
        expect(document.querySelector('#sfb-content-type')).toBeInTheDocument();
    });

    it('renders tags as a TagInput and lets a new tag be added', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'contentType = "movie" and genre has "Action"' });
        renderForm({ onSubmit, initialValues: { ...baseInitialValues, name: 'Tagged', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText(/genre has "Action"/)).toBeInTheDocument());

        const tagField = document.querySelector('[data-field="tags"]') as HTMLElement;
        const tagInput = tagField.querySelector('[data-component="tag-input"] input') as HTMLElement;
        expect(tagInput).toBeInTheDocument();

        await user.type(tagInput, 'Fresh Tag');
        await user.keyboard('{Enter}');

        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0].tagIds).toContain('Fresh Tag');
    });

    it('shows at most 3 preview items plus the total count', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockResolvedValue({
            matches: Array.from({ length: 5 }, (_, i) => ({
                candidate: {
                    providerType: 'provider-a',
                    externalIdSource: 'provider-a.movie',
                    externalId: String(i),
                    contentType: 'movie',
                    title: `Title ${i}`,
                    year: 2000 + i,
                    thumbnailUrl: null,
                },
                inLibrary: false,
            })),
            pageItemCount: 5,
            existsInLibraryCount: 0,
        });
        renderForm({ initialValues: { ...baseInitialValues, name: 'Preview', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        await user.click(previewButton());

        const list = await screen.findByTestId('wishlist-form__preview-list');
        expect(list.querySelectorAll('li')).toHaveLength(3);
        expect(screen.getByText('Newest 3 of 5')).toBeInTheDocument();
    });

    it('renders the placeholder preview artwork when the candidate has no thumbnail', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockResolvedValue({
            matches: [
                {
                    candidate: {
                        providerType: 'provider-a',
                        externalIdSource: 'provider-a.movie',
                        externalId: '27205',
                        contentType: 'movie',
                        title: 'Inception',
                        year: 2010,
                        thumbnailUrl: null,
                    },
                    inLibrary: false,
                },
            ],
            pageItemCount: 1,
            existsInLibraryCount: 0,
        });
        renderForm({ initialValues: { ...baseInitialValues, name: 'Preview', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        await user.click(previewButton());

        await waitFor(() => {
            expect(apiGet).toHaveBeenCalledWith(
                '/content-search?query=genre+has+%22Action%22&contentType=movie&page=1',
            );
        });
        const previewList = await screen.findByTestId('wishlist-form__preview-list');
        expect(previewList.querySelector('img')).not.toBeInTheDocument();
        const placeholder = previewList.querySelector('.wishlist-form__preview-thumb--placeholder');
        expect(placeholder).toHaveClass('wishlist-form__preview-thumb');
        expect(placeholder).toHaveAttribute('aria-hidden', 'true');
        expect(placeholder?.children).toHaveLength(0);
        expect(placeholder?.querySelectorAll('svg')).toHaveLength(0);
        expect(screen.getByText('Inception')).toBeInTheDocument();
    });

    it('falls back to the placeholder preview artwork when the thumbnail fails to load', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockResolvedValue({
            matches: [
                {
                    candidate: {
                        providerType: 'provider-a',
                        externalIdSource: 'provider-a.movie',
                        externalId: '27205',
                        contentType: 'movie',
                        title: 'Inception',
                        year: 2010,
                        thumbnailUrl: '/broken.jpg',
                    },
                    inLibrary: false,
                },
            ],
            pageItemCount: 1,
            existsInLibraryCount: 0,
        });
        renderForm({ initialValues: { ...baseInitialValues, name: 'Preview', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        await user.click(previewButton());

        await waitFor(() => {
            expect(apiGet).toHaveBeenCalledWith(
                '/content-search?query=genre+has+%22Action%22&contentType=movie&page=1',
            );
        });
        const previewList = await screen.findByTestId('wishlist-form__preview-list');
        const image = previewList.querySelector('img');
        expect(image).toHaveAttribute('src', '/broken.jpg');
        fireEvent.error(image!);
        expect(previewList.querySelector('img')).not.toBeInTheDocument();
        const placeholder = previewList.querySelector('.wishlist-form__preview-thumb--placeholder');
        expect(placeholder).toHaveClass('wishlist-form__preview-thumb');
        expect(placeholder).toHaveAttribute('aria-hidden', 'true');
        expect(placeholder?.children).toHaveLength(1);
        expect(placeholder?.querySelectorAll('svg')).toHaveLength(1);
        expect(screen.getByText('Inception')).toBeInTheDocument();
    });

    it('shows an empty preview state when no candidates match', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockResolvedValue({
            matches: [],
            pageItemCount: 0,
            existsInLibraryCount: 0,
        });
        renderForm({ initialValues: { ...baseInitialValues, name: 'Preview', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        await user.click(previewButton());

        expect(await screen.findByTestId('wishlist-form__preview-empty')).toBeInTheDocument();
    });

    it('shows a preview error state when the endpoint fails', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockRejectedValue(new Error('boom'));
        renderForm({ initialValues: { ...baseInitialValues, name: 'Preview', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText('genre has "Action"')).toBeInTheDocument());

        await user.click(previewButton());

        expect(await screen.findByTestId('wishlist-form__preview-error')).toBeInTheDocument();
    });

    it('no longer renders the "also save as a reusable filter" toggle', async () => {
        renderForm({ initialValues: { ...baseInitialValues, name: 'Reusable', criteria: sampleCriteria } });

        await waitFor(() => expect(screen.getByText(/genre has "Action"/)).toBeInTheDocument());

        expect(document.querySelector('[data-field="saveAsReusableFilter"]')).toBeNull();
        expect(screen.queryByText(/also save as a reusable filter/i)).not.toBeInTheDocument();
    });

    it('validates name is required', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm({ onSubmit });

        await user.click(screen.getByLabelText(/enabled/i));
        await user.click(saveButton());

        expect(await screen.findByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('prefills name and seeds the builder from criteria on edit', async () => {
        renderForm({
            initialValues: { ...baseInitialValues, name: 'Edited', criteria: sampleCriteria },
        });

        expect(screen.getByText('Edit Wishlist')).toBeInTheDocument();
        expect(screen.getByDisplayValue('Edited')).toBeInTheDocument();
        await waitFor(() => {
            expect(mockFormatMagpieQlCriteria).toHaveBeenCalledWith(sampleCriteria, 'content');
        });
    });

    it('does not block saving a monitored wishlist seeded from existing criteria on edit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'contentType = "movie" and genre has "Action"' });
        renderForm({
            initialValues: { ...baseInitialValues, name: 'Monitored Edit', criteria: sampleCriteria, enabled: true },
            onSubmit,
        });

        await waitFor(() => expect(screen.getByText(/genre has "Action"/)).toBeInTheDocument());

        await user.click(saveButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(screen.queryByText('Add filter criteria to monitor this wishlist')).not.toBeInTheDocument();
        expect(screen.queryByText('Select a content type to monitor this wishlist')).not.toBeInTheDocument();
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ enabled: true, contentType: 'movie' });
    });
});

describe('WishlistForm read-only mode', () => {
    it('shows a read-only badge, disables editing, and hides Save', () => {
        renderForm({
            initialValues: { ...baseInitialValues, name: 'Other User Wishlist' },
            canEdit: false,
        });

        expect(screen.getByTestId('wishlist-form__readonly-badge')).toBeInTheDocument();
        expect(screen.getByLabelText(/name/i)).toBeDisabled();
        expect(saveButton()).toBeNull();
        expect(previewButton()).toBeNull();

        const closeButtons = screen.getAllByRole('button', { name: /close/i });
        expect(closeButtons.length).toBeGreaterThan(0);
    });
});

describe('WishlistForm unsaved changes', () => {
    it('shows unsaved changes dialog when closing with a dirty form', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderForm({ onOpenChange });

        await user.type(screen.getByLabelText(/name/i), 'Dirty Value');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        await waitFor(() => expect(screen.getByText('Unsaved Changes')).toBeInTheDocument());
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('keeps the form open with edits intact when choosing to keep editing', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderForm({ onOpenChange });

        await user.type(screen.getByLabelText(/name/i), 'Dirty Value');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        await waitFor(() => expect(screen.getByText('Unsaved Changes')).toBeInTheDocument());

        await user.click(screen.getByRole('button', { name: /keep editing/i }));

        expect(onOpenChange).not.toHaveBeenCalled();
        await waitFor(() => expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument());
        expect(screen.getByLabelText(/name/i)).toHaveValue('Dirty Value');
    });

    it('closes without warning when the form is not dirty', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderForm({ onOpenChange });

        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(onOpenChange).toHaveBeenCalledWith(false);
        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
    });
});

describe('WishlistForm unsaved-changes guard', () => {
    function trackGuard() {
        const state = { active: 0 };
        const originalAdd = window.addEventListener.bind(window);
        const originalRemove = window.removeEventListener.bind(window);
        vi.spyOn(window, 'addEventListener').mockImplementation((type, ...rest) => {
            if (type === 'beforeunload') {
                state.active += 1;
            }
            return originalAdd(type, ...(rest as [EventListenerOrEventListenerObject]));
        });
        vi.spyOn(window, 'removeEventListener').mockImplementation((type, ...rest) => {
            if (type === 'beforeunload') {
                state.active -= 1;
            }
            return originalRemove(type, ...(rest as [EventListenerOrEventListenerObject]));
        });
        return state;
    }

    it('does not arm the guard when opening an untouched add form', () => {
        const guard = trackGuard();
        renderForm();

        expect(guard.active).toBe(0);
    });

    it('arms the guard after a real edit', async () => {
        const user = userEvent.setup();
        const guard = trackGuard();
        renderForm();

        await user.type(screen.getByLabelText(/name/i), 'Dirty Value');

        expect(guard.active).toBeGreaterThan(0);
    });

    it('disarms the guard when discarding a dirty form on cancel', async () => {
        const user = userEvent.setup();
        const guard = trackGuard();
        renderForm();

        await user.type(screen.getByLabelText(/name/i), 'Dirty Value');
        expect(guard.active).toBeGreaterThan(0);

        await user.click(screen.getByRole('button', { name: /cancel/i }));
        await waitFor(() => expect(screen.getByText('Unsaved Changes')).toBeInTheDocument());

        await user.click(screen.getByRole('button', { name: /discard changes/i }));

        await waitFor(() => expect(guard.active).toBe(0));
    });
});
