import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';

import { FiltersPage } from './FiltersPage';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
    };
});

vi.mock('@/lib/magpieQl', () => ({
    formatMagpieQlCriteria: vi.fn(),
    parseMagpieQl: vi.fn(),
}));

vi.mock('@/hooks/useUsers', () => ({
    useUsers: vi.fn(() => ({
        users: [
            { id: 'user-1', username: 'alice' },
            { id: 'user-2', username: 'bob' },
            { id: 'user-3', username: 'charlie' },
        ],
        isLoading: false,
        error: null,
    })),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { formatMagpieQlCriteria, parseMagpieQl } from '@/lib/magpieQl';

const mockSharedFilters = [
    {
        id: 'filter-1',
        name: 'Action Movies',
        ownerUserId: null,
        criteria: { contentHierarchyItem: { and: [{}, {}] } },
        ownerLock: false,
        createdAt: '2024-01-01T00:00:00Z',
        modifiedAt: '2024-01-15T10:30:00Z',
        createdByUserId: null,
        modifiedByUserId: null,
    },
    {
        id: 'filter-2',
        name: 'Sci-Fi >= 7.5',
        ownerUserId: null,
        criteria: { contentHierarchyItem: { and: [{}, {}, {}] } },
        ownerLock: false,
        createdAt: '2024-01-02T00:00:00Z',
        modifiedAt: null,
        createdByUserId: null,
        modifiedByUserId: null,
    },
    {
        id: 'filter-3',
        name: 'Family Friendly',
        ownerUserId: null,
        criteria: { contentHierarchyItem: { and: [{}, {}, {}, {}] } },
        ownerLock: false,
        createdAt: '2024-01-03T00:00:00Z',
        modifiedAt: '2024-01-10T08:00:00Z',
        createdByUserId: null,
        modifiedByUserId: null,
    },
    {
        id: 'filter-7',
        name: 'Comedy Classics',
        ownerUserId: null,
        criteria: { contentHierarchyItem: { and: [{}, {}] } },
        ownerLock: false,
        createdAt: '2024-01-07T00:00:00Z',
        modifiedAt: null,
        createdByUserId: null,
        modifiedByUserId: null,
    },
];

const mockMyFilters = [
    {
        id: 'filter-4',
        name: 'My Personal Filter',
        ownerUserId: 'user-1',
        criteria: { contentHierarchyItem: { and: [{}] } },
        ownerLock: true,
        createdAt: '2024-01-04T00:00:00Z',
        modifiedAt: null,
        createdByUserId: 'user-1',
        modifiedByUserId: null,
    },
    {
        id: 'filter-5',
        name: 'Horror Movies',
        ownerUserId: 'user-2',
        criteria: { contentHierarchyItem: { and: [{}, {}] } },
        ownerLock: false,
        createdAt: '2024-01-05T00:00:00Z',
        modifiedAt: null,
        createdByUserId: 'user-2',
        modifiedByUserId: null,
    },
    {
        id: 'filter-6',
        name: 'Documentary 2024',
        ownerUserId: 'user-1',
        criteria: { contentHierarchyItem: { and: [{}, {}, {}] } },
        ownerLock: false,
        createdAt: '2024-01-06T00:00:00Z',
        modifiedAt: '2024-01-12T14:20:00Z',
        createdByUserId: 'user-1',
        modifiedByUserId: null,
    },
    {
        id: 'filter-8',
        name: 'Award Winners',
        ownerUserId: 'user-3',
        criteria: { contentHierarchyItem: { and: [{}, {}, {}, {}, {}] } },
        ownerLock: true,
        createdAt: '2024-01-08T00:00:00Z',
        modifiedAt: '2024-01-14T16:00:00Z',
        createdByUserId: 'user-3',
        modifiedByUserId: null,
    },
];

function setupApiMocks() {
    const mockApiGet = vi.mocked(apiGet);
    mockApiGet.mockImplementation((url: string) => {
        if (url === '/content-filters') {
            return Promise.resolve(mockSharedFilters);
        }
        if (url === '/content-filters/my') {
            return Promise.resolve(mockMyFilters);
        }
        return Promise.resolve([]);
    });

    vi.mocked(apiPost).mockResolvedValue({});
    vi.mocked(apiPatch).mockResolvedValue(undefined);
    vi.mocked(apiDelete).mockResolvedValue(undefined);

    vi.mocked(parseMagpieQl).mockResolvedValue({ criteria: { contentHierarchyItem: { and: [{}] } } });

    vi.mocked(formatMagpieQlCriteria).mockImplementation(async (criteria: unknown) => {
        const c = criteria as { contentHierarchyItem?: { and?: unknown[] } };
        const count = c?.contentHierarchyItem?.and?.length ?? 0;
        if (count === 0) {
            return { query: '' };
        }
        if (count === 1) {
            return { query: 'monitoring = true' };
        }
        if (count === 2) {
            return { query: 'genre = Action and year >= 2020' };
        }
        if (count === 3) {
            return { query: 'genre = Sci-Fi and rating >= 7.5 and status = Monitored' };
        }
        return { query: `genre = Action and rating >= 7.0 and year >= 2000 and status = Monitored` };
    });
}

beforeEach(() => {
    vi.clearAllMocks();
    mockNavigate.mockClear();
    setupApiMocks();
});

afterEach(() => {
    vi.restoreAllMocks();
});

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
        },
    });
}

