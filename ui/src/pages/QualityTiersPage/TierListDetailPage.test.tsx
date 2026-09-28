import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { LiveAnnouncerProvider } from '@/components/ui/LiveAnnouncer';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';

import { TierListDetailPage } from './TierListDetailPage';
import type { Criterion } from './CriteriaEditor';
import { formatOperator, mapCriteriaToApi, mapCriteriaToDisplay } from './tierListDetailMappers';

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

import { ApiError, apiDelete, apiGet, apiPatch, apiPost } from '@/lib/api';
import type { ApiQualityTierList } from '@/hooks/useQualityTierLists';

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const MISSING_DETAIL_ID = '00000000-0000-0000-0000-000000000000';

const mockApiTierLists: Record<string, ApiQualityTierList> = {
    'qtl-1': {
        id: 'qtl-1',
        name: 'HD Standard',
        mimeTypes: ['video/*'],
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
                                    source: { operator: 'Equals', value: 'Bluray' },
                                    resolution: { operator: 'Equals', value: '1080p' },
                                    codec: { operator: 'Equals', value: 'x264' },
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
                                    source: { operator: 'Equals', value: 'HDTV' },
                                    resolution: { operator: 'Equals', value: '1080p' },
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
                                    source: { operator: 'Equals', value: 'WEB' },
                                    resolution: { operator: 'Equals', value: '1080p' },
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
                                    source: { operator: 'Equals', value: 'HDTV' },
                                    resolution: { operator: 'Equals', value: '720p' },
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
        mimeTypes: ['video/*'],
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
                                    source: { operator: 'Equals', value: 'Bluray' },
                                    resolution: { operator: 'Equals', value: '2160p' },
                                    codec: { operator: 'Equals', value: 'x265' },
                                    hdr: { operator: 'Equals', value: 'true' },
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
                                    source: { operator: 'Equals', value: 'WEB' },
                                    resolution: { operator: 'Equals', value: '2160p' },
                                    hdr: { operator: 'Equals', value: 'true' },
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
                                    source: { operator: 'Equals', value: 'HDTV' },
                                    resolution: { operator: 'Equals', value: '2160p' },
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
        mimeTypes: [],
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
                    criteria: { and: [{ items: { source: { operator: 'Equals', value: '*' } } }] },
                    createdAt: '2024-01-03T00:00:00Z',
                },
            },
        ],
    },
    'qtl-5': {
        id: 'qtl-5',
        name: 'WEB Preferred',
        mimeTypes: ['video/*'],
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
                                    source: { operator: 'Equals', value: 'WEB' },
                                    resolution: { operator: 'Equals', value: '2160p' },
                                    hdr: { operator: 'Equals', value: 'true' },
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
                                    source: { operator: 'Equals', value: 'WEB' },
                                    resolution: { operator: 'Equals', value: '1080p' },
                                    codec: { operator: 'Equals', value: 'x264' },
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
                                    source: { operator: 'Equals', value: 'WEB' },
                                    resolution: { operator: 'Equals', value: '720p' },
                                    codec: { operator: 'Equals', value: 'x264' },
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
                                    source: { operator: 'Equals', value: 'WEB' },
                                    resolution: { operator: 'Equals', value: '480p' },
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
                                    source: { operator: 'Equals', value: 'WEB' },
                                    resolution: { operator: 'Equals', value: '360p' },
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
                    criteria: { and: [{ items: { source: { operator: 'Equals', value: 'WEB' } } }] },
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
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <Routes>
                                    <Route path="/quality-tiers/:tierListId" element={children} />
                                    <Route path="/admin/quality-tiers/:tierListId" element={children} />
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
    const mockApiPost = vi.mocked(apiPost);

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
                return Promise.reject(new ApiError(404, 'Not found'));
            }
            if (url.startsWith('/quality-tier-lists')) {
                return Promise.resolve(Object.values(mockApiTierLists));
            }
            if (url === '/content-types') {
                return Promise.resolve([]);
            }
            if (url === '/subject-types') {
                return Promise.resolve([]);
            }
            return Promise.reject(new Error('Unknown endpoint'));
        });
        mockApiPatch.mockResolvedValue(undefined);
        mockApiPost.mockResolvedValue({
            id: '11111111-1111-4111-8111-111111111111',
            name: 'New Tier',
            criteria: { and: [] },
            rules: [],
            createdAt: '2024-01-06T00:00:00Z',
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    return { mockApiGet, mockApiPatch };
}

async function renderAndWaitForData(role: Role = Role.Admin, initialPath: string = '/quality-tiers/qtl-1') {
    render(
        <LiveAnnouncerProvider>
            <TestWrapper role={role} initialPath={initialPath}>
                <TierListDetailPage />
            </TestWrapper>
        </LiveAnnouncerProvider>,
    );

    if (initialPath === '/quality-tiers/invalid-id' || initialPath.includes(MISSING_DETAIL_ID)) {
        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Tier List Not Found' })).toBeInTheDocument();
        });
    } else {
        await waitFor(() => {
            expect(document.querySelector('[data-tier-id]')).toBeInTheDocument();
        });
    }
}

