import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';

import { QualityTiersPage } from './QualityTiersPage';

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

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import type { ApiQualityTierList } from '@/hooks/useQualityTierLists';
import type { ApiTag } from '@/hooks/useTags';

const mockApiTierLists: ApiQualityTierList[] = [
    {
        id: 'qtl-1',
        name: 'HD Standard',
        upgradeAllowed: true,
        createdAt: '2024-01-01T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'qtl-1',
                qualityTierId: 't1-1',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2024-01-01T00:00:00Z',
                qualityTier: {
                    id: 't1-1',
                    name: 'Bluray-1080p',
                    criteria: { source: ['Bluray'], resolution: ['1080p'], codec: ['x264'] },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-1',
                qualityTierId: 't1-2',
                ordinal: 1,
                isCutoff: false,
                createdAt: '2024-01-01T00:00:00Z',
                qualityTier: {
                    id: 't1-2',
                    name: 'HDTV-1080p',
                    criteria: { source: ['HDTV'], resolution: ['1080p'] },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-1',
                qualityTierId: 't1-3',
                ordinal: 2,
                isCutoff: false,
                createdAt: '2024-01-01T00:00:00Z',
                qualityTier: {
                    id: 't1-3',
                    name: 'WEB-1080p',
                    criteria: { source: ['WEB'], resolution: ['1080p'] },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-1',
                qualityTierId: 't1-4',
                ordinal: 3,
                isCutoff: false,
                createdAt: '2024-01-01T00:00:00Z',
                qualityTier: {
                    id: 't1-4',
                    name: 'HDTV-720p',
                    criteria: { source: ['HDTV'], resolution: ['720p'] },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'qtl-2',
        name: '4K Ultra HD',
        upgradeAllowed: true,
        createdAt: '2024-01-02T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'qtl-2',
                qualityTierId: 't2-1',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2024-01-02T00:00:00Z',
                qualityTier: {
                    id: 't2-1',
                    name: 'Bluray-2160p',
                    criteria: { source: ['Bluray'], resolution: ['2160p'], codec: ['x265'], hdr: true },
                    createdAt: '2024-01-02T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-2',
                qualityTierId: 't2-2',
                ordinal: 1,
                isCutoff: false,
                createdAt: '2024-01-02T00:00:00Z',
                qualityTier: {
                    id: 't2-2',
                    name: 'WEB-2160p',
                    criteria: { source: ['WEB'], resolution: ['2160p'], hdr: true },
                    createdAt: '2024-01-02T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-2',
                qualityTierId: 't2-3',
                ordinal: 2,
                isCutoff: false,
                createdAt: '2024-01-02T00:00:00Z',
                qualityTier: {
                    id: 't2-3',
                    name: 'HDTV-2160p',
                    criteria: { source: ['HDTV'], resolution: ['2160p'] },
                    createdAt: '2024-01-02T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'qtl-3',
        name: 'Any Quality',
        upgradeAllowed: false,
        createdAt: '2024-01-03T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'qtl-3',
                qualityTierId: 't3-1',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2024-01-03T00:00:00Z',
                qualityTier: {
                    id: 't3-1',
                    name: 'Any',
                    criteria: { source: ['*'] },
                    createdAt: '2024-01-03T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'qtl-4',
        name: 'SD Only',
        upgradeAllowed: false,
        createdAt: '2024-01-04T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'qtl-4',
                qualityTierId: 't4-1',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2024-01-04T00:00:00Z',
                qualityTier: {
                    id: 't4-1',
                    name: 'DVD',
                    criteria: { source: ['DVD'], resolution: ['480p'] },
                    createdAt: '2024-01-04T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-4',
                qualityTierId: 't4-2',
                ordinal: 1,
                isCutoff: false,
                createdAt: '2024-01-04T00:00:00Z',
                qualityTier: {
                    id: 't4-2',
                    name: 'SDTV',
                    criteria: { source: ['SDTV'], resolution: ['480p'] },
                    createdAt: '2024-01-04T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'qtl-5',
        name: 'WEB Preferred',
        upgradeAllowed: true,
        createdAt: '2024-01-05T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'qtl-5',
                qualityTierId: 't5-1',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2024-01-05T00:00:00Z',
                qualityTier: {
                    id: 't5-1',
                    name: 'WEB-2160p',
                    criteria: { source: ['WEB'], resolution: ['2160p'], hdr: true },
                    createdAt: '2024-01-05T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-5',
                qualityTierId: 't5-2',
                ordinal: 1,
                isCutoff: false,
                createdAt: '2024-01-05T00:00:00Z',
                qualityTier: {
                    id: 't5-2',
                    name: 'WEB-1080p',
                    criteria: { source: ['WEB'], resolution: ['1080p'], codec: ['x264'] },
                    createdAt: '2024-01-05T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-5',
                qualityTierId: 't5-3',
                ordinal: 2,
                isCutoff: false,
                createdAt: '2024-01-05T00:00:00Z',
                qualityTier: {
                    id: 't5-3',
                    name: 'WEB-720p',
                    criteria: { source: ['WEB'], resolution: ['720p'], codec: ['x264'] },
                    createdAt: '2024-01-05T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-5',
                qualityTierId: 't5-4',
                ordinal: 3,
                isCutoff: false,
                createdAt: '2024-01-05T00:00:00Z',
                qualityTier: {
                    id: 't5-4',
                    name: 'WEB-480p',
                    criteria: { source: ['WEB'], resolution: ['480p'] },
                    createdAt: '2024-01-05T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-5',
                qualityTierId: 't5-5',
                ordinal: 4,
                isCutoff: false,
                createdAt: '2024-01-05T00:00:00Z',
                qualityTier: {
                    id: 't5-5',
                    name: 'WEB-360p',
                    criteria: { source: ['WEB'], resolution: ['360p'] },
                    createdAt: '2024-01-05T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-5',
                qualityTierId: 't5-6',
                ordinal: 5,
                isCutoff: false,
                createdAt: '2024-01-05T00:00:00Z',
                qualityTier: {
                    id: 't5-6',
                    name: 'WEB-Other',
                    criteria: { source: ['WEB'] },
                    createdAt: '2024-01-05T00:00:00Z',
                },
            },
        ],
    },
];

const mockTags: ApiTag[] = [{ name: 'Movies' }, { name: 'TV Shows' }, { name: 'Anime' }];

function createTestQueryClient() {
    return new QueryClient({
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
}

function TestWrapper({
    children,
    role = Role.Admin,
    initialEntries = ['/quality-tiers'],
}: {
    children: ReactNode;
    role?: Role;
    initialEntries?: string[];
}) {
    const mockUser = { id: 'user-1', username: 'test', role };
    const queryClient = createTestQueryClient();

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

async function renderAndWaitForData(role: Role = Role.Admin, initialEntries: string[] = ['/quality-tiers']) {
    render(
        <TestWrapper role={role} initialEntries={initialEntries}>
            <QualityTiersPage />
        </TestWrapper>,
    );

    await waitFor(() => {
        expect(document.querySelector('[data-tier-list-id]')).toBeInTheDocument();
    });
}

describe('QualityTiersPage', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                const tagMatch = url.match(/[?&]tag=([^&]+)/);
                if (tagMatch) {
                    return Promise.resolve([]);
                }
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
        mockApiPost.mockResolvedValue(mockApiTierLists[0]);
        mockApiDelete.mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', async () => {
        await renderAndWaitForData();

        expect(document.querySelector('[data-page="quality-tiers"]')).toBeInTheDocument();
    });

    it('renders the page title', async () => {
        await renderAndWaitForData();

        expect(screen.getByRole('heading', { name: 'Quality Tiers', level: 1 })).toBeInTheDocument();
    });

    it('renders the tier list count', async () => {
        await renderAndWaitForData();

        const count = screen.getByTestId('quality-tiers-page__count');
        expect(count).toBeInTheDocument();
        expect(count).toHaveTextContent('5');
    });

    it('renders upgrade summary badges', async () => {
        await renderAndWaitForData();

        const summary = screen.getByLabelText('Upgrade summary');
        expect(summary).toBeInTheDocument();
        expect(summary).toHaveClass('list-toolbar__summary');
    });

    it('renders the page icon as decorative', async () => {
        await renderAndWaitForData();

        const icon = screen.getByTestId('quality-tiers-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('QualityTiersPage list items', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders tier list items in the list', async () => {
        await renderAndWaitForData();

        const list = screen.getByRole('list', { name: /quality tier lists/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(5);
    });

    it('renders tier list names', async () => {
        await renderAndWaitForData();

        expect(screen.getByText('HD Standard')).toBeInTheDocument();
        expect(screen.getByText('4K Ultra HD')).toBeInTheDocument();
        expect(screen.getByText('Any Quality')).toBeInTheDocument();
        expect(screen.getByText('SD Only')).toBeInTheDocument();
        expect(screen.getByText('WEB Preferred')).toBeInTheDocument();
    });

    it('renders tier counts', async () => {
        await renderAndWaitForData();

        expect(screen.getByText('4 tiers')).toBeInTheDocument();

        expect(screen.getByText('3 tiers')).toBeInTheDocument();

        expect(screen.getByText('1 tier')).toBeInTheDocument();

        expect(screen.getByText('2 tiers')).toBeInTheDocument();

        expect(screen.getByText('6 tiers')).toBeInTheDocument();
    });

    it('renders upgrade allowed badges', async () => {
        await renderAndWaitForData();

        // 3 card badges + 1 summary badge = 4
        const upgradeBadges = screen.getAllByText('Upgrade');
        expect(upgradeBadges.length).toBe(4);

        // 2 card badges + 1 summary badge = 3
        const noUpgradeBadges = screen.getAllByText('No Upgrade');
        expect(noUpgradeBadges.length).toBe(3);
    });

    it('applies data-tier-list-id to items', async () => {
        await renderAndWaitForData();

        const items = document.querySelectorAll('[data-tier-list-id]');
        expect(items.length).toBe(5);
        expect(items[0]).toHaveAttribute('data-tier-list-id', 'qtl-1');
    });

    it('renders tier preview chips', async () => {
        await renderAndWaitForData();

        expect(screen.getAllByText('Bluray-1080p').length).toBe(2);

        expect(screen.getByText('HDTV-1080p')).toBeInTheDocument();

        expect(screen.getAllByText('WEB-1080p').length).toBe(2);

        expect(screen.getAllByText('Bluray-2160p').length).toBe(2);

        expect(screen.getAllByText('WEB-2160p').length).toBe(3);
    });

    it('highlights cutoff tiers in preview', async () => {
        await renderAndWaitForData();

        const cutoffChips = screen.getAllByTestId('quality-tiers-page__tier-chip--cutoff');

        expect(cutoffChips.length).toBe(5);
    });

    it('shows cutoff tier name in metadata', async () => {
        await renderAndWaitForData();

        const cutoffLabels = screen.getAllByTestId('quality-tiers-page__item-cutoff');
        expect(cutoffLabels.length).toBe(5);
    });
});

describe('QualityTiersPage Add Tier List button', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows Add Tier List button for Manager role', async () => {
        await renderAndWaitForData(Role.Manager);

        expect(screen.getAllByRole('button', { name: /add tier list/i }).length).toBeGreaterThanOrEqual(1);
    });

    it('shows Add Tier List button for Admin role', async () => {
        await renderAndWaitForData(Role.Admin);

        expect(screen.getAllByRole('button', { name: /add tier list/i }).length).toBeGreaterThanOrEqual(1);
    });

    it('hides Add Tier List button for Viewer role', async () => {
        await renderAndWaitForData(Role.Viewer);

        expect(screen.queryByRole('button', { name: /add tier list/i })).not.toBeInTheDocument();
    });

    it('hides Add Tier List button for Requester role', async () => {
        await renderAndWaitForData(Role.Requester);

        expect(screen.queryByRole('button', { name: /add tier list/i })).not.toBeInTheDocument();
    });

    it('add button has data-action attribute', async () => {
        await renderAndWaitForData(Role.Manager);

        const addButtons = screen.getAllByRole('button', { name: /add tier list/i });
        expect(addButtons[0]).toHaveAttribute('data-action', 'add-tier-list');
    });
});

describe('QualityTiersPage management actions', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('Manager does not see edit buttons on list page', async () => {
        await renderAndWaitForData(Role.Manager);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(0);
    });

    it('Manager can see delete buttons', async () => {
        await renderAndWaitForData(Role.Manager);

        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        expect(deleteButtons.length).toBe(5);
    });

    it('Manager can see clone buttons', async () => {
        await renderAndWaitForData(Role.Manager);

        const cloneButtons = document.querySelectorAll('[data-action="clone"]');
        expect(cloneButtons.length).toBe(5);
    });

    it('Viewer cannot see management buttons', async () => {
        await renderAndWaitForData(Role.Viewer);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        const cloneButtons = document.querySelectorAll('[data-action="clone"]');

        expect(editButtons.length).toBe(0);
        expect(deleteButtons.length).toBe(0);
        expect(cloneButtons.length).toBe(0);
    });

    it('Requester cannot see management buttons', async () => {
        await renderAndWaitForData(Role.Requester);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        const cloneButtons = document.querySelectorAll('[data-action="clone"]');

        expect(editButtons.length).toBe(0);
        expect(deleteButtons.length).toBe(0);
        expect(cloneButtons.length).toBe(0);
    });
});

describe('QualityTiersPage delete confirmation', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
        mockApiDelete.mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('opens confirmation dialog when delete is clicked', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector(
            '[data-tier-list-id="qtl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Quality Tier List')).toBeInTheDocument();
        });

        expect(screen.getByText(/Are you sure you want to delete "HD Standard"\?/)).toBeInTheDocument();
    });

    it('does not delete immediately when delete is clicked', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector(
            '[data-tier-list-id="qtl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Quality Tier List')).toBeInTheDocument();
        });

        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('deletes tier list after confirmation', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector(
            '[data-tier-list-id="qtl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Quality Tier List')).toBeInTheDocument();
        });

        const confirmDeleteButton = document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement;
        await user.click(confirmDeleteButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalled();
        });
    });

    it('closes confirmation dialog on cancel', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector(
            '[data-tier-list-id="qtl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Quality Tier List')).toBeInTheDocument();
        });

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLButtonElement;
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete Quality Tier List')).not.toBeInTheDocument();
        });
    });
});

