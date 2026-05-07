import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
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

import { apiGet, apiDelete } from '@/lib/api';
import type { ApiRootFolder } from '@/hooks/useRootFolders';

import { RootFoldersPage } from './RootFoldersPage';

const mockContentTypes = [
    { slug: 'movies', displayName: 'Movies', levelDefinitions: [] },
    { slug: 'shows', displayName: 'TV Shows', levelDefinitions: [] },
    { slug: 'music', displayName: 'Music', levelDefinitions: [] },
    { slug: 'books', displayName: 'Books', levelDefinitions: [] },
    { slug: 'anime', displayName: 'Anime', levelDefinitions: [] },
];

const mockSubjectTypes: unknown[] = [];

const mockApiFolders: ApiRootFolder[] = [
    {
        id: 'folder-1',
        name: 'Movies',
        path: '/media/movies',
        contentTypeId: 'movies',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'folder-2',
        name: 'TV Shows',
        path: '/media/tv',
        contentTypeId: 'shows',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'folder-3',
        name: 'Music Library',
        path: '/media/music',
        contentTypeId: 'music',
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'folder-4',
        name: 'Anime Collection',
        path: '/media/anime',
        contentTypeId: 'anime',
        createdAt: '2024-01-03T00:00:00Z',
    },
    {
        id: 'folder-5',
        name: 'Ebooks',
        path: '/media/books',
        contentTypeId: 'books',
        createdAt: '2024-01-04T00:00:00Z',
    },
];

function createTestWrapper() {
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

    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
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
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-types') {
                return Promise.resolve(mockContentTypes);
            }
            if (url === '/subject-types') {
                return Promise.resolve(mockSubjectTypes);
            }
            if (url === '/root-folders') {
                return Promise.resolve(mockApiFolders);
            }
            return Promise.reject(new Error(`Unexpected API call: ${url}`));
        });
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

    it('renders the folder count', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const count = screen.getByTestId('root-folders-page__count');
            expect(count).toBeInTheDocument();
            expect(count).toHaveTextContent('5');
        });
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
});

describe('RootFoldersPage list items', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-types') {
                return Promise.resolve(mockContentTypes);
            }
            if (url === '/subject-types') {
                return Promise.resolve(mockSubjectTypes);
            }
            if (url === '/root-folders') {
                return Promise.resolve(mockApiFolders);
            }
            return Promise.reject(new Error(`Unexpected API call: ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders folder items in the list', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /root folders/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(5);
        });
    });

    it('renders folder names', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByText('Movies').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('TV Shows').length).toBeGreaterThanOrEqual(1);
            expect(screen.getByText('Music Library')).toBeInTheDocument();
            expect(screen.getByText('Anime Collection')).toBeInTheDocument();
            expect(screen.getByText('Ebooks')).toBeInTheDocument();
        });
    });

    it('renders folder paths', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByText('/media/movies')).toBeInTheDocument();
            expect(screen.getByText('/media/tv')).toBeInTheDocument();
            expect(screen.getByText('/media/music')).toBeInTheDocument();
            expect(screen.getByText('/media/anime')).toBeInTheDocument();
            expect(screen.getByText('/media/books')).toBeInTheDocument();
        });
    });

    it('renders content type badges', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const moviesBadge = document.querySelector('[data-folder-id="folder-1"] [data-component="badge"]');
            expect(moviesBadge).toHaveTextContent('Movies');

            const showsBadge = document.querySelector('[data-folder-id="folder-2"] [data-component="badge"]');
            expect(showsBadge).toHaveTextContent('TV Shows');
        });
    });

    it('applies data-folder-id to items', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const items = document.querySelectorAll('[data-folder-id]');
            expect(items.length).toBe(5);
            expect(items[0]).toHaveAttribute('data-folder-id', 'folder-1');
        });
    });
});

describe('RootFoldersPage actions', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-types') {
                return Promise.resolve(mockContentTypes);
            }
            if (url === '/subject-types') {
                return Promise.resolve(mockSubjectTypes);
            }
            if (url === '/root-folders') {
                return Promise.resolve(mockApiFolders);
            }
            return Promise.reject(new Error(`Unexpected API call: ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders edit button for each folder', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit"]');
            expect(editButtons.length).toBe(5);
        });
    });

    it('renders delete button for each folder', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(deleteButtons.length).toBe(5);
        });
    });

    it('edit button has correct aria-label', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const editButton = screen.getByRole('button', { name: /^edit movies$/i });
            expect(editButton).toHaveAttribute('data-action', 'edit');
            expect(editButton).toHaveAttribute('data-target', 'folder-1');
        });
    });

    it('delete button has correct aria-label', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const deleteButton = screen.getByRole('button', { name: /^delete movies$/i });
            expect(deleteButton).toHaveAttribute('data-action', 'delete');
            expect(deleteButton).toHaveAttribute('data-target', 'folder-1');
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

    it('opens confirmation dialog when delete button is clicked', async () => {
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
        const dialog = document.querySelector('[data-component="delete-confirm-dialog"]')!;
        expect(within(dialog).getByText(/Movies/)).toBeInTheDocument();
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

    it('does not call delete API when cancel is clicked', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete movies$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete movies$/i });
        await user.click(deleteButton);

        const cancelButton = await screen.findByRole('button', { name: /^cancel$/i });
        await user.click(cancelButton);

        expect(mockApiDelete).not.toHaveBeenCalled();
    });
});

