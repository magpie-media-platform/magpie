import { act, render, screen, within, waitFor, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport, ToastList } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

vi.mock('@/auth', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/auth')>();
    return {
        ...original,
        useAuth: () =>
            ({
                isAuthenticated: true,
                hasPermission: () => true,
                getRequiredRole: () => undefined,
                hasRole: () => true,
            }) as unknown as ReturnType<typeof original.useAuth>,
    };
});

vi.mock('@/hooks/useQualityTierLists', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useQualityTierLists')>();
    return {
        ...original,
        useQualityTierLists: () => ({
            tierLists: [
                {
                    id: 'quality-default',
                    name: 'Default',
                    mimeTypes: [],
                    upgradeAllowed: true,
                    createdAt: '2026-01-01T00:00:00Z',
                    qualityTierRanks: [],
                },
            ],
            isLoading: false,
        }),
    };
});

import { apiGet, apiDelete, apiPost, apiPatch } from '@/lib/api';
import type { ApiRootFolder } from '@/hooks/useRootFolders';

import { RootFoldersPage } from './RootFoldersPage';

const mockContentTypes = [
    {
        slug: 'movies',
        displayName: 'Movies',
        levelDefinitions: [
            { slug: 'movie', displayName: 'Movie', parentSlug: null, effectiveAcceptedMimeTypes: ['video/*'] },
        ],
    },
    {
        slug: 'shows',
        displayName: 'TV Shows',
        levelDefinitions: [
            { slug: 'series', displayName: 'Series', parentSlug: null, effectiveAcceptedMimeTypes: ['video/*'] },
        ],
    },
    {
        slug: 'music',
        displayName: 'Music',
        levelDefinitions: [
            { slug: 'album', displayName: 'Album', parentSlug: null, effectiveAcceptedMimeTypes: ['audio/*'] },
        ],
    },
    {
        slug: 'books',
        displayName: 'Books',
        levelDefinitions: [
            { slug: 'book', displayName: 'Book', parentSlug: null, effectiveAcceptedMimeTypes: ['text/*'] },
        ],
    },
    {
        slug: 'anime',
        displayName: 'Anime',
        levelDefinitions: [
            { slug: 'anime', displayName: 'Anime', parentSlug: null, effectiveAcceptedMimeTypes: ['video/*'] },
        ],
    },
];

const mockSubjectTypes: unknown[] = [];