describe('QualityTiersPage clone action', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
        mockApiPost.mockResolvedValue(mockApiTierLists[0]);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('clone button has correct aria-label', async () => {
        await renderAndWaitForData(Role.Manager);

        const cloneButton = document.querySelector('[data-action="clone"]');
        expect(cloneButton).toHaveAttribute('aria-label');
    });

    it('shows spinner when cloning', async () => {
        mockApiPost.mockImplementation(
            () => new Promise((resolve) => setTimeout(() => resolve(mockApiTierLists[0]), 200)),
        );

        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const cloneButton = document.querySelector('[data-action="clone"]') as HTMLButtonElement;
        await user.click(cloneButton);

        await waitFor(() => {
            expect(cloneButton).toBeDisabled();
            expect(cloneButton.querySelector('.card-actions__spinner')).toBeInTheDocument();
        });
    });

    it('shows toast notification after clone completes', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const cloneButton = document.querySelector('[data-action="clone"]') as HTMLButtonElement;
        await user.click(cloneButton);

        await waitFor(
            () => {
                expect(screen.getByText('Cloned')).toBeInTheDocument();
                expect(screen.getByText(/created copy of/i)).toBeInTheDocument();
            },
            { timeout: 3000 },
        );
    });
});

describe('QualityTiersPage accessibility', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('tier lists list has accessible label', async () => {
        await renderAndWaitForData();

        expect(screen.getByRole('list', { name: /quality tier lists/i })).toBeInTheDocument();
    });

    it('action buttons have aria-labels', async () => {
        await renderAndWaitForData(Role.Manager);

        const cloneButtons = document.querySelectorAll('[data-action="clone"]');
        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');

        cloneButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
        editButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
        deleteButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });

    it('tier chips have data-tier-id attributes', async () => {
        await renderAndWaitForData();

        const tierChips = document.querySelectorAll('[data-tier-id]');

        expect(tierChips.length).toBe(16);
    });

    it('cutoff tier chips have data-cutoff attribute', async () => {
        await renderAndWaitForData();

        const cutoffChips = document.querySelectorAll('[data-cutoff="true"]');
        expect(cutoffChips.length).toBe(5);
    });
});