function makeCriterion(overrides: Partial<Criterion> = {}): Criterion {
    return { id: 'c-1', field: 'source', operator: 'Equals', value: 'Bluray', ...overrides };
}

describe('TierListDetailPage criteria operator mapping', () => {
    it('maps numeric operators to their API wire names', () => {
        const cases: { ui: Criterion['operator']; wire: string }[] = [
            { ui: 'GreaterThan', wire: 'GreaterThan' },
            { ui: 'GreaterThanOrEqual', wire: 'GreaterThanOrEqual' },
            { ui: 'LessThan', wire: 'LessThan' },
            { ui: 'LessThanOrEqual', wire: 'LessThanOrEqual' },
        ];

        for (const { ui, wire } of cases) {
            const api = mapCriteriaToApi([makeCriterion({ field: 'size', operator: ui, value: '100' })]);
            expect(api.and?.[0].items?.size.operator).toBe(wire);
        }
    });

    it('round-trips a size criterion with a numeric operator through API and display', () => {
        const api = mapCriteriaToApi([makeCriterion({ field: 'size', operator: 'GreaterThanOrEqual', value: '2000' })]);
        expect(api.and?.[0].items?.size).toEqual({ operator: 'GreaterThanOrEqual', value: '2000' });

        const display = mapCriteriaToDisplay(api, 'tier-x');
        expect(display).toHaveLength(1);
        expect(display[0]).toMatchObject({ field: 'size', operator: 'GreaterThanOrEqual', value: '2000' });
    });

    it('round-trips an age criterion with LessThan', () => {
        const api = mapCriteriaToApi([makeCriterion({ field: 'age', operator: 'LessThan', value: '30' })]);
        const display = mapCriteriaToDisplay(api, 'tier-x');
        expect(display[0]).toMatchObject({ field: 'age', operator: 'LessThan', value: '30' });
    });

    it('maps a language criterion with Contains to the substring wire operator', () => {
        const api = mapCriteriaToApi([makeCriterion({ field: 'language', operator: 'Contains', value: 'English' })]);
        expect(api.and?.[0].items?.language.operator).toBe('ContainsString');
        const display = mapCriteriaToDisplay(api, 'tier-x');
        expect(display[0]).toMatchObject({ field: 'language', operator: 'Contains', value: 'English' });
    });

    it('emits repeated fields as separate API criteria', () => {
        const api = mapCriteriaToApi([
            makeCriterion({ id: 'c-1', field: 'source', value: 'Bluray' }),
            makeCriterion({ id: 'c-2', field: 'resolution', value: '1080p' }),
            makeCriterion({ id: 'c-3', field: 'source', value: 'WEB' }),
        ]);

        expect(api.and).toEqual([
            {
                items: {
                    source: { operator: 'Equals', value: 'Bluray' },
                    resolution: { operator: 'Equals', value: '1080p' },
                },
            },
            { items: { source: { operator: 'Equals', value: 'WEB' } } },
        ]);

        const display = mapCriteriaToDisplay(api, 'tier-x');
        expect(display).toEqual([
            expect.objectContaining({ field: 'source', operator: 'Equals', value: 'Bluray' }),
            expect.objectContaining({ field: 'resolution', operator: 'Equals', value: '1080p' }),
            expect.objectContaining({ field: 'source', operator: 'Equals', value: 'WEB' }),
        ]);
    });

    it('formats numeric operators as comparison symbols', () => {
        expect(formatOperator('GreaterThan')).toBe('>');
        expect(formatOperator('GreaterThanOrEqual')).toBe('≥');
        expect(formatOperator('LessThan')).toBe('<');
        expect(formatOperator('LessThanOrEqual')).toBe('≤');
    });

    it('formats wire string and null operators as readable labels', () => {
        expect(formatOperator('ContainsString')).toBe('contains');
        expect(formatOperator('NotStartsWithString')).toBe('not starts with');
        expect(formatOperator('IsNull')).toBe('is null');
        expect(formatOperator('IsNotNull')).toBe('is not null');
    });
});

