import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';

import { TierListDetailPage } from './TierListDetailPage';

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

import { apiGet, apiPatch } from '@/lib/api';
import type { ApiQualityTierList } from '@/hooks/useQualityTierLists';

const mockApiTierLists: Record<string, ApiQualityTierList> = {
    'qtl-1': {
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'Bluray' },
                                    resolution: { operator: 0, value: '1080p' },
                                    codec: { operator: 0, value: 'x264' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'HDTV' },
                                    resolution: { operator: 0, value: '1080p' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'WEB' },
                                    resolution: { operator: 0, value: '1080p' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'HDTV' },
                                    resolution: { operator: 0, value: '720p' },
                                },
                            },
                        ],
                    },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            },
        ],
    },
    'qtl-2': {
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'Bluray' },
                                    resolution: { operator: 0, value: '2160p' },
                                    codec: { operator: 0, value: 'x265' },
                                    hdr: { operator: 0, value: 'true' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'WEB' },
                                    resolution: { operator: 0, value: '2160p' },
                                    hdr: { operator: 0, value: 'true' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'HDTV' },
                                    resolution: { operator: 0, value: '2160p' },
                                },
                            },
                        ],
                    },
                    createdAt: '2024-01-02T00:00:00Z',
                },
            },
        ],
    },
    'qtl-3': {
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
                    criteria: { and: [{ items: { source: { operator: 0, value: '*' } } }] },
                    createdAt: '2024-01-03T00:00:00Z',
                },
            },
        ],
    },
    'qtl-5': {
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'WEB' },
                                    resolution: { operator: 0, value: '2160p' },
                                    hdr: { operator: 0, value: 'true' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'WEB' },
                                    resolution: { operator: 0, value: '1080p' },
                                    codec: { operator: 0, value: 'x264' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'WEB' },
                                    resolution: { operator: 0, value: '720p' },
                                    codec: { operator: 0, value: 'x264' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'WEB' },
                                    resolution: { operator: 0, value: '480p' },
                                },
                            },
                        ],
                    },
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
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'WEB' },
                                    resolution: { operator: 0, value: '360p' },
                                },
                            },
                        ],
                    },
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
                    criteria: { and: [{ items: { source: { operator: 0, value: 'WEB' } } }] },
                    createdAt: '2024-01-05T00:00:00Z',
                },
            },
        ],
    },
};

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
    initialPath = '/quality-tiers/qtl-1',
}: {
    children: ReactNode;
    role?: Role;
    initialPath?: string;
}) {
    const mockUser = { id: 'user-1', username: 'test', role };
    const queryClient = createTestQueryClient();

    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[initialPath]}>
                <AuthProvider initialUser={mockUser}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <Routes>
                                    <Route path="/quality-tiers/:tierListId" element={children} />
                                </Routes>
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

function setupMockApi() {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            const detailMatch = url.match(/^\/quality-tier-lists\/([^?]+)$/);
            if (detailMatch) {
                const tierListId = detailMatch[1];
                const tierList = mockApiTierLists[tierListId];
                if (tierList) {
                    return Promise.resolve(tierList);
                }
                return Promise.reject(new Error('Not found'));
            }
            if (url.startsWith('/quality-tier-lists')) {
                return Promise.resolve(Object.values(mockApiTierLists));
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
        mockApiPatch.mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    return { mockApiGet, mockApiPatch };
}

async function renderAndWaitForData(role: Role = Role.Admin, initialPath: string = '/quality-tiers/qtl-1') {
    render(
        <TestWrapper role={role} initialPath={initialPath}>
            <TierListDetailPage />
        </TestWrapper>,
    );

    if (initialPath === '/quality-tiers/invalid-id') {
        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Failed to Load Tier List' })).toBeInTheDocument();
        });
    } else {
        await waitFor(() => {
            expect(document.querySelector('[data-tier-id]')).toBeInTheDocument();
        });
    }
}

describe('TierListDetailPage', () => {
    setupMockApi();

    it('renders with correct data-page attribute', async () => {
        render(
            <TestWrapper>
                <TierListDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-page="tier-list-detail"]')).toBeInTheDocument();
        });
    });

    it('renders the tier list title', async () => {
        await renderAndWaitForData();

        expect(screen.getByRole('heading', { name: 'HD Standard', level: 1 })).toBeInTheDocument();
    });

    it('renders upgrade allowed badge', async () => {
        await renderAndWaitForData();

        expect(screen.getByText('Upgrade')).toBeInTheDocument();
    });

    it('renders no upgrade badge for tier list without upgrades', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/qtl-3');

        expect(screen.getByText('No Upgrade')).toBeInTheDocument();
    });

    it('renders page description', async () => {
        await renderAndWaitForData();

        expect(screen.getByText(/tiers are listed in priority order/i)).toBeInTheDocument();
    });

    it('renders breadcrumb with tier list name', async () => {
        await renderAndWaitForData();

        expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();

        expect(screen.getAllByRole('link', { name: 'Quality Tiers' }).length).toBeGreaterThan(0);
    });
});