const mockApiFolders: ApiRootFolder[] = [
    {
        id: 'folder-1',
        name: 'Movies',
        path: '/media/movies',
        contentType: 'movies',
        isDefault: true,
        defaultMonitoring: true,
        upgradeFileHandling: 'Replace',
        defaults: { movie: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'folder-2',
        name: 'TV Shows',
        path: '/media/tv',
        contentType: 'shows',
        isDefault: true,
        defaultMonitoring: true,
        upgradeFileHandling: 'Replace',
        defaults: { series: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'folder-3',
        name: 'Music Library',
        path: '/media/music',
        contentType: 'music',
        isDefault: false,
        defaultMonitoring: null,
        upgradeFileHandling: 'Replace',
        defaults: { album: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'folder-4',
        name: 'Anime Collection',
        path: '/media/anime',
        contentType: 'anime',
        isDefault: false,
        defaultMonitoring: false,
        upgradeFileHandling: 'Replace',
        defaults: { anime: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-03T00:00:00Z',
    },
    {
        id: 'folder-5',
        name: 'Ebooks',
        path: '/media/books',
        contentType: 'books',
        isDefault: true,
        defaultMonitoring: true,
        upgradeFileHandling: 'Replace',
        defaults: { book: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-04T00:00:00Z',
    },
];

function defaultApiGet(url: string) {
    if (url === '/content-types') {
        return Promise.resolve(mockContentTypes);
    }
    if (url === '/subject-types') {
        return Promise.resolve(mockSubjectTypes);
    }
    if (url === '/root-folders') {
        return Promise.resolve(mockApiFolders);
    }
    if (url === '/system/disk-space') {
        return Promise.resolve({
            folders: [
                {
                    id: 'folder-1',
                    name: 'Movies',
                    path: '/media/movies',
                    totalBytes: 1000 * 1024 * 1024 * 1024,
                    freeBytes: 750 * 1024 * 1024 * 1024,
                    usedBytes: 250 * 1024 * 1024 * 1024,
                },
                {
                    id: 'folder-2',
                    name: 'TV Shows',
                    path: '/media/tv',
                    totalBytes: 1000 * 1024 * 1024 * 1024,
                    freeBytes: 500 * 1024 * 1024 * 1024,
                    usedBytes: 500 * 1024 * 1024 * 1024,
                },
            ],
            totalBytes: 1000 * 1024 * 1024 * 1024,
            freeBytes: 750 * 1024 * 1024 * 1024,
            usedBytes: 250 * 1024 * 1024 * 1024,
        });
    }
    if (url.startsWith('/quality-tier-lists')) {
        return Promise.resolve([
            {
                id: 'quality-default',
                name: 'Default',
                mimeTypes: [],
                upgradeAllowed: true,
                createdAt: '2026-01-01T00:00:00Z',
                qualityTierRanks: [],
            },
        ]);
    }
    if (url === '/settings/groups') {
        return Promise.resolve([]);
    }
    return Promise.reject(new Error(`Unexpected API call: ${url}`));
}

afterEach(() => {
    cleanup();
    document
        .querySelectorAll('[data-component="toast"], .toast-viewport, [role="status"], [role="alert"]')
        .forEach((node) => {
            node.remove();
        });
});

function createTestWrapper(onClient?: (client: QueryClient) => void) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });
    onClient?.(queryClient);

    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

describe('RootFoldersPage', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(defaultApiGet);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-page="root-folders"]')).toBeInTheDocument();
        });
    });

    it('renders the page title', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Root Folders', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders no folder count pill', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-folder-id], [data-row-id]')).toBeTruthy();
        });
        expect(screen.queryByTestId('root-folders-page__count')).not.toBeInTheDocument();
        expect(document.querySelector('.list-toolbar__summary')).toBeNull();
    });

    it('renders the page icon as decorative', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const icon = screen.getByTestId('root-folders-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('renders an Add Folder button', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const addButtons = screen.getAllByRole('button', { name: /add folder/i });
            expect(addButtons.length).toBeGreaterThanOrEqual(1);
        });
    });

    it('renders the system default monitoring control', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-component="monitoring-default-settings"]')).toBeInTheDocument();
            expect(document.querySelector('[data-setting="system-default-monitoring"]')).toBeInTheDocument();
        });
    });

    it('renders disk-space details from the existing system endpoint', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const widget = document.querySelector('[data-component="disk-space-widget"]');
            expect(widget).not.toBeNull();

            const movies = widget?.querySelector<HTMLElement>('[data-folder-id="folder-1"]');
            const tvShows = widget?.querySelector<HTMLElement>('[data-folder-id="folder-2"]');
            expect(movies).not.toBeNull();
            expect(tvShows).not.toBeNull();

            const movieDiskSpace = within(movies as HTMLElement);
            expect(movieDiskSpace.getByText('250.00 GB used')).toBeInTheDocument();
            expect(movieDiskSpace.getByText('750.00 GB free')).toBeInTheDocument();
            expect(movieDiskSpace.getByText('1000.00 GB total')).toBeInTheDocument();
            expect(movies).toHaveAttribute('data-usage-status', 'healthy');
            expect(tvShows).toHaveAttribute('data-usage-status', 'healthy');
        });

        expect(screen.getByRole('heading', { name: 'Disk Space', level: 2 })).toBeInTheDocument();
        expect(mockApiGet).toHaveBeenCalledWith('/system/disk-space');
    });

    it('shows a single add affordance with no duplicate in the empty state', async () => {
        mockApiGet.mockImplementation((url: string) =>
            url === '/root-folders' ? Promise.resolve([]) : defaultApiGet(url),
        );

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'No root folders configured' })).toBeInTheDocument();
        });
        expect(screen.getByText('A root folder determines where imported content is stored.')).toBeInTheDocument();

        expect(document.querySelector('[data-action="add-folder"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="add-folder-empty"]')).not.toBeInTheDocument();
    });
});