function TestWrapper({
    children,
    role = Role.Admin,
    userId = '1',
    initialEntries,
    queryClient,
}: {
    children: ReactNode;
    role?: Role;
    userId?: string;
    initialEntries?: string[];
    queryClient?: QueryClient;
}) {
    const mockUser = { id: userId, username: 'test', role };
    const client = queryClient ?? createTestQueryClient();

    return (
        <QueryClientProvider client={client}>
            <MemoryRouter initialEntries={initialEntries}>
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

async function openOverflowMenu(user: ReturnType<typeof userEvent.setup>, filterId: string) {
    const row = document.querySelector(`[data-filter-id="${filterId}"]`);
    const trigger = row?.querySelector('[data-action="more-actions"]') as HTMLButtonElement;
    await user.click(trigger);

    await waitFor(() => {
        expect(document.querySelector('[data-component="dropdown-menu"]')).toBeInTheDocument();
    });
    return trigger;
}

describe('FiltersPage', () => {
    it('renders with correct data-page attribute', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-page="filters"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'Filters', level: 1 })).toBeInTheDocument();
    });

    it('renders the page icon as decorative', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        const icon = screen.getByTestId('filters-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('FiltersPage list items', () => {
    it('renders filter items in the list', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /content filters/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(8);
        });
    });

    it('renders filter names', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });
        expect(screen.getByText('Sci-Fi >= 7.5')).toBeInTheDocument();
        expect(screen.getByText('Family Friendly')).toBeInTheDocument();
        expect(screen.getByText('My Personal Filter')).toBeInTheDocument();
    });

    it('renders query preview with criteria count', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getAllByText('genre = Action and year >= 2020').length).toBeGreaterThan(0);
        });
    });

    it('applies data-filter-id to items', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const items = document.querySelectorAll('[data-filter-id]');
            expect(items.length).toBe(8);
            expect(items[0]).toHaveAttribute('data-filter-id', 'filter-1');
        });
    });

    it('shows Shared badge for shared filters', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const sharedBadges = screen.getAllByText('Shared');
            expect(sharedBadges.length).toBeGreaterThan(0);
        });
    });

    it('shows Personal badge for personal filters', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const personalBadges = screen.getAllByText('Personal');
            expect(personalBadges.length).toBeGreaterThan(0);
        });
    });

    it('resolves owner names from user lookup', async () => {
        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(screen.getAllByText('alice').length).toBe(2);
        expect(screen.getByText('bob')).toBeInTheDocument();
        expect(screen.getByText('charlie')).toBeInTheDocument();
    });
});

