import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthProvider, Role } from '@/auth';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiQualityTierList } from '@/hooks/useQualityTierLists';
import { testEffectivePermissions } from '@/test/permissions';

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

import { apiDelete, apiGet, apiPatch, apiPost } from '@/lib/api';

const tierLists: ApiQualityTierList[] = [
    {
        id: 'video-list',
        name: 'HD Standard',
        mimeTypes: ['video/*'],
        upgradeAllowed: true,
        createdAt: '2026-01-01T00:00:00Z',
        modifiedAt: '2026-02-01T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'video-list',
                qualityTierId: 'high-tier',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2026-01-01T00:00:00Z',
                qualityTier: {
                    id: 'high-tier',
                    name: 'High',
                    criteria: {},
                    createdAt: '2026-01-01T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'video-list',
                qualityTierId: 'standard-tier',
                ordinal: 1,
                isCutoff: false,
                createdAt: '2026-01-01T00:00:00Z',
                qualityTier: {
                    id: 'standard-tier',
                    name: 'Standard',
                    criteria: {},
                    createdAt: '2026-01-01T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'audio-list',
        name: 'Audio Policy',
        mimeTypes: ['audio/*', 'application/vnd.unknown'],
        upgradeAllowed: false,
        createdAt: '2026-01-02T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'audio-list',
                qualityTierId: 'lossless-tier',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2026-01-02T00:00:00Z',
                qualityTier: {
                    id: 'lossless-tier',
                    name: 'Lossless',
                    criteria: {},
                    createdAt: '2026-01-02T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'universal-list',
        name: 'Universal',
        mimeTypes: [],
        upgradeAllowed: true,
        createdAt: '2026-01-03T00:00:00Z',
        qualityTierRanks: [],
    },
];

function LocationDisplay() {
    const location = useLocation();
    return <div data-testid="location">{location.pathname + location.search}</div>;
}

function TestWrapper({ children, role = Role.Admin }: { children: ReactNode; role?: Role }) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={['/admin/quality-tiers']}>
                <AuthProvider
                    initialUser={{ id: 'user-1', username: 'test', role }}
                    initialPermissions={testEffectivePermissions()}
                >
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <LocationDisplay />
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

async function renderPage(role: Role = Role.Admin) {
    render(
        <TestWrapper role={role}>
            <QualityTiersPage />
        </TestWrapper>,
    );
    await waitFor(() => expect(document.querySelector('[data-row-id="video-list"]')).toBeInTheDocument());
}

describe('QualityTiersPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/quality-tier-lists')) {
                return Promise.resolve(tierLists);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([{ name: 'Movies' }]);
            }
            return Promise.reject(new Error(`Unexpected request: ${url}`));
        });
        vi.mocked(apiPost).mockResolvedValue({ ...tierLists[2], id: 'created-list', name: 'Books' });
        vi.mocked(apiPatch).mockResolvedValue(undefined);
        vi.mocked(apiDelete).mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the quality tiers registry with its summary fields', async () => {
        await renderPage();

        expect(screen.getByRole('heading', { name: 'Quality Tiers', level: 1 })).toBeInTheDocument();
        expect(document.title).toContain('Quality Tiers');
        expect(document.querySelector('[data-component="data-table"][aria-label="Quality tiers"]')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'HD Standard' })).toHaveAttribute(
            'href',
            '/admin/quality-tiers/video-list',
        );
        expect(screen.getByText('Any')).toBeInTheDocument();
        expect(screen.getByText('audio/*, application/vnd.unknown')).toBeInTheDocument();

        const row = document.querySelector('[data-row-id="video-list"]') as HTMLElement;
        expect(within(row).getByText('Allowed')).toBeInTheDocument();
        for (const label of ['Name', 'File Types', 'Upgrades']) {
            expect(row.querySelector(`[data-label="${label}"]`)).toBeInTheDocument();
        }
        for (const label of ['Tier Count', 'Cutoff', 'Last Updated']) {
            expect(row.querySelector(`[data-label="${label}"]`)).not.toBeInTheDocument();
        }
    });

    it('sorts by standard table columns', async () => {
        const user = userEvent.setup();
        await renderPage();

        await user.click(screen.getByRole('button', { name: 'Sort by Upgrades' }));

        await waitFor(() =>
            expect(
                [...document.querySelectorAll('[data-row-id]')].map((row) => row.getAttribute('data-row-id')),
            ).toEqual(['video-list', 'universal-list', 'audio-list']),
        );
        expect(screen.getByTestId('location')).toHaveTextContent('sort=upgradeAllowed%3Adesc');
    });

    it('filters by name and MIME types', async () => {
        const user = userEvent.setup();
        await renderPage();

        await user.click(screen.getByRole('button', { name: 'Filter by Name' }));
        await user.type(screen.getByLabelText('Name'), 'audio');
        await user.click(screen.getByRole('button', { name: 'Apply' }));
        await waitFor(() => expect(document.querySelectorAll('[data-row-id]')).toHaveLength(1));
        expect(document.querySelector('[data-row-id="audio-list"]')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Name filtered, edit filter' }));
        await user.click(screen.getByRole('button', { name: 'Clear' }));
        await user.click(screen.getByRole('button', { name: 'Filter by File Types' }));
        await user.click(screen.getByRole('menuitemradio', { name: 'audio/*' }));
        await waitFor(() => expect(document.querySelectorAll('[data-row-id]')).toHaveLength(1));
        expect(document.querySelector('[data-row-id="audio-list"]')).toBeInTheDocument();
    });

    it('allows managers to edit, clone, and delete through row actions', async () => {
        const user = userEvent.setup();
        await renderPage(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Clone HD Standard' }));
        await waitFor(() =>
            expect(apiPost).toHaveBeenCalledWith(
                '/quality-tier-lists',
                expect.objectContaining({ name: 'HD Standard (Copy)', mimeTypes: ['video/*'] }),
            ),
        );

        await user.click(screen.getByRole('button', { name: 'Delete HD Standard' }));
        expect(screen.getByText('Delete Tier List')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Delete' }));
        await waitFor(() => expect(apiDelete).toHaveBeenCalledWith('/quality-tier-lists/video-list'));
    });

    it('does not delete before confirmation', async () => {
        const user = userEvent.setup();
        await renderPage(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Delete HD Standard' }));

        expect(screen.getByText('Delete Tier List')).toBeInTheDocument();
        expect(apiDelete).not.toHaveBeenCalled();
    });

    it('shows the clone pending state', async () => {
        vi.mocked(apiPost).mockImplementation(() => new Promise<never>(() => {}));
        const user = userEvent.setup();
        await renderPage(Role.Manager);

        await user.click(screen.getByRole('button', { name: 'Clone HD Standard' }));

        const cloneButton = await screen.findByRole('button', { name: 'Cloning HD Standard' });
        expect(cloneButton).toBeDisabled();
        expect(cloneButton.querySelector('.quality-tiers-page__spinner')).toBeInTheDocument();
    });

    it('hides every manager action from viewers', async () => {
        await renderPage(Role.Viewer);

        expect(screen.queryByRole('button', { name: /clone hd standard/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /delete hd standard/i })).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="add-tier-list"]')).not.toBeInTheDocument();
    });

    it('hides every manager action from requesters', async () => {
        await renderPage(Role.Requester);

        expect(screen.queryByRole('button', { name: /clone hd standard/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /delete hd standard/i })).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="add-tier-list"]')).not.toBeInTheDocument();
    });

    it('creates a normalized MIME-aware list and opens its detail page', async () => {
        const user = userEvent.setup();
        await renderPage(Role.Manager);

        await user.click(document.querySelector('[data-action="add-tier-list"]') as HTMLElement);
        await user.type(screen.getByLabelText(/^Name/), '  Books  ');
        await user.type(screen.getByRole('combobox'), ' Application/EPUB+ZIP {Enter}');
        await user.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() =>
            expect(apiPost).toHaveBeenCalledWith('/quality-tier-lists', {
                name: 'Books',
                mimeTypes: ['application/epub+zip'],
                upgradeAllowed: true,
            }),
        );
        await waitFor(() =>
            expect(screen.getByTestId('location')).toHaveTextContent('/admin/quality-tiers/created-list'),
        );
    });

    it('shows the empty state', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) =>
            Promise.resolve(url.startsWith('/quality-tier-lists') ? [] : [{ name: 'Movies' }]),
        );

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        expect(await screen.findByText('No tier lists configured')).toBeInTheDocument();
    });

    it('shows the error state and retry action', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) =>
            url.startsWith('/quality-tier-lists')
                ? Promise.reject(new Error('Offline'))
                : Promise.resolve([{ name: 'Movies' }]),
        );

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        expect(await screen.findByText('Failed to load the tier lists.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    });

    it('announces the loading state', () => {
        vi.mocked(apiGet).mockImplementation((url: string) =>
            url.startsWith('/quality-tier-lists')
                ? new Promise<ApiQualityTierList[]>(() => {})
                : Promise.resolve([{ name: 'Movies' }]),
        );

        render(
            <TestWrapper>
                <QualityTiersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('status')).toHaveTextContent('Loading tier lists...');
    });
});