describe('TierListDetailPage', () => {
    const { mockApiPatch } = setupMockApi();

    it('renders with correct data-page attribute', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestWrapper>
                    <TierListDetailPage />
                </TestWrapper>
            </LiveAnnouncerProvider>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-page="tier-list-detail"]')).toBeInTheDocument();
        });
    });

    it('renders the tier list title', async () => {
        await renderAndWaitForData();

        expect(screen.getByRole('heading', { name: 'HD Standard', level: 1 })).toBeInTheDocument();
    });

    it('does not render an upgrade-allowed badge in the header', async () => {
        await renderAndWaitForData();

        expect(screen.queryByText('Upgrade')).not.toBeInTheDocument();
        expect(screen.queryByText('No Upgrade')).not.toBeInTheDocument();
    });

    it('renders page description', async () => {
        await renderAndWaitForData();

        expect(screen.getByText(/tiers are listed from highest to lowest priority/i)).toBeInTheDocument();
    });

    it('renders breadcrumb with tier list name', async () => {
        await renderAndWaitForData();

        expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();

        expect(screen.getAllByRole('link', { name: 'Quality Tiers' }).length).toBeGreaterThan(0);
    });

    it('updates MIME types from the detail header', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Edit MIME types for HD Standard' }));
        await user.click(screen.getByRole('button', { name: 'Remove video/*' }));
        await user.type(screen.getByRole('combobox', { name: 'MIME types for HD Standard' }), 'audio/*{Enter}');
        await user.click(screen.getByRole('button', { name: 'Save MIME changes' }));

        await waitFor(() =>
            expect(mockApiPatch).toHaveBeenCalledWith('/quality-tier-lists/qtl-1', {
                mimeTypes: { value: ['audio/*'] },
            }),
        );
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

    it('posts a valid Guid exclusion id when creating a tier with a new exclusion', async () => {
        const mockApiPost = vi.mocked(apiPost);
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const addButton = document.querySelector('[data-action="add-tier"]') as HTMLButtonElement;
        await user.click(addButton);

        await user.type(await screen.findByLabelText(/name/i), 'Excluded Tier');

        const form = document.querySelector('[data-component="tier-form"]') as HTMLElement;
        await user.click(within(form).getByRole('button', { name: /add exclusion/i }));
        const exclusions = within(form).getByText('Exclusions').closest('[data-component="exclusions-editor"]');
        await user.click(within(exclusions as HTMLElement).getByRole('button', { name: 'Add' }));
        await user.type(within(exclusions as HTMLElement).getByLabelText(/criterion 1 field/i), 'Source');
        await user.type(within(exclusions as HTMLElement).getByLabelText(/criterion 1 value/i), 'CAM');
        await user.click(within(form).getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith(
                '/quality-tiers',
                expect.objectContaining({ name: 'Excluded Tier' }),
            );
        });

        const createCall = mockApiPost.mock.calls.find(([url]) => url === '/quality-tiers');
        expect(createCall).toBeDefined();

        const body = createCall?.[1] as { rules?: { id: string }[] };
        expect(body.rules).toHaveLength(1);
        expect(body.rules?.[0].id).toMatch(guidPattern);
        expect(body.rules?.[0].id).not.toMatch(/^rule-/);
    });

    it('posts repeated criteria fields without dropping earlier criteria', async () => {
        const mockApiPost = vi.mocked(apiPost);
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const addButton = document.querySelector('[data-action="add-tier"]') as HTMLButtonElement;
        await user.click(addButton);

        await user.type(await screen.findByLabelText(/name/i), 'Dual Source');

        const form = document.querySelector('[data-component="tier-form"]') as HTMLElement;
        await user.click(form.querySelector('[data-action="add-criterion"]') as HTMLButtonElement);
        await user.click(form.querySelector('[data-action="add-criterion"]') as HTMLButtonElement);

        const fields = within(form).getAllByLabelText(/criterion \d+ field/i);
        await user.type(fields[0], 'Title');
        await user.type(fields[1], 'Title');

        const values = within(form).getAllByLabelText(/criterion \d+ value/i);
        await user.type(values[0], 'Bluray');
        await user.type(values[1], 'WEB');

        await user.click(within(form).getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith(
                '/quality-tiers',
                expect.objectContaining({ name: 'Dual Source' }),
            );
        });

        const createCall = mockApiPost.mock.calls.find(([url]) => url === '/quality-tiers');
        const body = createCall?.[1] as {
            criteria: {
                and?: { items?: Record<string, { operator?: string; value?: string | null }> }[];
            };
        };

        expect(body.criteria.and).toEqual([
            { items: { Title: { operator: 'Equals', value: 'Bluray' } } },
            { items: { Title: { operator: 'Equals', value: 'WEB' } } },
        ]);
    });
});