describe('FiltersPage filters', () => {
    it('renders filter controls', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        const filterBar = document.querySelector('[data-component="filter-filters"]');
        expect(filterBar).toBeInTheDocument();
        expect(filterBar).toHaveClass('filters-page__ownership');
        expect(filterBar?.querySelector('[data-component="ownership-toggle"]')).toBeInTheDocument();
    });

    it('renders the ownership toggle', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('radiogroup', { name: /filter by ownership/i })).toBeInTheDocument();
        expect(screen.getByRole('radio', { name: 'All' })).toBeInTheDocument();
        expect(screen.getByRole('radio', { name: 'Mine' })).toBeInTheDocument();
    });

    it('filters content filters to the current user when Mine is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('radio', { name: 'Mine' }));

        const list = screen.getByRole('list', { name: /content filters/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(2);
    });

    it('marks the Mine scope active once selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'true');

        await user.click(screen.getByRole('radio', { name: 'Mine' }));

        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'false');
    });

    it('restores all filters when All is selected after Mine', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('radio', { name: 'Mine' }));
        await user.click(screen.getByRole('radio', { name: 'All' }));

        const list = screen.getByRole('list', { name: /content filters/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(8);
    });

    it('shows empty state when filters match nothing', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([]);
            }
            if (url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            return Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No filters found')).toBeInTheDocument();
            expect(
                screen.getByText('Reusable filters can be applied in the library and wishlists.'),
            ).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="add-filter"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="add-filter-empty"]')).not.toBeInTheDocument();
    });
});

describe('FiltersPage Add Filter button', () => {
    it('shows Add Filter button in header for Requester role', async () => {
        render(
            <TestWrapper role={Role.Requester}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="add-filter"]')).toBeInTheDocument();
    });

    it('shows Add Filter button in header for Manager role', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="add-filter"]')).toBeInTheDocument();
    });

    it('shows Add Filter button in header for Admin role', async () => {
        render(
            <TestWrapper role={Role.Admin}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="add-filter"]')).toBeInTheDocument();
    });

    it('hides Add Filter button for Viewer role', async () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="add-filter"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="add-filter-empty"]')).not.toBeInTheDocument();
    });

    it('filter form cancel button has X icon and data-action attribute', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const addButton = document.querySelector('[data-action="add-filter"]') as HTMLButtonElement;
        await user.click(addButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const cancelButton = document.querySelector('[data-action="cancel"]');
        expect(cancelButton).toBeInTheDocument();
        expect(cancelButton).toHaveTextContent('Cancel');

        const icon = cancelButton?.querySelector('[aria-hidden="true"]');
        expect(icon).toBeInTheDocument();
    });

    it('prompts before closing a dirty Add Filter draft with title criteria', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const addButton = document.querySelector('[data-action="add-filter"]') as HTMLButtonElement;
        await user.click(addButton);

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Create Filter' })).toBeInTheDocument();
        });

        const nameInput = screen.getByLabelText(/name/i);
        const titleInput = screen.getByLabelText('Title');
        await user.type(nameInput, 'Draft Filter');
        await user.type(titleInput, 'Inception');

        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        await waitFor(() => {
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-component="filter-form"]')).toHaveAttribute('data-state', 'open');
        expect(nameInput).toHaveValue('Draft Filter');
        expect(titleInput).toHaveValue('Inception');
        expect(apiPost).not.toHaveBeenCalled();
    });
});

describe('FiltersPage Edit action', () => {
    it('Manager can edit shared filters and unlocked personal filters', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(7);
    });

    it('Requester can only edit their own filters', async () => {
        render(
            <TestWrapper role={Role.Requester} userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(2);
    });

    it('Viewer cannot edit filters', async () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(0);
    });

    it('edit button has correct aria-label', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const editButton = document.querySelector('[data-action="edit"]');
        expect(editButton).toHaveAttribute('aria-label');
    });
});