describe('TierListDetailPage tiers list', () => {
    setupMockApi();

    it('renders tiers in the list', async () => {
        await renderAndWaitForData();

        const list = screen.getByRole('list', { name: /quality tiers/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(4);
    });

    it('renders tier names', async () => {
        await renderAndWaitForData();

        expect(screen.getByText('Bluray-1080p')).toBeInTheDocument();
        expect(screen.getByText('HDTV-1080p')).toBeInTheDocument();
        expect(screen.getByText('WEB-1080p')).toBeInTheDocument();
        expect(screen.getByText('HDTV-720p')).toBeInTheDocument();
    });

    it('renders priority numbers', async () => {
        await renderAndWaitForData();

        expect(screen.getByText('1')).toBeInTheDocument();
        expect(screen.getByText('2')).toBeInTheDocument();
        expect(screen.getByText('3')).toBeInTheDocument();
        expect(screen.getByText('4')).toBeInTheDocument();
    });

    it('renders criteria counts', async () => {
        await renderAndWaitForData();

        const twoCriteria = screen.getAllByText('2 criteria');
        expect(twoCriteria.length).toBeGreaterThan(0);

        expect(screen.getByText('3 criteria')).toBeInTheDocument();
    });

    it('applies data-tier-id to items', async () => {
        await renderAndWaitForData();

        const items = document.querySelectorAll('[data-tier-id]');
        expect(items.length).toBe(4);
        expect(items[0]).toHaveAttribute('data-tier-id', 't1-1');
    });

    it('highlights cutoff tier', async () => {
        await renderAndWaitForData();

        const cutoffBadge = screen.getByText('Cutoff');
        expect(cutoffBadge).toBeInTheDocument();

        const cutoffTier = document.querySelector('[data-cutoff="true"]');
        expect(cutoffTier).toBeInTheDocument();
        expect(cutoffTier).toHaveAttribute('data-tier-id', 't1-1');
    });
});

describe('TierListDetailPage criteria display', () => {
    setupMockApi();

    it('renders criteria for tiers', async () => {
        await renderAndWaitForData();

        const criterionElements = document.querySelectorAll('[data-criterion-id]');
        expect(criterionElements.length).toBeGreaterThan(0);
    });

    it('displays criterion field names', async () => {
        await renderAndWaitForData();

        const sourceFields = screen.getAllByText('source');
        expect(sourceFields.length).toBeGreaterThan(0);

        const resolutionFields = screen.getAllByText('resolution');
        expect(resolutionFields.length).toBeGreaterThan(0);
    });

    it('displays criterion operators', async () => {
        await renderAndWaitForData();

        const equalsOperators = screen.getAllByText('=');
        expect(equalsOperators.length).toBeGreaterThan(0);
    });

    it('displays criterion values', async () => {
        await renderAndWaitForData();

        expect(screen.getAllByText('Bluray').length).toBeGreaterThan(0);
        expect(screen.getAllByText('1080p').length).toBeGreaterThan(0);
    });
});

describe('TierListDetailPage Add Tier button', () => {
    setupMockApi();

    it('shows Add Tier button for Manager role', async () => {
        await renderAndWaitForData(Role.Manager);

        const addButtons = screen.getAllByRole('button', { name: /add tier/i });
        expect(addButtons.length).toBeGreaterThanOrEqual(1);
    });

    it('shows Add Tier button for Admin role', async () => {
        await renderAndWaitForData(Role.Admin);

        const addButtons = screen.getAllByRole('button', { name: /add tier/i });
        expect(addButtons.length).toBeGreaterThanOrEqual(1);
    });

    it('hides Add Tier button for Viewer role', async () => {
        await renderAndWaitForData(Role.Viewer);

        expect(screen.queryByRole('button', { name: /add tier/i })).not.toBeInTheDocument();
    });

    it('hides Add Tier button for Requester role', async () => {
        await renderAndWaitForData(Role.Requester);

        expect(screen.queryByRole('button', { name: /add tier/i })).not.toBeInTheDocument();
    });

    it('add button has data-action attribute', async () => {
        await renderAndWaitForData(Role.Manager);

        const addButton = document.querySelector('[data-action="add-tier"]');
        expect(addButton).toBeInTheDocument();
    });
});

describe('TierListDetailPage management actions', () => {
    setupMockApi();

    it('Manager can see edit buttons', async () => {
        await renderAndWaitForData(Role.Manager);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(4);
    });

    it('Manager can see delete buttons', async () => {
        await renderAndWaitForData(Role.Manager);

        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        expect(deleteButtons.length).toBe(4);
    });

    it('Manager can see set-cutoff buttons for non-cutoff tiers', async () => {
        await renderAndWaitForData(Role.Manager);

        const setCutoffButtons = document.querySelectorAll('[data-action="set-cutoff"]');

        expect(setCutoffButtons.length).toBe(3);
    });

    it('Viewer cannot see management buttons', async () => {
        await renderAndWaitForData(Role.Viewer);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        const setCutoffButtons = document.querySelectorAll('[data-action="set-cutoff"]');

        expect(editButtons.length).toBe(0);
        expect(deleteButtons.length).toBe(0);
        expect(setCutoffButtons.length).toBe(0);
    });

    it('Requester cannot see management buttons', async () => {
        await renderAndWaitForData(Role.Requester);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        const setCutoffButtons = document.querySelectorAll('[data-action="set-cutoff"]');

        expect(editButtons.length).toBe(0);
        expect(deleteButtons.length).toBe(0);
        expect(setCutoffButtons.length).toBe(0);
    });
});

describe('TierListDetailPage drag-and-drop reordering', () => {
    setupMockApi();

    it('tiers are draggable for Manager role', async () => {
        await renderAndWaitForData(Role.Manager);

        const tiers = document.querySelectorAll('[data-tier-id]');
        tiers.forEach((tier) => {
            expect(tier).toHaveAttribute('draggable', 'true');
        });
    });

    it('tiers are not draggable for Viewer role', async () => {
        await renderAndWaitForData(Role.Viewer);

        const tiers = document.querySelectorAll('[data-tier-id]');
        tiers.forEach((tier) => {
            expect(tier).not.toHaveAttribute('draggable', 'true');
        });
    });

    it('tiers are not draggable for Requester role', async () => {
        await renderAndWaitForData(Role.Requester);

        const tiers = document.querySelectorAll('[data-tier-id]');
        tiers.forEach((tier) => {
            expect(tier).not.toHaveAttribute('draggable', 'true');
        });
    });

    it('Manager sees drag handle icons', async () => {
        await renderAndWaitForData(Role.Manager);

        const dragHandles = screen.queryAllByTestId('tier-list-detail-page__drag-handle');
        expect(dragHandles.length).toBe(4);
    });

    it('Viewer does not see drag handle icons', async () => {
        await renderAndWaitForData(Role.Viewer);

        const dragHandles = screen.queryAllByTestId('tier-list-detail-page__drag-handle');
        expect(dragHandles.length).toBe(0);
    });

    it('shows drag reorder hint in description for Manager role', async () => {
        await renderAndWaitForData(Role.Manager);

        expect(screen.getByText(/drag tiers to reorder/i)).toBeInTheDocument();
    });

    it('does not show drag reorder hint for Viewer role', async () => {
        await renderAndWaitForData(Role.Viewer);

        expect(screen.queryByText(/drag tiers to reorder/i)).not.toBeInTheDocument();
    });
});

describe('TierListDetailPage delete confirmation', () => {
    setupMockApi();

    it('delete button has correct aria-label', async () => {
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector('[data-action="delete"]');
        expect(deleteButton).toHaveAttribute('aria-label');
    });

    it('opens confirmation dialog when delete is clicked', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector('[data-action="delete"]') as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Tier')).toBeInTheDocument();
        });

        expect(screen.getByText(/Are you sure you want to delete "Bluray-1080p"\?/)).toBeInTheDocument();
    });

    it('does not delete immediately when delete is clicked', async () => {
        const mockApiPatchLocal = vi.mocked(apiPatch);
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector('[data-action="delete"]') as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Tier')).toBeInTheDocument();
        });

        expect(mockApiPatchLocal).not.toHaveBeenCalled();
    });

    it('deletes tier after confirmation', async () => {
        const mockApiPatchLocal = vi.mocked(apiPatch);
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector('[data-action="delete"]') as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Tier')).toBeInTheDocument();
        });

        const confirmDeleteButton = document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement;
        await user.click(confirmDeleteButton);

        await waitFor(() => {
            expect(mockApiPatchLocal).toHaveBeenCalled();
        });
    });

    it('closes confirmation dialog on cancel', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector('[data-action="delete"]') as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Tier')).toBeInTheDocument();
        });

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLButtonElement;
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete Tier')).not.toBeInTheDocument();
        });
    });

    it('shows toast notification after delete completes', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const deleteButton = document.querySelector('[data-action="delete"]') as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Tier')).toBeInTheDocument();
        });

        const confirmDeleteButton = document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement;
        await user.click(confirmDeleteButton);

        await waitFor(
            () => {
                expect(screen.getByText('Deleted')).toBeInTheDocument();
                expect(screen.getByText(/removed tier/i)).toBeInTheDocument();
            },
            { timeout: 3000 },
        );
    });
});

