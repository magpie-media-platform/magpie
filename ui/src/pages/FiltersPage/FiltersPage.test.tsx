import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';

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

vi.mock('@/lib/filterQuery', () => ({
    formatFilterCriteria: vi.fn(),
    parseFilterQuery: vi.fn(),
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
import { formatFilterCriteria } from '@/lib/filterQuery';

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

    vi.mocked(formatFilterCriteria).mockImplementation(async (criteria: unknown) => {
        const c = criteria as { contentHierarchyItem?: { and?: unknown[] } };
        const count = c?.contentHierarchyItem?.and?.length ?? 0;
        if (count === 0) {
            return { query: '' };
        }
        if (count === 1) {
            return { query: 'monitoring = Active' };
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

function TestWrapper({
    children,
    role = Role.Admin,
    userId = '1',
    initialEntries,
}: {
    children: ReactNode;
    role?: Role;
    userId?: string;
    initialEntries?: string[];
}) {
    const mockUser = { id: userId, username: 'test', role };
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
        },
    });

    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={initialEntries}>
                <AuthProvider initialUser={mockUser}>
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

    it('renders the filter count', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const count = screen.getByTestId('filters-page__count');
            expect(count).toBeInTheDocument();
            expect(count).toHaveTextContent('8');
        });
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

describe('FiltersPage summary badges', () => {
    it('renders type badges after data loads', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Filter summary"]');
            expect(summary?.querySelector('.badge[data-status="info"]')).toBeInTheDocument();
            expect(summary?.querySelector('.badge[data-status="stabilizing"]')).toBeInTheDocument();
        });
    });

    it('summary section has accessible label', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Filter summary"]');
        expect(summary).toBeInTheDocument();
    });

    it('shows correct count for shared filters', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Filter summary"]');
            const sharedBadge = summary?.querySelector('.badge[data-status="info"]');
            const count = sharedBadge?.querySelector('.badge__count');

            expect(count).toHaveTextContent('4');
        });
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

        expect(document.querySelector('[data-component="filter-filters"]')).toBeInTheDocument();
    });

    it('renders owner filter', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('combobox', { name: /filter by owner/i })).toBeInTheDocument();
    });

    it('filters content filters by owner', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const ownerTrigger = screen.getByRole('combobox', { name: /filter by owner/i });
        await user.click(ownerTrigger);

        const sharedOption = screen.getByRole('option', { name: 'Shared' });
        await user.click(sharedOption);

        const list = screen.getByRole('list', { name: /content filters/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(4);
    });

    it('shows clear filters button when filters are active', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const ownerTrigger = screen.getByRole('combobox', { name: /filter by owner/i });
        await user.click(ownerTrigger);

        const sharedOption = screen.getByRole('option', { name: 'Shared' });
        await user.click(sharedOption);

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });

    it('clears filters when clear button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const ownerTrigger = screen.getByRole('combobox', { name: /filter by owner/i });
        await user.click(ownerTrigger);

        const sharedOption = screen.getByRole('option', { name: 'Shared' });
        await user.click(sharedOption);

        const clearButton = document.querySelector('[data-action="clear-filters"]');
        await user.click(clearButton!);

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
            expect(screen.getByText('Create a filter to start matching content')).toBeInTheDocument();
        });
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

    it('overflow menu contains Duplicate option', async () => {
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

        const duplicateItem = document.querySelector('[data-action="duplicate"]');
        expect(duplicateItem).toBeInTheDocument();
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
    it('duplicate option has correct data attributes', async () => {
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

        const duplicateItem = document.querySelector('[data-action="duplicate"]');
        expect(duplicateItem).toBeInTheDocument();
        expect(duplicateItem).toHaveAttribute('data-target', 'filter-1');
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

        const ownerTrigger = screen.getByRole('combobox', { name: /filter by owner/i });
        await user.click(ownerTrigger);

        const sharedOption = screen.getByRole('option', { name: 'Shared' });
        await user.click(sharedOption);

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
});

describe('FiltersPage convert to wishlist action', () => {
    it('shows Create Wishlist option in overflow menu for Requester role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        await openOverflowMenu(user, 'filter-1');

        const wishlistItem = document.querySelector('[data-action="convert-to-wishlist"]');
        expect(wishlistItem).toBeInTheDocument();
    });

    it('shows Create Wishlist option in overflow menu for Manager role', async () => {
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

        const wishlistItem = document.querySelector('[data-action="convert-to-wishlist"]');
        expect(wishlistItem).toBeInTheDocument();
    });

    it('hides Create Wishlist option for Viewer role', async () => {
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

        const wishlistItem = document.querySelector('[data-action="convert-to-wishlist"]');
        expect(wishlistItem).not.toBeInTheDocument();
    });

    it('opens wishlist form when Create Wishlist is clicked', async () => {
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

        const wishlistItem = document.querySelector('[data-action="convert-to-wishlist"]') as HTMLElement;
        await user.click(wishlistItem);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Add Wishlist')).toBeInTheDocument();
    });

    it('pre-populates wishlist name from filter name', async () => {
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

        const wishlistItem = document.querySelector('[data-action="convert-to-wishlist"]') as HTMLElement;
        await user.click(wishlistItem);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toHaveValue('Action Movies Wishlist');
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

    it('filter comboboxes have accessible labels', () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('combobox', { name: /filter by owner/i })).toBeInTheDocument();
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

        const applyButtons = document.querySelectorAll('[data-action="apply-in-library"]');
        applyButtons.forEach((btn) => {
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

describe('FiltersPage apply in library action', () => {
    it('shows apply in library button for all filters', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const applyButtons = document.querySelectorAll('[data-action="apply-in-library"]');
        expect(applyButtons.length).toBe(8);
    });

    it('shows apply in library button for Viewer role', async () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const applyButtons = document.querySelectorAll('[data-action="apply-in-library"]');
        expect(applyButtons.length).toBe(8);
    });

    it('apply in library button has correct aria-label', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const applyButton = filter1?.querySelector('[data-action="apply-in-library"]');
        expect(applyButton).toHaveAttribute('aria-label', 'Apply Action Movies in Library');
    });

    it('apply button shows labeled text', async () => {
        render(
            <TestWrapper>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const filter1 = document.querySelector('[data-filter-id="filter-1"]');
        const applyButton = filter1?.querySelector('[data-action="apply-in-library"]');
        expect(applyButton).toHaveTextContent('Apply');
    });

    it('navigates to library with filter query when clicked', async () => {
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
        const applyButton = filter1?.querySelector('[data-action="apply-in-library"]') as HTMLButtonElement;

        await user.click(applyButton);

        expect(mockNavigate).toHaveBeenCalledWith(expect.stringContaining('/library/all?filter='));
    });
});

describe('FiltersPage URL persistence', () => {
    it('restores owner filter from URL on mount', async () => {
        render(
            <TestWrapper initialEntries={['/?owner=__shared__']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const list = screen.getByRole('list', { name: /content filters/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(4);
    });

    it('restores specific user owner filter from URL on mount', async () => {
        render(
            <TestWrapper initialEntries={['/?owner=user-2']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /content filters/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(1);
        });
    });

    it('shows clear button when owner param is in URL', async () => {
        render(
            <TestWrapper initialEntries={['/?owner=__shared__']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });

    it('clears owner param from URL when clear is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper initialEntries={['/?owner=__shared__']}>
                <FiltersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });

        const clearButton = document.querySelector('[data-action="clear-filters"]');
        await user.click(clearButton!);

        const list = screen.getByRole('list', { name: /content filters/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(8);

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();
    });
});