describe('FiltersPage overflow menu actions', () => {
    it('shows overflow menu trigger for all filters', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const menuTriggers = document.querySelectorAll('[data-action="more-actions"]');
        expect(menuTriggers.length).toBe(8);
    });

    it('overflow menu trigger has correct aria-label', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const trigger = filter1?.querySelector('[data-action="more-actions"]');
        expect(trigger).toHaveAttribute('aria-label', 'More actions for Action Movies');
    });

    it('Manager sees Delete option in overflow menu', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const deleteItem = document.querySelector('[data-action="delete"]');
        expect(deleteItem).toBeInTheDocument();
    });

    it('Requester does not see Delete option in overflow menu', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester} userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-4');

        const deleteItem = document.querySelector('[data-action="delete"]');
        expect(deleteItem).not.toBeInTheDocument();
    });

    it('Viewer does not see Delete option in overflow menu', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Viewer}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const deleteItem = document.querySelector('[data-action="delete"]');
        expect(deleteItem).not.toBeInTheDocument();
    });
});

describe('FiltersPage delete confirmation', () => {
    it('opens confirmation dialog when delete is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const deleteItem = document.querySelector('[data-action="delete"]') as HTMLElement;
        await user.click(deleteItem);

        await waitFor(() => {
            expect(screen.getByText('Delete Filter')).toBeInTheDocument();
        });

        expect(screen.getByText(/Are you sure you want to delete "Action Movies"\?/)).toBeInTheDocument();
    });

    it('does not delete immediately when delete is clicked', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const deleteItem = document.querySelector('[data-action="delete"]') as HTMLElement;
        await user.click(deleteItem);

        await waitFor(() => {
            expect(screen.getByText('Delete Filter')).toBeInTheDocument();
        });

        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('deletes filter after confirmation', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const deleteItem = document.querySelector('[data-action="delete"]') as HTMLElement;
        await user.click(deleteItem);

        await waitFor(() => {
            expect(screen.getByText('Delete Filter')).toBeInTheDocument();
        });

        const confirmDeleteButton = document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement;
        await user.click(confirmDeleteButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalled();
        });
    });

    it('closes confirmation dialog on cancel', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const deleteItem = document.querySelector('[data-action="delete"]') as HTMLElement;
        await user.click(deleteItem);

        await waitFor(() => {
            expect(screen.getByText('Delete Filter')).toBeInTheDocument();
        });

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLButtonElement;
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete Filter')).not.toBeInTheDocument();
        });
    });
});

describe('FiltersPage duplicate action', () => {
    it('shows a labelled duplicate item in the overflow menu', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const button = document.querySelector('[data-action="duplicate"]');
        expect(button).toBeInTheDocument();
        expect(button).toHaveAttribute('data-target', 'filter-1');
        expect(button).toHaveTextContent('Duplicate');
    });

    it('shows the duplicate item to a requester who cannot edit a shared filter', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        expect(filter1?.querySelector('[data-action="edit"]')).not.toBeInTheDocument();

        await openOverflowMenu(user, 'filter-1');
        expect(document.querySelector('[data-action="duplicate"]')).toBeInTheDocument();
    });

    it('hides the duplicate item from a viewer who cannot create filters', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Viewer}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');
        expect(document.querySelector('[data-action="duplicate"]')).not.toBeInTheDocument();
    });

    it('opens the create form pre-filled with a copied name', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');
        const duplicateButton = document.querySelector('[data-action="duplicate"]') as HTMLButtonElement;
        await user.click(duplicateButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toHaveValue('Action Movies (Copy)');
    });

    it('creates a brand-new filter when a duplicate is saved', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');
        const duplicateButton = document.querySelector('[data-action="duplicate"]') as HTMLButtonElement;
        await user.click(duplicateButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const saveButton = document.querySelector('[data-action="save-filter"]') as HTMLButtonElement;
        await user.click(saveButton);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalled();
        });
    });
});

