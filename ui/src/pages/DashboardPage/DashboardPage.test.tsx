import { render, screen, within, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { AuthProvider, Role, type User } from '@/auth';
import { deriveOnboardingSteps, type OnboardingStatus } from '@/hooks/useOnboardingStatus';
import { testEffectivePermissions } from '@/test/permissions';
import { DashboardPage } from './DashboardPage';

const mockOnboardingStatus = vi.fn();

vi.mock('@/hooks/useOnboardingStatus', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useOnboardingStatus')>('@/hooks/useOnboardingStatus');
    return { ...actual, useOnboardingStatus: () => mockOnboardingStatus() };
});

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockClientHealthResult = vi.fn();
const mockServerHealthResult = vi.fn();
const mockActiveDownloadsResult = vi.fn();
const mockRecentJobsResult = vi.fn();
const mockContentCountsResult = vi.fn();
const mockRecentContentResult = vi.fn();
const mockUpcomingContentResult = vi.fn();

vi.mock('@/hooks/useDashboard', () => ({
    dashboardQueryKeys: {
        all: ['dashboard'],
        downloadClients: ['dashboard', 'download-clients'],
        mediaServers: ['dashboard', 'media-servers'],
        downloads: ['dashboard', 'downloads'],
        jobs: ['dashboard', 'jobs'],
        contentStats: ['dashboard', 'content-stats'],
        recentContent: ['dashboard', 'recent-content'],
        upcomingContent: (today: string) => ['dashboard', 'upcoming-content', today],
    },
    useDownloadClientHealth: () => mockClientHealthResult(),
    useMediaServerHealth: () => mockServerHealthResult(),
    useActiveDownloads: () => mockActiveDownloadsResult(),
    useRecentJobs: () => mockRecentJobsResult(),
    useContentCounts: () => mockContentCountsResult(),
    useRecentContent: () => mockRecentContentResult(),
    useUpcomingContent: () => mockUpcomingContentResult(),
}));

const defaultClientHealth = { healthy: 2, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 1, stale: 0 };
const defaultServerHealth = { healthy: 1, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 0, stale: 0 };

const defaultActiveDownloads = {
    counts: { downloading: 3, queued: 7, paused: 2, stalled: 1 },
    items: [
        {
            id: 'dl-1',
            title: 'Breaking Bad S01E01 (1080p BluRay)',
            progress: 67,
            state: 'downloading' as const,
            size: '2.4 GB',
            eta: '12 min',
        },
        {
            id: 'dl-2',
            title: 'The Office S03E07 (720p WEB)',
            progress: 23,
            state: 'downloading' as const,
            size: '850 MB',
            eta: '8 min',
        },
        {
            id: 'dl-3',
            title: 'Dune Part Two (2024) (2160p)',
            progress: 5,
            state: 'downloading' as const,
            size: '18.2 GB',
            eta: '2h 15min',
        },
    ],
};

const defaultRecentJobs = [
    {
        id: '11111111-1111-4111-8111-111111111111',
        name: 'Content metadata refresh item',
        status: 'completed' as const,
        duration: '2m 15s',
        timestamp: '5 min ago',
    },
    {
        id: '22222222-2222-4222-8222-222222222222',
        name: 'Search Missing',
        status: 'running' as const,
        timestamp: '10 min ago',
    },
    {
        id: '33333333-3333-4333-8333-333333333333',
        name: 'Sync Plex Library',
        status: 'completed' as const,
        duration: '45s',
        timestamp: '1h ago',
    },
];

const defaultContentCounts = {
    total: 307,
    missing: 42,
    upgradable: 15,
    fulfilled: 238,
    fulfilledExternally: 12,
    partial: 18,
};

const defaultRecentContent = [
    { id: 'content-1', title: 'Shogun (2024)', addedAt: '2h ago', fulfillment: 'fulfilled' as const },
    { id: 'content-2', title: 'The Gentlemen', addedAt: '5h ago', fulfillment: 'missing' as const },
    { id: 'content-3', title: '3 Body Problem', addedAt: '1d ago', fulfillment: 'upgradable' as const },
];