describe('TierListDetailPage not found state', () => {
    setupMockApi();

    it('shows not found message for invalid tier list ID', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/invalid-id');

        expect(screen.getByRole('heading', { name: 'Failed to Load Tier List' })).toBeInTheDocument();
        expect(screen.getByText('Not found')).toBeInTheDocument();
    });

    it('shows back link in not found state', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/invalid-id');

        expect(screen.getByRole('link', { name: /back to quality tiers/i })).toBeInTheDocument();
    });
});

describe('TierListDetailPage accessibility', () => {
    setupMockApi();

    it('tiers list has accessible label', async () => {
        await renderAndWaitForData();

        expect(screen.getByRole('list', { name: /quality tiers/i })).toBeInTheDocument();
    });

    it('action buttons have aria-labels', async () => {
        await renderAndWaitForData(Role.Manager);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        const setCutoffButtons = document.querySelectorAll('[data-action="set-cutoff"]');

        editButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
        deleteButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
        setCutoffButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });

    it('tiers have data-tier-id attributes', async () => {
        await renderAndWaitForData();

        const tiers = document.querySelectorAll('[data-tier-id]');
        expect(tiers.length).toBe(4);
    });

    it('cutoff tier has data-cutoff attribute', async () => {
        await renderAndWaitForData();

        const cutoffTier = document.querySelector('[data-cutoff="true"]');
        expect(cutoffTier).toBeInTheDocument();
    });

    it('criteria have data-criterion-id attributes', async () => {
        await renderAndWaitForData();

        const criteria = document.querySelectorAll('[data-criterion-id]');

        expect(criteria.length).toBe(9);
    });
});