describe('TierListDetailPage management actions', () => {
    setupMockApi();

    it('Manager can see edit buttons', async () => {
        await renderAndWaitForData(Role.Manager);

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(4);
    });

    it('keeps stored criterion fields when saving an unchanged edited tier', async () => {
        const mockApiPatchLocal = vi.mocked(apiPatch);
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(screen.getByRole('button', { name: /edit bluray-1080p/i }));

        const form = document.querySelector('[data-component="tier-form"]') as HTMLElement;
        expect(within(form).getByLabelText(/criterion 1 field/i)).toHaveValue('source');
        expect(within(form).getByLabelText(/criterion 2 field/i)).toHaveValue('resolution');
        expect(within(form).getByLabelText(/criterion 3 field/i)).toHaveValue('codec');

        await user.click(within(form).getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPatchLocal).toHaveBeenCalledWith(
                '/quality-tiers/t1-1',
                expect.objectContaining({ name: { value: 'Bluray-1080p' } }),
            );
        });

        const updateCall = mockApiPatchLocal.mock.calls.find(([url]) => url === '/quality-tiers/t1-1');
        expect(updateCall).toBeDefined();

        const body = updateCall?.[1] as {
            criteria: {
                value: {
                    and?: { items?: Record<string, { operator?: string; value?: string }> }[];
                };
            };
        };
        const items = body.criteria.value.and?.[0].items ?? {};

        expect(items).toEqual({
            source: { operator: 'Equals', value: 'Bluray' },
            resolution: { operator: 'Equals', value: '1080p' },
            codec: { operator: 'Equals', value: 'x264' },
        });
        expect(items).not.toHaveProperty('Title');
    });

    it('Manager can see delete buttons', async () => {
        await renderAndWaitForData(Role.Manager);

        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        expect(deleteButtons.length).toBe(4);
    });

    it('Manager sees a cutoff toggle on every tier', async () => {
        await renderAndWaitForData(Role.Manager);

        const setCutoffButtons = document.querySelectorAll('[data-action="set-cutoff"]');

        expect(setCutoffButtons.length).toBe(4);
    });

    it('toggling cutoff on one tier does not clear the existing cutoff (multi-cutoff)', async () => {
        const mockApiPatchLocal = vi.mocked(apiPatch);
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const toggle = document.querySelector('[data-tier-id="t1-2"] [data-action="set-cutoff"]') as HTMLButtonElement;
        await user.click(toggle);

        await waitFor(() => {
            expect(mockApiPatchLocal).toHaveBeenCalledWith(
                '/quality-tier-lists/qtl-1',
                expect.objectContaining({
                    qualityTierRanks: {
                        update: [{ qualityTierId: { value: 't1-2' }, isCutoff: { value: true } }],
                    },
                }),
            );
        });

        const updates = vi.mocked(apiPatch).mock.calls[0][1] as {
            qualityTierRanks: { update: { qualityTierId: { value: string } }[] };
        };
        const touchedTierIds = updates.qualityTierRanks.update.map((u) => u.qualityTierId.value);
        expect(touchedTierIds).not.toContain('t1-1');
    });

    it('clearing cutoff on a cutoff tier only patches that tier', async () => {
        const mockApiPatchLocal = vi.mocked(apiPatch);
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        const toggle = document.querySelector('[data-tier-id="t1-1"] [data-action="set-cutoff"]') as HTMLButtonElement;
        await user.click(toggle);

        await waitFor(() => {
            expect(mockApiPatchLocal).toHaveBeenCalledWith(
                '/quality-tier-lists/qtl-1',
                expect.objectContaining({
                    qualityTierRanks: {
                        update: [{ qualityTierId: { value: 't1-1' }, isCutoff: { value: false } }],
                    },
                }),
            );
        });
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

describe('TierListDetailPage reordering', () => {
    const { mockApiPatch } = setupMockApi();

    it('Manager can drag a tier by its handle', async () => {
        await renderAndWaitForData(Role.Manager);

        const handles = screen.queryAllByTestId('tier-list-detail-page__drag-handle');
        expect(handles.length).toBe(4);
        handles.forEach((handle) => {
            expect(handle).toHaveAttribute('draggable', 'true');
        });
    });

    it('Viewer sees no reorder controls', async () => {
        await renderAndWaitForData(Role.Viewer);

        expect(screen.queryAllByTestId('tier-list-detail-page__drag-handle')).toHaveLength(0);
        expect(screen.queryByRole('button', { name: /move .* up/i })).not.toBeInTheDocument();
    });

    it('Requester sees no reorder controls', async () => {
        await renderAndWaitForData(Role.Requester);

        expect(screen.queryAllByTestId('tier-list-detail-page__drag-handle')).toHaveLength(0);
        expect(screen.queryByRole('button', { name: /move .* up/i })).not.toBeInTheDocument();
    });

    it('shows the reorder hint in the description for Manager role', async () => {
        await renderAndWaitForData(Role.Manager);

        expect(screen.getByText(/drag tiers or use the move buttons to reorder/i)).toBeInTheDocument();
    });

    it('does not show the reorder hint for Viewer role', async () => {
        await renderAndWaitForData(Role.Viewer);

        expect(screen.queryByText(/use the move buttons to reorder/i)).not.toBeInTheDocument();
    });

    it('saves the new ordinals when a tier is moved up with the keyboard', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Move HDTV-1080p up' }));

        await waitFor(() =>
            expect(mockApiPatch).toHaveBeenCalledWith('/quality-tier-lists/qtl-1', {
                qualityTierRanks: {
                    update: [
                        { qualityTierId: { value: 't1-2' }, ordinal: { value: 0 } },
                        { qualityTierId: { value: 't1-1' }, ordinal: { value: 1 } },
                        { qualityTierId: { value: 't1-3' }, ordinal: { value: 2 } },
                        { qualityTierId: { value: 't1-4' }, ordinal: { value: 3 } },
                    ],
                },
            }),
        );
    });

    it('renders the refetched server order once the move is saved', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Move HDTV-1080p up' }));

        await waitFor(() =>
            expect(
                [...document.querySelectorAll('[data-tier-id]')].map((tier) => tier.getAttribute('data-tier-id')),
            ).toEqual(['t1-1', 't1-2', 't1-3', 't1-4']),
        );
    });

    it('announces the move in a live region', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Move HDTV-1080p up' }));

        await waitFor(() => expect(screen.getByText('HDTV-1080p moved to position 1 of 4.')).toBeInTheDocument());
    });
});