const defaultUpcomingContent = [
    {
        id: 'upcoming-1',
        contentId: 'content-1',
        title: 'Shogun',
        subtitle: 'S01E10 - A Dream of a Dream',
        date: new Date().toISOString(),
        posterUrl: 'https://image.provider-a.example/t/p/w92/7O4iVfOMQmdCSxhOg1WnzG1AgYT.jpg',
        kind: 'episode' as const,
        fulfillment: 'missing' as const,
    },
    {
        id: 'upcoming-2',
        contentId: 'content-4',
        title: 'Fallout',
        subtitle: 'S01E08 - The Beginning',
        date: new Date().toISOString(),
        posterUrl: 'https://image.provider-a.example/t/p/w92/AnsSKR9LuK0T9bSHlYNWIJgJzlX.jpg',
        kind: 'episode' as const,
        fulfillment: 'fulfilled' as const,
    },
];

function makeResult<T>(data: T, loading = false, error: Error | null = null) {
    return { data: loading ? undefined : data, isLoading: loading, error };
}

function setLoadingMocks() {
    mockClientHealthResult.mockReturnValue(makeResult(undefined, true));
    mockServerHealthResult.mockReturnValue(makeResult(undefined, true));
    mockActiveDownloadsResult.mockReturnValue(makeResult(undefined, true));
    mockRecentJobsResult.mockReturnValue(makeResult(undefined, true));
    mockContentCountsResult.mockReturnValue(makeResult(undefined, true));
    mockRecentContentResult.mockReturnValue(makeResult(undefined, true));
    mockUpcomingContentResult.mockReturnValue(makeResult(undefined, true));
}

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });
}

const adminUser: User = { id: 'admin-1', username: 'admin', role: Role.Admin };
const viewerUser: User = { id: 'viewer-1', username: 'viewer', role: Role.Viewer };
const completeOnboarding: OnboardingStatus = {
    hasIndexer: true,
    hasDownloadClient: true,
    hasRootFolder: true,
    hasMonitoredContent: true,
    isComplete: true,
};
const partialOnboarding: OnboardingStatus = {
    hasIndexer: false,
    hasDownloadClient: true,
    hasRootFolder: true,
    hasMonitoredContent: false,
    isComplete: false,
};

async function renderDashboard(user: User = adminUser) {
    const queryClient = createTestQueryClient();

    const result = render(
        <QueryClientProvider client={queryClient}>
            <AuthProvider initialUser={user} skipSessionInit initialPermissions={testEffectivePermissions()}>
                <MemoryRouter>
                    <DashboardPage />
                </MemoryRouter>
            </AuthProvider>
        </QueryClientProvider>,
    );

    await waitFor(() => {
        expect(screen.getByText('Shogun (2024)')).toBeInTheDocument();
    });

    return result;
}