describe('TierListDetailPage different tier lists', () => {
    setupMockApi();

    it('renders 4K Ultra HD tier list correctly', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/qtl-2');

        expect(screen.getByRole('heading', { name: '4K Ultra HD', level: 1 })).toBeInTheDocument();
        expect(screen.getByText('Bluray-2160p')).toBeInTheDocument();
        expect(screen.getByText('WEB-2160p')).toBeInTheDocument();
        expect(screen.getByText('HDTV-2160p')).toBeInTheDocument();
    });

    it('renders Any Quality tier list correctly', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/qtl-3');

        expect(screen.getByRole('heading', { name: 'Any Quality', level: 1 })).toBeInTheDocument();
        expect(screen.getByText('Any')).toBeInTheDocument();
        expect(screen.getByText('1 criterion')).toBeInTheDocument();
    });

    it('renders WEB Preferred tier list with many tiers', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/qtl-5');

        expect(screen.getByRole('heading', { name: 'WEB Preferred', level: 1 })).toBeInTheDocument();

        const list = screen.getByRole('list', { name: /quality tiers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(6);
    });
});

describe('TierListDetailPage error state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url.match(/^\/quality-tier-lists\/[^?]+$/)) {
                return Promise.reject(new Error('Connection refused'));
            }
            if (url.startsWith('/quality-tier-lists')) {
                return Promise.resolve([]);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows error state when API fails', async () => {
        render(
            <TestWrapper>
                <TierListDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Failed to Load Tier List' })).toBeInTheDocument();
        });
    });

    it('shows error message from API', async () => {
        render(
            <TestWrapper>
                <TierListDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Connection refused')).toBeInTheDocument();
        });
    });

    it('shows retry button in error state', async () => {
        render(
            <TestWrapper>
                <TierListDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
        });
    });

    it('shows back link in error state', async () => {
        render(
            <TestWrapper>
                <TierListDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('link', { name: /back to quality tiers/i })).toBeInTheDocument();
        });
    });
});