describe('TierListDetailPage delete confirmation', () => {
    const { mockApiGet } = setupMockApi();
    const mockApiDelete = vi.mocked(apiDelete);

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
                expect(screen.getByText('Removed tier "Bluray-1080p"')).toBeInTheDocument();
            },
            { timeout: 3000 },
        );
    });

    it('deletes the quality tier row when no other list ranks it', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(document.querySelector('[data-action="delete"]') as HTMLButtonElement);
        await waitFor(() => expect(screen.getByText('Delete Tier')).toBeInTheDocument());
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement);

        await waitFor(() => expect(mockApiDelete).toHaveBeenCalledWith('/quality-tiers/t1-1'));
    });

    it('keeps the quality tier row when another list still ranks it', async () => {
        const sharedList: ApiQualityTierList = {
            ...mockApiTierLists['qtl-3'],
            qualityTierRanks: mockApiTierLists['qtl-1'].qualityTierRanks,
        };
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/quality-tier-lists/qtl-1') {
                return Promise.resolve(mockApiTierLists['qtl-1']);
            }
            if (url.startsWith('/quality-tier-lists')) {
                return Promise.resolve([mockApiTierLists['qtl-1'], sharedList]);
            }
            return Promise.resolve([]);
        });
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(document.querySelector('[data-action="delete"]') as HTMLButtonElement);
        await waitFor(() => expect(screen.getByText('Delete Tier')).toBeInTheDocument());
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement);

        await waitFor(() => expect(screen.getByText('Deleted')).toBeInTheDocument());
        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('keeps the quality tier row when the tier list collection has not loaded', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/quality-tier-lists/qtl-1') {
                return Promise.resolve(mockApiTierLists['qtl-1']);
            }
            if (url.startsWith('/quality-tier-lists')) {
                return new Promise<ApiQualityTierList[]>(() => {});
            }
            return Promise.resolve([]);
        });
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(document.querySelector('[data-action="delete"]') as HTMLButtonElement);
        await waitFor(() => expect(screen.getByText('Delete Tier')).toBeInTheDocument());
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement);

        await waitFor(() => expect(screen.getByText('Deleted')).toBeInTheDocument());
        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('reports that the tier survived when the quality tier row cannot be deleted', async () => {
        mockApiDelete.mockRejectedValueOnce(new Error('restricted'));
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(document.querySelector('[data-action="delete"]') as HTMLButtonElement);
        await waitFor(() => expect(screen.getByText('Delete Tier')).toBeInTheDocument());
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement);

        await waitFor(() => expect(screen.getByText('Tier kept')).toBeInTheDocument());
        expect(screen.queryByText('Delete failed')).not.toBeInTheDocument();
    });
});