describe('RootFoldersPage accessibility', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-types') {
                return Promise.resolve(mockContentTypes);
            }
            if (url === '/subject-types') {
                return Promise.resolve(mockSubjectTypes);
            }
            if (url === '/root-folders') {
                return Promise.resolve(mockApiFolders);
            }
            return Promise.reject(new Error(`Unexpected API call: ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('list has accessible label', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('list', { name: /root folders/i })).toBeInTheDocument();
        });
    });

    it('action buttons have aria-labels', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const actionButtons = screen.getAllByTestId('root-folders-page__action-btn');
            expect(actionButtons.length).toBeGreaterThan(0);
            actionButtons.forEach((btn) => {
                expect(btn).toHaveAttribute('aria-label');
            });
        });
    });
});

describe('RootFoldersPage content type filter', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-types') {
                return Promise.resolve(mockContentTypes);
            }
            if (url === '/subject-types') {
                return Promise.resolve(mockSubjectTypes);
            }
            if (url === '/root-folders') {
                return Promise.resolve(mockApiFolders);
            }
            return Promise.reject(new Error(`Unexpected API call: ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders summary badges for each content type', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Content type summary"]');
            expect(summary).toBeInTheDocument();
            const badges = summary!.querySelectorAll('[data-component="badge"]');
            expect(badges.length).toBeGreaterThan(0);
        });
    });

    it('renders content type filter dropdown', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-filter="content-type"]')).toBeInTheDocument();
        });
    });

    it('filters folders by content type when filter is applied', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /root folders/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(5);
        });

        const trigger = document.querySelector('[data-filter="content-type"]') as HTMLElement;
        await user.click(trigger);

        await waitFor(() => {
            const options = screen.getAllByRole('option');
            expect(options.length).toBeGreaterThan(0);
        });
        const moviesOption = screen.getByRole('option', { name: 'Movies' });
        await user.click(moviesOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /root folders/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });

    it('shows clear button when filter is active', async () => {
        const user = userEvent.setup();

        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-filter="content-type"]')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const trigger = document.querySelector('[data-filter="content-type"]') as HTMLElement;
        await user.click(trigger);

        await waitFor(() => {
            expect(screen.getAllByRole('option').length).toBeGreaterThan(0);
        });
        const moviesOption = screen.getByRole('option', { name: 'Movies' });
        await user.click(moviesOption);

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
                contentTypeId: 'nonexistent-type',
                createdAt: '2024-01-01T00:00:00Z',
            },
        ];

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-types') {
                return Promise.resolve(mockContentTypes);
            }
            if (url === '/subject-types') {
                return Promise.resolve(mockSubjectTypes);
            }
            if (url === '/root-folders') {
                return Promise.resolve(foldersWithUnknown);
            }
            return Promise.reject(new Error(`Unexpected API call: ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders unknown variant badge for unresolved content type', async () => {
        render(<RootFoldersPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const badge = document.querySelector('[data-folder-id="folder-unknown"] [data-component="badge"]');
            expect(badge).toBeInTheDocument();
            expect(badge).toHaveAttribute('data-status', 'unknown');
            expect(badge).toHaveTextContent('Unknown');
        });
    });
});