describe('FiltersPage stale edit guard', () => {
    it('does not create a duplicate when editing a filter that no longer exists', async () => {
        const user = userEvent.setup();
        const mockApiGet = vi.mocked(apiGet);
        const mockApiPost = vi.mocked(apiPost);
        const mockApiPatch = vi.mocked(apiPatch);
        const queryClient = createTestQueryClient();

        render(
            <TestWrapper role={Role.Manager} queryClient={queryClient}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const editButton = filter1?.querySelector('[data-action="edit"]') as HTMLButtonElement;
        await user.click(editButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve(mockSharedFilters.filter((f) => f.id !== 'filter-1'));
            }
            if (url === '/content-filters/my') {
                return Promise.resolve(mockMyFilters);
            }
            return Promise.resolve([]);
        });

        await queryClient.invalidateQueries({ queryKey: ['content-filters'] });

        await waitFor(() => {
            expect(document.querySelector('[data-filter-id="filter-1"]')).not.toBeInTheDocument();
        });

        const saveButton = document.querySelector('[data-action="save-filter"]') as HTMLButtonElement;
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByTestId('toast-title')).toHaveTextContent('Filter Unavailable');
        });

        expect(mockApiPost).not.toHaveBeenCalled();
        expect(mockApiPatch).not.toHaveBeenCalled();
    });
});

describe('FiltersPage owner lock indicator', () => {
    it('shows lock indicator for personal filters with ownerLock=true', async () => {
        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const lockIndicators = document.querySelectorAll('[data-owner-lock="true"]');
        expect(lockIndicators.length).toBe(2);
    });

    it('does not show lock indicator for shared filters', async () => {
        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const lockIndicator = filter1?.querySelector('[data-owner-lock]');
        expect(lockIndicator).not.toBeInTheDocument();
    });

    it('lock indicator has accessible label', async () => {
        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter4 = document.querySelector('[data-filter-id="filter-4"]');
        const lockIndicator = filter4?.querySelector('[data-owner-lock="true"]');
        expect(lockIndicator).toHaveAttribute('aria-label', 'Locked - Only you can edit');
    });
});

describe('FiltersPage lock/unlock actions in overflow menu', () => {
    it('shows unlock option for locked personal filters owned by current user', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-4');

        const unlockItem = document.querySelector('[data-action="unlock"]');
        expect(unlockItem).toBeInTheDocument();
    });

    it('shows lock option for unlocked personal filters owned by current user', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-6');

        const lockItem = document.querySelector('[data-action="lock"]');
        expect(lockItem).toBeInTheDocument();
    });

    it('does not show lock/unlock option for shared filters', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const lockItem = document.querySelector('[data-action="lock"]');
        const unlockItem = document.querySelector('[data-action="unlock"]');
        expect(lockItem).not.toBeInTheDocument();
        expect(unlockItem).not.toBeInTheDocument();
    });

    it('does not show lock/unlock option for filters not owned by current user', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-5');

        const lockItem = document.querySelector('[data-action="lock"]');
        const unlockItem = document.querySelector('[data-action="unlock"]');
        expect(lockItem).not.toBeInTheDocument();
        expect(unlockItem).not.toBeInTheDocument();
    });

    it('shows toast notification after lock completes', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-6');

        const lockItem = document.querySelector('[data-action="lock"]') as HTMLElement;
        await user.click(lockItem);

        await waitFor(
            () => {
                expect(screen.getByTestId('toast-title')).toHaveTextContent('Documentary 2024');
            },
            { timeout: 2000 },
        );
    });
});

describe('FiltersPage edit permissions with ownerLock', () => {
    it('Manager cannot edit locked personal filters owned by others', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter8 = document.querySelector('[data-filter-id="filter-8"]');
        const editButton = filter8?.querySelector('[data-action="edit"]');
        expect(editButton).not.toBeInTheDocument();
    });

    it('Manager can edit unlocked personal filters owned by others', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter5 = document.querySelector('[data-filter-id="filter-5"]');
        const editButton = filter5?.querySelector('[data-action="edit"]');
        expect(editButton).toBeInTheDocument();
    });

    it('Owner can edit their own locked filters', async () => {
        render(
            <TestWrapper role={Role.Requester} userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter4 = document.querySelector('[data-filter-id="filter-4"]');
        const editButton = filter4?.querySelector('[data-action="edit"]');
        expect(editButton).toBeInTheDocument();
    });
});