describe('TierListDetailPage not found state', () => {
    setupMockApi();

    it('shows not found message for invalid tier list ID', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/invalid-id');

        expect(screen.getByRole('heading', { name: 'Tier List Not Found' })).toBeInTheDocument();
        expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
    });

    it('shows back link in not found state', async () => {
        await renderAndWaitForData(Role.Admin, '/quality-tiers/invalid-id');

        expect(screen.getByRole('link', { name: /back to quality tiers/i })).toBeInTheDocument();
    });

    it('shows not found state for the zero UUID admin tier list route', async () => {
        await renderAndWaitForData(Role.Admin, `/admin/quality-tiers/${MISSING_DETAIL_ID}`);

        expect(screen.getByRole('heading', { name: 'Tier List Not Found' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: /back to quality tiers/i })).toHaveAttribute(
            'href',
            '/admin/quality-tiers',
        );
        expect(document.querySelector('.tier-list-detail-page__loading')).not.toBeInTheDocument();
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
            <LiveAnnouncerProvider>
                <TestWrapper>
                    <TierListDetailPage />
                </TestWrapper>
            </LiveAnnouncerProvider>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Failed to Load Tier List' })).toBeInTheDocument();
        });
    });

    it('shows error message from API', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestWrapper>
                    <TierListDetailPage />
                </TestWrapper>
            </LiveAnnouncerProvider>,
        );

        await waitFor(() => {
            expect(screen.getByText('Connection refused')).toBeInTheDocument();
        });
    });

    it('shows retry button in error state', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestWrapper>
                    <TierListDetailPage />
                </TestWrapper>
            </LiveAnnouncerProvider>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
        });
    });

    it('shows back link in error state', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestWrapper>
                    <TierListDetailPage />
                </TestWrapper>
            </LiveAnnouncerProvider>,
        );

        await waitFor(() => {
            expect(screen.getByRole('link', { name: /back to quality tiers/i })).toBeInTheDocument();
        });
    });
});