describe('DashboardPage', () => {
    it.each([
        { isLoading: true, error: null, errorCount: 0 },
        { isLoading: false, error: new Error('Upcoming unavailable'), errorCount: 1 },
    ])('keeps content statistics when upcoming is unavailable: %j', async ({ isLoading, error, errorCount }) => {
        // Arrange
        mockUpcomingContentResult.mockReturnValue({ data: undefined, isLoading, error });
        // Act
        await renderDashboard();
        // Assert
        expect(screen.getByText('Shogun (2024)')).toBeInTheDocument();
        expect(screen.getByText('The Gentlemen')).toBeInTheDocument();
        expect(screen.queryAllByText('Failed to load')).toHaveLength(errorCount);
    });

    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockClientHealthResult.mockReturnValue(makeResult(defaultClientHealth));
        mockServerHealthResult.mockReturnValue(makeResult(defaultServerHealth));
        mockActiveDownloadsResult.mockReturnValue(makeResult(defaultActiveDownloads));
        mockRecentJobsResult.mockReturnValue(makeResult(defaultRecentJobs));
        mockContentCountsResult.mockReturnValue(makeResult(defaultContentCounts));
        mockRecentContentResult.mockReturnValue(makeResult(defaultRecentContent));
        mockUpcomingContentResult.mockReturnValue(makeResult(defaultUpcomingContent));
        mockOnboardingStatus.mockReturnValue({
            status: completeOnboarding,
            steps: deriveOnboardingSteps(completeOnboarding, 'Magpie'),
            isComplete: true,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
    });

    it('renders the dashboard page with correct data-page attribute', async () => {
        await renderDashboard();

        expect(document.querySelector('[data-page="dashboard"]')).toBeInTheDocument();
    });

    it('renders the page title', async () => {
        await renderDashboard();

        expect(screen.getByRole('heading', { name: 'Dashboard', level: 1 })).toBeInTheDocument();
    });

    describe('Service Health Bar', () => {
        it('renders the compact service health bar', async () => {
            await renderDashboard();

            const healthBar = document.querySelector('[data-component="service-health-bar"]');
            expect(healthBar).toBeInTheDocument();
        });

        it('renders an accessible heading', async () => {
            await renderDashboard();

            expect(screen.getByRole('heading', { level: 2, name: 'Service Health' })).toBeInTheDocument();
        });

        it('displays both service names', async () => {
            await renderDashboard();

            const healthBar = document.querySelector('[data-component="service-health-bar"]')!;
            expect(within(healthBar as HTMLElement).getByText('Download Clients')).toBeInTheDocument();
            expect(within(healthBar as HTMLElement).getByText('Media Servers')).toBeInTheDocument();
        });

        it('shows unknown summary for clients with unknown status', async () => {
            await renderDashboard();

            const clientSegment = screen.getByLabelText(/download clients: 1 unknown/i);
            expect(clientSegment).toBeInTheDocument();
        });

        it('shows OK summary for fully healthy servers', async () => {
            await renderDashboard();

            const serverSegment = screen.getByLabelText(/media servers: 1 ok/i);
            expect(serverSegment).toBeInTheDocument();
        });

        it('links healthy service to filtered page', async () => {
            await renderDashboard();

            const serverLink = screen.getByLabelText(/media servers: 1 ok/i);
            expect(serverLink.tagName).toBe('A');
            expect(serverLink).toHaveAttribute('href', '/admin/media-servers?status=healthy');
        });

        it('keeps download-client add-first navigation for empty admin state', async () => {
            mockClientHealthResult.mockReturnValue(
                makeResult({ healthy: 0, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 0, stale: 0 }),
            );

            await renderDashboard();

            const clientLink = screen.getByLabelText(/download clients: add first/i);
            expect(clientLink.tagName).toBe('A');
            expect(clientLink).toHaveAttribute('href', '/admin/download-clients');
        });

        it('does not link integration health to admin pages for non-admin users', async () => {
            await renderDashboard(viewerUser);

            const clientSegment = screen.getByLabelText(/download clients: 1 unknown/i);
            expect(clientSegment.tagName).not.toBe('A');
            expect(clientSegment).not.toHaveAttribute('href');
        });

        it('renders the health summary for a Viewer (not gated on system.view)', async () => {
            await renderDashboard(viewerUser);

            const healthBar = document.querySelector('[data-component="service-health-bar"]');
            expect(healthBar).toBeInTheDocument();
            expect(within(healthBar as HTMLElement).getByText('Download Clients')).toBeInTheDocument();
            expect(within(healthBar as HTMLElement).getByText('Media Servers')).toBeInTheDocument();
        });
    });

    describe('Download Queue Widget', () => {
        it('renders download queue widget with counts', async () => {
            await renderDashboard();

            const widgetSection = screen.getByRole('region', { name: /dashboard widgets/i });
            const downloadWidget = within(widgetSection)
                .getByRole('heading', { name: 'Downloads' })
                .closest<HTMLElement>('[data-component="download-queue-widget"]');

            expect(downloadWidget).toBeInTheDocument();

            expect(within(downloadWidget!).getByText('Downloading')).toBeInTheDocument();
            expect(within(downloadWidget!).getByText('Queued')).toBeInTheDocument();
            expect(within(downloadWidget!).getByText('Stalled')).toBeInTheDocument();
        });

        it('renders active downloads from API data', async () => {
            await renderDashboard();

            const widgetSection = screen.getByRole('region', { name: /dashboard widgets/i });
            const downloadWidget = within(widgetSection)
                .getByRole('heading', { name: 'Downloads' })
                .closest<HTMLElement>('[data-component="download-queue-widget"]');

            expect(within(downloadWidget!).getByText('Breaking Bad S01E01 (1080p BluRay)')).toBeInTheDocument();
            expect(within(downloadWidget!).getByText('The Office S03E07 (720p WEB)')).toBeInTheDocument();
            expect(within(downloadWidget!).getByText('Dune Part Two (2024) (2160p)')).toBeInTheDocument();
        });

        it('has link to downloads page for view all', async () => {
            await renderDashboard();

            const viewAllLink = screen.getByRole('link', { name: /view all downloads/i });

            expect(viewAllLink).toHaveAttribute('href', '/downloads');
        });

        it('deep-links status rows to the active-tab qstate filter', async () => {
            await renderDashboard();

            expect(screen.getByText('Downloading').closest('a')).toHaveAttribute(
                'href',
                '/downloads?qstate=Downloading',
            );
            expect(screen.getByText('Queued').closest('a')).toHaveAttribute('href', '/downloads?qstate=Queued');
            expect(screen.getByText('Stalled').closest('a')).toHaveAttribute('href', '/downloads?qstate=Stalled');
        });
    });

    describe('Recent Jobs Widget', () => {
        it('renders recent jobs widget', async () => {
            await renderDashboard();

            const widgetSection = screen.getByRole('region', { name: /dashboard widgets/i });
            const jobsWidget = within(widgetSection)
                .getByRole('heading', { name: 'Recent Jobs' })
                .closest<HTMLElement>('[data-component="recent-jobs-widget"]');

            expect(jobsWidget).toBeInTheDocument();
        });

        it('renders job items from API data', async () => {
            await renderDashboard();

            expect(screen.getByText('Content metadata refresh item')).toBeInTheDocument();
            expect(screen.getByText('Search Missing')).toBeInTheDocument();
            expect(screen.getByText('Sync Plex Library')).toBeInTheDocument();
        });

        it('links to admin jobs page for admin users', async () => {
            await renderDashboard(adminUser);

            const viewAllLink = screen.getByRole('link', { name: /view all jobs/i });

            expect(viewAllLink).toHaveAttribute('href', '/admin/jobs');
        });

        it('links to my-jobs page for non-admin users', async () => {
            await renderDashboard(viewerUser);

            const viewAllLink = screen.getByRole('link', { name: /view all jobs/i });

            expect(viewAllLink).toHaveAttribute('href', '/my-jobs');
        });

        it('generates role-aware job item URLs for admin users', async () => {
            await renderDashboard(adminUser);

            const jobLink = screen.getByRole('link', { name: /view content metadata refresh item job details/i });

            expect(jobLink).toHaveAttribute('href', '/admin/jobs?job=11111111-1111-4111-8111-111111111111');
        });

        it('generates role-aware job item URLs for non-admin users', async () => {
            await renderDashboard(viewerUser);

            const jobLink = screen.getByRole('link', { name: /view content metadata refresh item job details/i });

            expect(jobLink).toHaveAttribute('href', '/my-jobs?job=11111111-1111-4111-8111-111111111111');
        });
    });

    describe('Content Stats Widget', () => {
        it('renders content stats widget', async () => {
            await renderDashboard();

            const statsSection = screen.getByRole('region', { name: /content overview/i });
            const contentWidget = within(statsSection)
                .getByRole('heading', { name: 'Content' })
                .closest<HTMLElement>('[data-component="content-stats-widget"]');

            expect(contentWidget).toBeInTheDocument();
        });

        it('renders fulfillment status counts from API data', async () => {
            await renderDashboard();

            const statsSection = screen.getByRole('region', { name: /content overview/i });
            const contentWidget = within(statsSection)
                .getByRole('heading', { name: 'Content' })
                .closest<HTMLElement>('[data-component="content-stats-widget"]');

            expect(within(contentWidget!).getAllByText('Fulfilled').length).toBeGreaterThanOrEqual(1);
            expect(within(contentWidget!).getAllByText('Missing').length).toBeGreaterThanOrEqual(1);
            expect(within(contentWidget!).getAllByText('Upgradable').length).toBeGreaterThanOrEqual(1);
            expect(within(contentWidget!).getByText('External')).toBeInTheDocument();
        });

        it('renders recent content items from API data', async () => {
            await renderDashboard();

            const statsSection = screen.getByRole('region', { name: /content overview/i });
            const contentWidget = within(statsSection)
                .getByRole('heading', { name: 'Content' })
                .closest<HTMLElement>('[data-component="content-stats-widget"]');

            expect(within(contentWidget!).getByText('Shogun (2024)')).toBeInTheDocument();
            expect(within(contentWidget!).getByText('The Gentlemen')).toBeInTheDocument();
            expect(within(contentWidget!).getByText('3 Body Problem')).toBeInTheDocument();
        });

        it('has link to library page for view all content', async () => {
            await renderDashboard();

            const viewAllLink = screen.getByRole('link', { name: /view all content/i });

            expect(viewAllLink).toHaveAttribute('href', '/library');
        });

        it.each([
            ['View 238 fulfilled items', '/library/all?fulfillment=fulfilled&levels=all'],
            ['View 15 upgradable items', '/library/all?fulfillment=upgradable&levels=all'],
            ['View 18 partial items', '/library/all?fulfillment=partial&levels=all'],
            ['View 42 missing items', '/library/all?fulfillment=missing&levels=all'],
            ['View 12 external items', '/library/all?fulfillment=fulfilledExternally&levels=all'],
        ])('links "%s" to the library at every level', async (name, href) => {
            // Arrange
            await renderDashboard();
            const statsSection = screen.getByRole('region', { name: /content overview/i });
            const contentWidget = within(statsSection)
                .getByRole('heading', { name: 'Content' })
                .closest<HTMLElement>('[data-component="content-stats-widget"]');

            // Act
            const link = within(contentWidget!).getByRole('link', { name });

            // Assert
            expect(link).toHaveAttribute('href', href);
        });
    });

    describe('Upcoming Content Widget', () => {
        it('renders upcoming content widget', async () => {
            await renderDashboard();

            const statsSection = screen.getByRole('region', { name: /content overview/i });
            const upcomingWidget = within(statsSection)
                .getByRole('heading', { name: 'Upcoming' })
                .closest<HTMLElement>('[data-component="upcoming-content-widget"]');

            expect(upcomingWidget).toBeInTheDocument();
        });

        it('renders upcoming content items from API data', async () => {
            await renderDashboard();

            const statsSection = screen.getByRole('region', { name: /content overview/i });
            const upcomingWidget = within(statsSection)
                .getByRole('heading', { name: 'Upcoming' })
                .closest<HTMLElement>('[data-component="upcoming-content-widget"]');

            expect(within(upcomingWidget!).getByText('Shogun')).toBeInTheDocument();
            expect(within(upcomingWidget!).getByText('Fallout')).toBeInTheDocument();
        });

        it('has link to calendar page', async () => {
            await renderDashboard();

            const viewCalendarLink = screen.getByRole('link', { name: /view calendar/i });

            expect(viewCalendarLink).toHaveAttribute('href', '/calendar');
        });
    });

    describe('Extension Slot', () => {
        it('renders the extension widgets slot', async () => {
            await renderDashboard();

            const extensionSlot = screen.getByTestId('dashboard-page__extension-widgets');
            expect(extensionSlot).toBeInTheDocument();
        });
    });

    describe('Loading State', () => {
        it('shows skeleton placeholders for all widgets when loading', () => {
            setLoadingMocks();
            const queryClient = createTestQueryClient();

            render(
                <QueryClientProvider client={queryClient}>
                    <AuthProvider
                        initialUser={adminUser}
                        skipSessionInit
                        initialPermissions={testEffectivePermissions()}
                    >
                        <MemoryRouter>
                            <DashboardPage />
                        </MemoryRouter>
                    </AuthProvider>
                </QueryClientProvider>,
            );

            const healthSkeletons = screen.getAllByTestId('service-health-bar__segment--skeleton');
            expect(healthSkeletons.length).toBe(3);

            const downloadSkeletons = document.querySelectorAll(
                '[data-component="download-queue-widget"] .status-row [data-component="skeleton"]',
            );
            expect(downloadSkeletons.length).toBe(4);

            const jobSkeletons = screen.getAllByTestId('recent-jobs-widget__item--skeleton');
            expect(jobSkeletons.length).toBe(3);
        });

        it('does not show service names when loading', () => {
            setLoadingMocks();
            const queryClient = createTestQueryClient();

            render(
                <QueryClientProvider client={queryClient}>
                    <AuthProvider
                        initialUser={adminUser}
                        skipSessionInit
                        initialPermissions={testEffectivePermissions()}
                    >
                        <MemoryRouter>
                            <DashboardPage />
                        </MemoryRouter>
                    </AuthProvider>
                </QueryClientProvider>,
            );

            const healthBar = document.querySelector('[data-component="service-health-bar"]')!;
            expect(within(healthBar as HTMLElement).queryByText('Download Clients')).not.toBeInTheDocument();
            expect(within(healthBar as HTMLElement).queryByText('Media Servers')).not.toBeInTheDocument();
        });
    });

    describe('Onboarding checklist', () => {
        it('keeps the checklist and the resolved steps when one probe fails', async () => {
            // Arrange
            mockOnboardingStatus.mockReturnValue({
                status: partialOnboarding,
                steps: deriveOnboardingSteps(partialOnboarding, 'Magpie').map((step) => ({
                    ...step,
                    failed: step.key === 'indexer',
                })),
                isComplete: false,
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            // Act
            await renderDashboard();

            // Assert
            const checklist = document.querySelector('[data-component="onboarding-checklist"]') as HTMLElement;
            expect(checklist).toBeInTheDocument();
            expect(
                within(checklist).getByText('Magpie could not check this step. It may already be done.'),
            ).toBeInTheDocument();
            expect(checklist.querySelector('[data-step="indexer"]')).toHaveAttribute('data-state', 'unknown');
            expect(checklist.querySelector('[data-step="downloadClient"]')).toHaveAttribute('data-state', 'done');
            expect(checklist.querySelector('[data-step="rootFolder"]')).toHaveAttribute('data-state', 'done');
            expect(checklist.querySelector('[data-step="monitoredContent"]')).toHaveAttribute('data-state', 'todo');
            expect(within(checklist).getByLabelText('2 of 4 steps complete')).toBeInTheDocument();
        });

        it('hides the checklist when every probe fails', async () => {
            // Arrange
            mockOnboardingStatus.mockReturnValue({
                status: partialOnboarding,
                steps: deriveOnboardingSteps(partialOnboarding, 'Magpie').map((step) => ({ ...step, failed: true })),
                isComplete: false,
                isLoading: false,
                error: new Error('Host unreachable'),
                refetch: vi.fn(),
            });

            // Act
            await renderDashboard();

            // Assert
            expect(document.querySelector('[data-component="onboarding-checklist"]')).not.toBeInTheDocument();
        });
    });

    describe('Accessibility', () => {
        it('has properly labeled sections', async () => {
            await renderDashboard();

            expect(screen.getByRole('region', { name: /dashboard widgets/i })).toBeInTheDocument();
            expect(screen.getByRole('region', { name: /content overview/i })).toBeInTheDocument();
        });

        it('has icon marked as decorative', async () => {
            await renderDashboard();

            const icon = screen.getByTestId('dashboard-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });
});