describe('FiltersPage empty state', () => {
    it('hides empty-state Add Filter button when filters are active', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([]);
            }
            if (url === '/content-filters/my') {
                return Promise.resolve(mockMyFilters);
            }
            return Promise.resolve([]);
        });

        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Personal Filter')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('radio', { name: 'Mine' }));

        expect(screen.getByText('No filters found')).toBeInTheDocument();
        expect(screen.getByText('Try adjusting your filters')).toBeInTheDocument();

        expect(document.querySelector('[data-action="add-filter-empty"]')).not.toBeInTheDocument();
    });
});

describe('FiltersPage query preview', () => {
    it('renders query text for each filter', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const queryElements = document.querySelectorAll('[data-query]');
            expect(queryElements.length).toBe(8);
        });
    });

    it('displays the criteria count in query text', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getAllByText('genre = Action and year >= 2020').length).toBeGreaterThan(0);
        });
    });

    it('query element has title attribute for full text on hover', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-query]')).not.toBeNull();
        });

        const queryElement = document.querySelector('[data-query]');
        expect(queryElement).toHaveAttribute('title');
    });

    it('query element uses code element for semantic markup', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-query]')).not.toBeNull();
        });

        const queryElement = document.querySelector('[data-query]');
        expect(queryElement?.tagName.toLowerCase()).toBe('code');
    });

    it('query preview is styled as monospace', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getAllByTestId('filters-page__item-query').length).toBeGreaterThan(0);
        });
    });

    it('keeps a long unbroken query and wishlist badge contained with the full query available', async () => {
        const longQuery = `genre = ${'UnbrokenQuerySegment'.repeat(10)}`;
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([
                    {
                        id: 'filter-long',
                        name: 'Overflowing Filter',
                        ownerUserId: null,
                        criteria: { contentHierarchyItem: { and: [{}] } },
                        ownerLock: false,
                        createdAt: '2024-01-01T00:00:00Z',
                        modifiedAt: null,
                        createdByUserId: null,
                        modifiedByUserId: null,
                    },
                ]);
            }
            if (url === '/wishlists') {
                return Promise.resolve([
                    {
                        id: 'wishlist-1',
                        contentFilterId: 'filter-long',
                        name: 'ReallyLongUnbrokenWishlistName'.repeat(3),
                    },
                ]);
            }
            return Promise.resolve([]);
        });
        vi.mocked(formatMagpieQlCriteria).mockResolvedValue({ query: longQuery });

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        const queryElement = await screen.findByTitle(longQuery);

        expect(queryElement).toHaveClass('filters-page__item-query');
        expect(queryElement).toHaveTextContent(longQuery);
        expect(queryElement.closest('.filters-page__item-info')).not.toBeNull();

        const wishlistBadge = document.querySelector('[data-action="open-wishlist"]');
        expect(wishlistBadge?.querySelector('.filters-page__wishlist-badge-label')).not.toBeNull();
        expect(wishlistBadge?.closest('.filters-page__item-info')).not.toBeNull();
    });
});

describe('FiltersPage accessibility', () => {
    it('filters list has accessible label', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /content filters/i })).toBeInTheDocument();
    });

    it('ownership toggle has an accessible group label', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('radiogroup', { name: /filter by ownership/i })).toBeInTheDocument();
    });

    it('inline action buttons have aria-labels', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        editButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });

        const viewButtons = document.querySelectorAll('[data-action="view-in-library"]');
        viewButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });

        const menuTriggers = document.querySelectorAll('[data-action="more-actions"]');
        menuTriggers.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });

    it('add filter button has data-action attribute', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const addButton = document.querySelector('[data-action="add-filter"]');
        expect(addButton).toBeInTheDocument();
        expect(addButton).toHaveAttribute('data-action', 'add-filter');
    });
});