describe('RootFoldersPage table rows', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(defaultApiGet);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the folders as a table', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('grid')).toBeInTheDocument();
            expect(document.querySelector('[aria-label="Root folders"]')).toBeInTheDocument();
        });
    });

    it('renders a row per folder', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const rows = document.querySelectorAll('[data-row-id]');
            expect(rows.length).toBe(5);
        });
    });

    it('renders folder names and paths', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const table = within(screen.getByRole('grid'));
            expect(table.getByText('Music Library')).toBeInTheDocument();
            expect(table.getByText('/media/movies')).toBeInTheDocument();
            expect(table.getByText('/media/anime')).toBeInTheDocument();
        });
    });

    it('keeps long unbroken folder names and paths contained with the full values available', async () => {
        const longName = `RootFolder${'UnbrokenNameSegment'.repeat(8)}`;
        const longPath = `/media/${'unbrokenpathsegment'.repeat(10)}`;
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/root-folders') {
                return Promise.resolve([{ ...mockApiFolders[0], id: 'folder-long', name: longName, path: longPath }]);
            }
            return defaultApiGet(url);
        });

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByTitle(longName)).toHaveClass('root-folders-table__name');
            expect(screen.getByTitle(longPath)).toHaveClass('root-folders-table__path');
        });
        expect(screen.getByTitle(longName).closest('[data-column="name"]')).toBeInTheDocument();
        expect(screen.getByTitle(longPath).closest('[data-column="path"]')).toBeInTheDocument();
    });

    it('renders a content type badge per row', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const moviesRow = document.querySelector('[data-row-id="folder-1"]');
            const badge = within(moviesRow as HTMLElement).getByText('Movies', {
                selector: '[data-component="badge"]',
            });
            expect(badge).toBeInTheDocument();
        });
    });

    it('renders the default monitoring per row', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const activeRow = document.querySelector('[data-row-id="folder-1"]');
            expect(within(activeRow as HTMLElement).getByText('Active')).toBeInTheDocument();

            const inheritRow = document.querySelector('[data-row-id="folder-3"]');
            expect(within(inheritRow as HTMLElement).getByText('Inherit')).toBeInTheDocument();
        });
    });
});