describe('QualityTiersPage empty state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('empty state is not shown when there are tier lists', async () => {
        await renderAndWaitForData();

        expect(screen.queryByText('No quality tier lists configured')).not.toBeInTheDocument();
    });
});

describe('QualityTiersPage tag filtering', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve(mockTags);
            }
            if (url.startsWith('/quality-tier-lists?tag=Movies')) {
                return Promise.resolve([mockApiTierLists[1]]);
            }
            if (url.startsWith('/quality-tier-lists?tag=Anime')) {
                return Promise.resolve([]);
            }
            if (url.startsWith('/quality-tier-lists?tag=')) {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve(mockApiTierLists);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows all tier lists when no tag query param', async () => {
        await renderAndWaitForData();

        const items = document.querySelectorAll('[data-tier-list-id]');
        expect(items.length).toBe(5);
    });

    it('renders tag filter dropdown', async () => {
        await renderAndWaitForData();

        const tagFilter = document.querySelector('[data-filter="tag"]');
        expect(tagFilter).toBeInTheDocument();
    });

    it('shows All Tags when no filter selected', async () => {
        await renderAndWaitForData();

        expect(screen.getByText('All Tags')).toBeInTheDocument();
    });
});

describe('QualityTiersPage error state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows error state when API fails', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Failed to load quality tier lists')).toBeInTheDocument();
        });
    });

    it('shows error message from API', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.reject(new Error('Server unavailable'));
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Something went wrong. Please try again.')).toBeInTheDocument();
        });
    });

    it('shows retry button in error state', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
        });
    });

    it('still shows page title in error state', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/tags' || url.startsWith('/tags?')) {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists' || url.startsWith('/quality-tier-lists?')) {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Quality Tiers', level: 1 })).toBeInTheDocument();
        });
    });
});

describe('QualityTiersPage loading state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows loading spinner while data is fetching', () => {
        mockApiGet.mockImplementation(() => new Promise(() => {}));

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        expect(screen.getByTestId('quality-tiers-page__loading')).toBeInTheDocument();
        expect(screen.getByText('Loading quality tier lists...')).toBeInTheDocument();
    });

    it('still shows page title while loading', () => {
        mockApiGet.mockImplementation(() => new Promise(() => {}));

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'Quality Tiers', level: 1 })).toBeInTheDocument();
    });

    it('does not show the tier list content while loading', () => {
        mockApiGet.mockImplementation(() => new Promise(() => {}));

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-tier-list-id]')).not.toBeInTheDocument();
    });
});