describe('FiltersPage view in library action', () => {
    it('shows view in library button for all filters', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const viewButtons = document.querySelectorAll('[data-action="view-in-library"]');
        expect(viewButtons.length).toBe(8);
    });

    it('shows view in library button for Viewer role', async () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const viewButtons = document.querySelectorAll('[data-action="view-in-library"]');
        expect(viewButtons.length).toBe(8);
    });

    it('view in library button has correct aria-label', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const viewButton = filter1?.querySelector('[data-action="view-in-library"]');
        expect(viewButton).toHaveAttribute('aria-label', 'View Action Movies in Library');
    });

    it('view button shows labeled text', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const viewButton = filter1?.querySelector('[data-action="view-in-library"]');
        expect(viewButton).toHaveTextContent('View');
    });

    it('navigates to library referencing the saved filter by id', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const queryElements = document.querySelectorAll('[data-query]');
            expect(queryElements.length).toBe(8);
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const viewButton = filter1?.querySelector('[data-action="view-in-library"]') as HTMLButtonElement;

        await user.click(viewButton);

        expect(mockNavigate).toHaveBeenCalledWith(expect.stringContaining('/library/all?savedFilter='));
    });

    it('references the saved filter by id alone, with no embedded expression', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const queryElements = document.querySelectorAll('[data-query]');
            expect(queryElements.length).toBe(8);
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const viewButton = filter1?.querySelector('[data-action="view-in-library"]') as HTMLButtonElement;

        await user.click(viewButton);

        const target = String(mockNavigate.mock.calls[0][0]);
        const search = new URLSearchParams(target.split('?')[1]);
        expect(search.get('savedFilter')).toBe('filter-1');
        expect(search.get('filter')).toBeNull();
    });
});

describe('FiltersPage search providers action', () => {
    it('navigates to the add page with the saved filter preloaded', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const queryElements = document.querySelectorAll('[data-query]');
            expect(queryElements.length).toBe(8);
        });

        await openOverflowMenu(user, 'filter-1');

        const searchItem = document.querySelector('[data-action="search-providers"]') as HTMLElement;
        expect(searchItem).toBeInTheDocument();
        expect(searchItem).toHaveTextContent('Search providers');
        await user.click(searchItem);

        expect(mockNavigate).toHaveBeenCalledWith(
            '/library/add',
            expect.objectContaining({
                state: expect.objectContaining({
                    savedFilterId: 'filter-1',
                    filterQuery: expect.any(String),
                }),
            }),
        );
    });
});

describe('FiltersPage convert to wishlist action', () => {
    it('navigates to the wishlists page with the filter criteria seeded', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const queryElements = document.querySelectorAll('[data-query]');
            expect(queryElements.length).toBe(8);
        });

        await openOverflowMenu(user, 'filter-1');

        const convertItem = document.querySelector('[data-action="convert-to-wishlist"]') as HTMLElement;
        expect(convertItem).toBeInTheDocument();
        expect(convertItem).toHaveTextContent('Convert to wishlist');
        await user.click(convertItem);

        expect(mockNavigate).toHaveBeenCalledWith(
            '/wishlists',
            expect.objectContaining({
                state: expect.objectContaining({
                    createCriteria: expect.any(Object),
                    createName: 'Action Movies',
                }),
            }),
        );
    });
});