describe('RootFoldersPage actions', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiDelete = vi.mocked(apiDelete);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(defaultApiGet);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders edit and delete buttons for each folder', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="edit"]').length).toBe(5);
            expect(document.querySelectorAll('[data-action="delete"]').length).toBe(5);
        });
    });

    it('edit button has correct aria-label and target', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const editButton = screen.getByRole('button', { name: /^edit movies$/i });
            expect(editButton).toHaveAttribute('data-action', 'edit');
            expect(editButton).toHaveAttribute('data-target', 'folder-1');
        });
    });

    it('opens add form when add button is clicked', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /add folder/i }).length).toBeGreaterThanOrEqual(1);
        });

        const addButton = screen.getAllByRole('button', { name: /add folder/i })[0];
        await user.click(addButton);

        await waitFor(() => {
            expect(screen.getByText('Add Root Folder')).toBeInTheDocument();
        });
    });

    it('shows the simple required quality picker after selecting a single-root type', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /add folder/i }).length).toBeGreaterThanOrEqual(1);
        });

        await user.click(screen.getAllByRole('button', { name: /add folder/i })[0]);

        expect(document.querySelector('[data-field="default-quality-profile"]')).not.toBeInTheDocument();
        await user.click(document.querySelector('[data-field="content-type"]') as HTMLElement);
        await user.click(await screen.findByRole('option', { name: 'Movies' }));

        await waitFor(() => {
            expect(document.querySelector('[data-field="default-quality-profile"]')).toHaveTextContent(
                'Select tier list...',
            );
        });
    });

    it('posts the selected root-level default when creating a root folder', async () => {
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({
            id: 'folder-created',
            name: 'New Movies',
            path: '/media/new-movies',
            contentType: 'movies',
            isDefault: false,
            defaultMonitoring: true,
            upgradeFileHandling: 'Replace',
            defaults: { movie: { qualityTierListId: 'quality-default' } },
            createdAt: '2024-01-05T00:00:00Z',
        });

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /add folder/i }).length).toBeGreaterThanOrEqual(1);
        });

        await user.click(screen.getAllByRole('button', { name: /add folder/i })[0]);
        await user.type(screen.getByLabelText(/^Name/i), 'New Movies');
        await user.type(document.querySelector('[data-field="path"]') as HTMLElement, '/media/new-movies');

        const contentTypeTrigger = document.querySelector('[data-field="content-type"]') as HTMLElement;
        await user.click(contentTypeTrigger);
        await user.click(await screen.findByRole('option', { name: 'Movies' }));

        const qualityTrigger = document.querySelector('[data-field="default-quality-profile"]') as HTMLElement;
        await user.click(qualityTrigger);
        await waitFor(() =>
            expect(screen.queryAllByRole('option').map((option) => option.textContent)).toEqual([
                'Default - Any MIME type',
            ]),
        );
        await user.click(screen.getByRole('option', { name: /Default/ }));

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/root-folders', {
                name: 'New Movies',
                path: '/media/new-movies',
                contentType: 'movies',
                isDefault: false,
                defaultMonitoring: null,
                renameTemplate: null,
                upgradeFileHandling: 'Replace',
                defaults: { movie: { qualityTierListId: 'quality-default' } },
            });
        });
    });

    it('keeps a failed save in the form lifecycle after showing the error toast', async () => {
        const user = userEvent.setup();
        mockApiPost.mockRejectedValue(new Error('Save failed'));

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /add folder/i }).length).toBeGreaterThanOrEqual(1);
        });

        await user.click(screen.getAllByRole('button', { name: /add folder/i })[0]);
        await user.type(screen.getByLabelText(/^Name/i), 'New Movies');
        await user.type(document.querySelector('[data-field="path"]') as HTMLElement, '/media/new-movies');
        await user.click(document.querySelector('[data-field="content-type"]') as HTMLElement);
        await user.click(await screen.findByRole('option', { name: 'Movies' }));
        const qualityTrigger = document.querySelector('[data-field="default-quality-profile"]') as HTMLElement;
        await user.click(qualityTrigger);
        await user.click(await screen.findByRole('option', { name: /Default/ }));
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(await screen.findByText('Save failed')).toBeInTheDocument();
        expect(screen.getByText('Add Root Folder')).toBeInTheDocument();
    });

    it('blocks creation until the root-level default is selected', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /add folder/i }).length).toBeGreaterThanOrEqual(1);
        });

        await user.click(screen.getAllByRole('button', { name: /add folder/i })[0]);
        await user.type(screen.getByLabelText(/^Name/i), 'New Movies');
        await user.type(document.querySelector('[data-field="path"]') as HTMLElement, '/media/new-movies');

        const contentTypeTrigger = document.querySelector('[data-field="content-type"]') as HTMLElement;
        await user.click(contentTypeTrigger);
        await user.click(await screen.findByRole('option', { name: 'Movies' }));

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(await screen.findByText('Select a default tier list for Movie')).toBeInTheDocument();
        expect(mockApiPost).not.toHaveBeenCalled();
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^edit movies$/i })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: /^edit movies$/i });
        await user.click(editButton);

        await waitFor(() => {
            expect(screen.getByText('Edit Root Folder')).toBeInTheDocument();
            expect(screen.getAllByDisplayValue('Movies').length).toBeGreaterThanOrEqual(1);
        });
    });

    it('refreshes a clean open editor and preserves a dirty draft', async () => {
        const user = userEvent.setup();
        let queryClient: QueryClient | undefined;

        render(<RootFoldersPage />, {
            wrapper: createTestWrapper((client) => {
                queryClient = client;
            }),
        });

        await user.click(await screen.findByRole('button', { name: /^edit movies$/i }));
        const nameInput = screen.getByRole('textbox', { name: /^name$/i });

        act(() => {
            queryClient?.setQueryData(
                ['root-folders', 'list'],
                mockApiFolders.map((folder) =>
                    folder.id === 'folder-1' ? { ...folder, name: 'Movies from server' } : folder,
                ),
            );
        });
        await waitFor(() => expect(nameInput).toHaveValue('Movies from server'));

        await user.clear(nameInput);
        await user.type(nameInput, 'My unsaved movies');
        act(() => {
            queryClient?.setQueryData(
                ['root-folders', 'list'],
                mockApiFolders.map((folder) =>
                    folder.id === 'folder-1' ? { ...folder, name: 'Movies changed again' } : folder,
                ),
            );
        });

        expect(nameInput).toHaveValue('My unsaved movies');
    });

    it('opens confirmation dialog when delete button is clicked, without deleting', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete movies$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete movies$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Root Folder')).toBeInTheDocument();
        });
        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('calls delete API after confirming deletion', async () => {
        mockApiDelete.mockResolvedValue(undefined);
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete movies$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete movies$/i });
        await user.click(deleteButton);

        const confirmButton = await screen.findByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/root-folders/folder-1');
        });
    });

    it('renders only edit and delete as per-row actions (no test/toggle)', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="folder-1"]')).toBeInTheDocument();
        });

        const actions = document.querySelectorAll('[data-row-id="folder-1"] .card-actions__btn');
        expect(actions).toHaveLength(2);
        expect(document.querySelector('[data-action="toggle-enabled"]')).toBeNull();
        expect(document.querySelector('[data-action="test"]')).toBeNull();
    });

    it('patches a folder when its default radio is selected', async () => {
        const user = userEvent.setup();
        mockApiPatch.mockResolvedValue(undefined);

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="folder-3"]')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('radio', { name: 'Set Music Library as default for Music' }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/root-folders/folder-3', { isDefault: true });
        });
    });
});