describe('TierListDetailPage list criteria and exclusions', () => {
    const { mockApiPatch } = setupMockApi();

    async function openListRules(user: ReturnType<typeof userEvent.setup>) {
        await user.click(document.querySelector('[data-action="list-rules"]') as HTMLElement);
        await waitFor(() => {
            expect(document.querySelector('[data-component="list-rules-form"]')).toBeInTheDocument();
        });
    }

    it('shows separate list-wide criteria and exclusion sections', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await openListRules(user);

        expect(screen.getByText('Criteria for the entire list')).toBeInTheDocument();
        expect(screen.getByText('Exclusions for the entire list')).toBeInTheDocument();
        expect(screen.getByText(/apply to every tier in "HD Standard"/i)).toBeInTheDocument();
        expect(screen.getByText(/before any tier in this list is considered/i)).toBeInTheDocument();
        expect(screen.getByText(/rejects the release before any tier in this list is evaluated/i)).toBeInTheDocument();
        expect(screen.queryByLabelText(/kind/i)).not.toBeInTheDocument();
    });

    it('opens the criteria sheet from the Tier List Actions sheet and closes the actions sheet', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Tier List Actions' }));
        const actions = await screen.findByRole('dialog', { name: 'Tier List Actions' });
        expect(
            within(within(actions).getByRole('group', { name: 'Quick actions' }))
                .getAllByRole('button')
                .map((action) => action.textContent),
        ).toEqual(['Add Tier', 'Criteria + Exclusions']);
        await user.click(within(actions).getByRole('button', { name: 'Criteria + Exclusions' }));

        await waitFor(() => {
            expect(document.querySelector('[data-component="list-rules-form"]')).toBeInTheDocument();
        });
        expect(screen.queryByRole('dialog', { name: 'Tier List Actions' })).not.toBeInTheDocument();
    });

    it('closes without warning when only a blank exclusion was inserted', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await openListRules(user);

        const sheet = document.querySelector('[data-component="list-rules-form"]') as HTMLElement;
        await user.click(within(sheet).getByRole('button', { name: /add exclusion/i }));
        await user.click(within(sheet).getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        await waitFor(() => {
            expect(document.querySelector('[data-component="list-rules-form"]')).not.toBeInTheDocument();
        });
    });

    it('warns before discarding a real list exclusion edit', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await openListRules(user);

        const sheet = document.querySelector('[data-component="list-rules-form"]') as HTMLElement;
        await user.click(within(sheet).getByRole('button', { name: /add exclusion/i }));
        await user.type(within(sheet).getByLabelText(/exclusion name/i), 'Reject CAM');
        await user.click(within(sheet).getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(document.querySelector('[data-component="list-rules-form"]')).toBeInTheDocument();
    });

    it('refuses to save a list criterion with no field name', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await openListRules(user);

        const sheet = document.querySelector('[data-component="list-rules-form"]') as HTMLElement;
        await user.click(within(sheet).getByRole('button', { name: 'Add' }));
        await user.type(within(sheet).getByLabelText(/criterion 1 value/i), 'Bluray');
        await user.click(within(sheet).getByRole('button', { name: /save criteria and exclusions/i }));

        expect(within(sheet).getByText('Every criterion needs a field name')).toBeInTheDocument();
        expect(mockApiPatch).not.toHaveBeenCalled();
    });

    it('refuses to save a list exclusion criterion with no field name', async () => {
        const user = userEvent.setup();
        await renderAndWaitForData(Role.Manager);

        await openListRules(user);

        const sheet = document.querySelector('[data-component="list-rules-form"]') as HTMLElement;
        await user.click(within(sheet).getByRole('button', { name: /add exclusion/i }));
        await user.click(within(sheet).getAllByRole('button', { name: 'Add' })[1]);
        await user.type(within(sheet).getAllByLabelText(/criterion 1 value/i)[0], 'CAM');
        await user.click(within(sheet).getByRole('button', { name: /save criteria and exclusions/i }));

        expect(within(sheet).getByText('Every criterion needs a field name')).toBeInTheDocument();
        expect(mockApiPatch).not.toHaveBeenCalled();
    });
});