describe('FiltersPage wishlist-backed filters', () => {
    const wishlistBackedFilter = {
        id: 'filter-9',
        name: 'Wishlist Backing',
        ownerUserId: null,
        criteria: { contentHierarchyItem: { and: [{}, {}] } },
        ownerLock: false,
        createdAt: '2024-01-09T00:00:00Z',
        modifiedAt: null,
        createdByUserId: null,
        modifiedByUserId: null,
    };

    function setupWishlistBackedMocks() {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([mockSharedFilters[0], wishlistBackedFilter]);
            }
            if (url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/wishlists') {
                return Promise.resolve([
                    {
                        id: 'wishlist-1',
                        contentFilterId: 'filter-9',
                        name: 'My Wishlist',
                    },
                ]);
            }
            return Promise.resolve([]);
        });
    }

    it('shows a "Part of {wishlist}" badge linking to the wishlist detail page', async () => {
        setupWishlistBackedMocks();

        render(
            <TestWrapper role={Role.Admin}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Wishlist Backing')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-filter-id="filter-9"]');
        const badge = row?.querySelector('[data-action="open-wishlist"]');
        expect(badge).toBeInTheDocument();
        expect(badge).toHaveTextContent('Part of My Wishlist');
        expect(badge).toHaveAttribute('href', '/wishlists/wishlist-1');
    });

    it('shows the edit pencil disabled with a wishlist tooltip, and no delete or lock', async () => {
        const user = userEvent.setup();
        setupWishlistBackedMocks();

        render(
            <TestWrapper role={Role.Admin}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Wishlist Backing')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-filter-id="filter-9"]');
        const editButton = row?.querySelector('[data-action="edit"]');
        expect(editButton).toBeInTheDocument();
        expect(editButton).toHaveAttribute('aria-disabled', 'true');
        expect(editButton).toHaveAttribute('data-disabled-reason', 'wishlist-bound');

        await openOverflowMenu(user, 'filter-9');
        expect(document.querySelector('[data-action="delete"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="lock"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="unlock"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="convert-to-wishlist"]')).not.toBeInTheDocument();
    });

    it('hides the wishlist-bound edit pencil for a role that cannot edit the filter', async () => {
        setupWishlistBackedMocks();

        render(
            <TestWrapper role={Role.Viewer}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Wishlist Backing')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-filter-id="filter-9"]');
        expect(row?.querySelector('[data-action="edit"]')).not.toBeInTheDocument();
        expect(row?.querySelector('[data-action="view-in-library"]')).toBeInTheDocument();
    });

    it('does not open the editor when the wishlist-bound pencil is clicked', async () => {
        const user = userEvent.setup();
        setupWishlistBackedMocks();

        render(
            <TestWrapper role={Role.Admin}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Wishlist Backing')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-filter-id="filter-9"]');
        const editButton = row?.querySelector('[data-action="edit"]') as HTMLButtonElement;
        await user.click(editButton);

        expect(screen.queryByText('Edit Filter')).not.toBeInTheDocument();
    });

    it('keeps View and Duplicate available on wishlist-backed rows', async () => {
        const user = userEvent.setup();
        setupWishlistBackedMocks();

        render(
            <TestWrapper role={Role.Admin}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Wishlist Backing')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-filter-id="filter-9"]');
        expect(row?.querySelector('[data-action="view-in-library"]')).toBeInTheDocument();

        await openOverflowMenu(user, 'filter-9');
        expect(document.querySelector('[data-action="duplicate"]')).toBeInTheDocument();
    });

    it('keeps edit and delete on a standalone filter', async () => {
        const user = userEvent.setup();
        setupWishlistBackedMocks();

        render(
            <TestWrapper role={Role.Admin}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-filter-id="filter-1"]');
        expect(row?.querySelector('[data-action="edit"]')).toBeInTheDocument();
        expect(row?.querySelector('[data-action="open-wishlist"]')).not.toBeInTheDocument();

        await openOverflowMenu(user, 'filter-1');
        expect(document.querySelector('[data-action="delete"]')).toBeInTheDocument();
    });
});

describe('FiltersPage edit deep-link', () => {
    it('opens the editor for the filter named in the ?edit= param', async () => {
        render(
            <TestWrapper role={Role.Manager} initialEntries={['/?edit=filter-1']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Edit Filter')).toBeInTheDocument();
        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toHaveValue('Action Movies');
    });

    it('ignores an unknown ?edit= id without opening a dialog', async () => {
        render(
            <TestWrapper role={Role.Manager} initialEntries={['/?edit=does-not-exist']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
});

describe('FiltersPage URL persistence', () => {
    it('restores the Mine scope from URL on mount', async () => {
        render(
            <TestWrapper userId="user-1" initialEntries={['/?scope=mine']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /content filters/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(2);
        });

        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveAttribute('aria-checked', 'true');
    });

    it('shows all filters when no scope param is present', async () => {
        render(
            <TestWrapper userId="user-1">
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const list = screen.getByRole('list', { name: /content filters/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(8);
        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'true');
    });

    it('drops the scope param from URL when All is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1" initialEntries={['/?scope=mine']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('radio', { name: 'Mine' })).toHaveAttribute('aria-checked', 'true');
        });

        await user.click(screen.getByRole('radio', { name: 'All' }));

        const list = screen.getByRole('list', { name: /content filters/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(8);
        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'true');
    });
});