describe('RootFoldersPage content type filter', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(defaultApiGet);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders a column-header type filter', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const typeHeader = document.querySelector('[data-column="contentType"]');
            expect(typeHeader).toBeInTheDocument();
            expect(
                within(typeHeader as HTMLElement).getByRole('button', { name: /filter by type/i }),
            ).toBeInTheDocument();
        });
    });

    it('filters rows by content type when the filter is applied', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-row-id]').length).toBe(5);
        });

        const typeHeader = document.querySelector('[data-column="contentType"]') as HTMLElement;
        const trigger = within(typeHeader).getByRole('button', { name: /filter by type/i });
        await user.click(trigger);

        await waitFor(() => {
            expect(screen.getByRole('menuitemradio', { name: 'Movies' })).toBeInTheDocument();
        });
        await user.click(screen.getByRole('menuitemradio', { name: 'Movies' }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-row-id]').length).toBe(1);
        });
    });

    it('shows a clear-filter button when the filter is active', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-row-id]').length).toBe(5);
        });

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const typeHeader = document.querySelector('[data-column="contentType"]') as HTMLElement;
        await user.click(within(typeHeader).getByRole('button', { name: /filter by type/i }));

        await waitFor(() => {
            expect(screen.getByRole('menuitemradio', { name: 'Movies' })).toBeInTheDocument();
        });
        await user.click(screen.getByRole('menuitemradio', { name: 'Movies' }));

        await waitFor(() => {
            expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
        });
    });
});

describe('RootFoldersPage unknown content type badge', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();

        const foldersWithUnknown: ApiRootFolder[] = [
            {
                id: 'folder-unknown',
                name: 'Unknown Folder',
                path: '/media/unknown',
                contentType: 'nonexistent-type',
                isDefault: false,
                defaultMonitoring: true,
                upgradeFileHandling: 'Replace',
                defaults: {},
                createdAt: '2024-01-01T00:00:00Z',
            },
        ];

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/root-folders') {
                return Promise.resolve(foldersWithUnknown);
            }
            return defaultApiGet(url);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders unknown variant badge for unresolved content type', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const row = document.querySelector('[data-row-id="folder-unknown"]');
            expect(row).toBeInTheDocument();
            const badge = within(row as HTMLElement).getByText('Unknown');
            expect(badge.closest('[data-component="badge"]')).toHaveAttribute('data-status', 'unknown');
        });
    });
});
