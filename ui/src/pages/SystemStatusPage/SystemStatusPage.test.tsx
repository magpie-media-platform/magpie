import { render, screen, within, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { SystemStatusPage } from './SystemStatusPage';
import type { SystemStatusData } from '@/hooks/useSystemStatus';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockSystemStatusData: SystemStatusData = {
    services: {
        api: {
            name: 'API',
            status: 'healthy',
            message: 'All endpoints responding normally',
            lastCheckedAt: new Date().toISOString(),
            responseTime: 12,
        },
        database: {
            name: 'Database',
            status: 'healthy',
            message: 'Connection pool healthy',
            lastCheckedAt: new Date().toISOString(),
            responseTime: 3,
        },
        scheduler: {
            name: 'Scheduler',
            status: 'healthy',
            message: '5 jobs running, 12 queued',
            lastCheckedAt: new Date().toISOString(),
        },
    },
    integrations: {
        indexers: { healthy: 4, unhealthy: 1, unknown: 0, stale: 1 },
        downloadClients: { healthy: 2, unhealthy: 0, unknown: 1, stale: 0 },
        mediaServers: { healthy: 1, unhealthy: 0, unknown: 0, stale: 0 },
    },
    downloadQueue: {
        counts: { downloading: 3, queued: 7, stalled: 1 },
        activeDownloads: [
            {
                id: 'dl-1',
                title: 'Breaking Bad S01E01 (1080p BluRay)',
                progress: 67,
                state: 'downloading',
                size: '2.4 GB',
                eta: '12 min',
            },
            {
                id: 'dl-2',
                title: 'The Office S03E07 (720p WEB)',
                progress: 23,
                state: 'downloading',
                size: '850 MB',
                eta: '8 min',
            },
            {
                id: 'dl-3',
                title: 'Dune Part Two (2024) (2160p)',
                progress: 5,
                state: 'downloading',
                size: '18.2 GB',
                eta: '2h 15min',
            },
        ],
    },
    systemInfo: {
        version: '0.1.0',
        runtime: '.NET 10.0.0',
        operatingSystem: 'Windows 11',
        architecture: 'x64',
        systemRoot: '/opt/magpie',
        nodeId: 'node-abc123',
        startedAt: '2024-01-01T12:00:00Z',
    },
    diskSpace: [
        {
            id: 'rf-1',
            name: 'TV Shows',
            path: '/mnt/media/tv',
            usedBytes: 2.1 * 1024 * 1024 * 1024 * 1024,
            totalBytes: 4 * 1024 * 1024 * 1024 * 1024,
            freeBytes: 1.9 * 1024 * 1024 * 1024 * 1024,
        },
        {
            id: 'rf-2',
            name: 'Movies',
            path: '/mnt/media/movies',
            usedBytes: 3.5 * 1024 * 1024 * 1024 * 1024,
            totalBytes: 4 * 1024 * 1024 * 1024 * 1024,
            freeBytes: 0.5 * 1024 * 1024 * 1024 * 1024,
        },
    ],
    recentJobs: [],
    isLoading: false,
    error: null,
};

vi.mock('@/hooks/useSystemStatus', () => ({
    useSystemStatus: () => mockSystemStatusData,
}));

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

    return function Wrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>{children}</MemoryRouter>
            </QueryClientProvider>
        );
    };
}

async function renderSystemStatus() {
    const result = render(<SystemStatusPage />, { wrapper: createWrapper() });

    await waitFor(() => {
        expect(screen.getByText('0.1.0')).toBeInTheDocument();
    });

    return result;
}

describe('SystemStatusPage', () => {
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
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
    });

    it('renders the system status page with correct data-page attribute', async () => {
        await renderSystemStatus();

        expect(document.querySelector('[data-page="system-status"]')).toBeInTheDocument();
    });

    it('renders the page title', async () => {
        await renderSystemStatus();

        expect(screen.getByRole('heading', { name: 'System Status', level: 1 })).toBeInTheDocument();
    });

    describe('Core Services Section', () => {
        it('renders section heading', async () => {
            await renderSystemStatus();

            expect(screen.getByRole('heading', { name: 'Core Services', level: 2 })).toBeInTheDocument();
        });

        it('renders three service cards', async () => {
            await renderSystemStatus();

            expect(screen.getByRole('heading', { name: 'API', level: 3 })).toBeInTheDocument();
            expect(screen.getByRole('heading', { name: 'Database', level: 3 })).toBeInTheDocument();
            expect(screen.getByRole('heading', { name: 'Scheduler', level: 3 })).toBeInTheDocument();
        });

        it('displays service status messages', async () => {
            await renderSystemStatus();

            expect(screen.getByText('All endpoints responding normally')).toBeInTheDocument();
            expect(screen.getByText('Connection pool healthy')).toBeInTheDocument();
            expect(screen.getByText('5 jobs running, 12 queued')).toBeInTheDocument();
        });

        it('displays response time for services that have it', async () => {
            await renderSystemStatus();

            expect(screen.getByText('12ms')).toBeInTheDocument();
            expect(screen.getByText('3ms')).toBeInTheDocument();
        });

        it('displays health badges for services', async () => {
            await renderSystemStatus();

            const healthyBadges = screen.getAllByText('Healthy');
            expect(healthyBadges.length).toBeGreaterThanOrEqual(3);
        });
    });

    describe('Integrations Section', () => {
        it('renders section heading', async () => {
            await renderSystemStatus();

            expect(screen.getByRole('heading', { name: 'Integrations', level: 2 })).toBeInTheDocument();
        });

        it('renders three health cards', async () => {
            await renderSystemStatus();

            const healthCards = document.querySelectorAll('[data-component="health-card"]');
            expect(healthCards.length).toBe(3);

            const cardTitles = Array.from(healthCards).map(
                (card) => card.querySelector('.health-card__title')?.textContent,
            );
            expect(cardTitles).toContain('Indexers');
            expect(cardTitles).toContain('Download Clients');
            expect(cardTitles).toContain('Media Servers');
        });

        it('has link to indexers page for status', async () => {
            await renderSystemStatus();

            const healthCards = document.querySelectorAll('[data-component="health-card"]');
            const indexerCard = Array.from(healthCards).find(
                (card) => card.querySelector('.health-card__title')?.textContent === 'Indexers',
            );

            const healthyLink = within(indexerCard as HTMLElement).getByRole('link', {
                name: /view 4 healthy items/i,
            });

            expect(healthyLink).toHaveAttribute('href', '/indexers?status=healthy');
        });

        it('has link to download clients page for status', async () => {
            await renderSystemStatus();

            const healthCards = document.querySelectorAll('[data-component="health-card"]');
            const clientCard = Array.from(healthCards).find(
                (card) => card.querySelector('.health-card__title')?.textContent === 'Download Clients',
            );

            const healthyLink = within(clientCard as HTMLElement).getByRole('link', {
                name: /view 2 healthy items/i,
            });

            expect(healthyLink).toHaveAttribute('href', '/download-clients?status=healthy');
        });

        it('has link to media servers page for status', async () => {
            await renderSystemStatus();

            const healthCards = document.querySelectorAll('[data-component="health-card"]');
            const serverCard = Array.from(healthCards).find(
                (card) => card.querySelector('.health-card__title')?.textContent === 'Media Servers',
            );

            const healthyLink = within(serverCard as HTMLElement).getByRole('link', {
                name: /view 1 healthy items/i,
            });

            expect(healthyLink).toHaveAttribute('href', '/admin/media-servers?status=healthy');
        });
    });

    describe('Download Queue Section', () => {
        it('renders section heading', async () => {
            await renderSystemStatus();

            expect(screen.getByRole('heading', { name: 'Download Queue', level: 2 })).toBeInTheDocument();
        });

        it('renders download queue widget', async () => {
            await renderSystemStatus();

            const queueWidget = document.querySelector('[data-component="download-queue-widget"]');
            expect(queueWidget).toBeInTheDocument();
        });

        it('displays download state counts', async () => {
            await renderSystemStatus();

            const downloadingRow = document.querySelector('[data-status="downloading"]');
            const queuedRow = document.querySelector('[data-status="queued"]');
            const stalledRow = document.querySelector('[data-status="stalled"]');

            expect(downloadingRow).toBeInTheDocument();
            expect(queuedRow).toBeInTheDocument();
            expect(stalledRow).toBeInTheDocument();
        });

        it('displays active downloads with progress bars', async () => {
            await renderSystemStatus();

            const progressBars = screen.getAllByRole('progressbar');
            expect(progressBars.length).toBeGreaterThanOrEqual(1);
        });

        it('has link to downloads page for status', async () => {
            await renderSystemStatus();

            const downloadingLinks = screen.getAllByRole('link', {
                name: /view 3 downloading items/i,
            });

            const downloadingLink = downloadingLinks[downloadingLinks.length - 1];

            expect(downloadingLink).toHaveAttribute('href', '/downloads?state=downloading');
        });

        it('has link to downloads page for view all', async () => {
            await renderSystemStatus();

            const viewAllLinks = screen.getAllByRole('link', { name: /view all downloads/i });
            const viewAllLink = viewAllLinks[viewAllLinks.length - 1];

            expect(viewAllLink).toHaveAttribute('href', '/downloads');
        });

        it('has link to specific download for item', async () => {
            await renderSystemStatus();

            const downloadItem = document.querySelector('[data-download-id="dl-1"]');
            expect(downloadItem).toBeInTheDocument();

            expect(downloadItem).toHaveAttribute('href', '/downloads/dl-1');
        });
    });

    describe('System Information Section', () => {
        it('renders section heading', async () => {
            await renderSystemStatus();

            expect(screen.getByRole('heading', { name: 'System Information', level: 2 })).toBeInTheDocument();
        });

        it('displays version information', async () => {
            await renderSystemStatus();

            expect(screen.getByText('Version')).toBeInTheDocument();
            expect(screen.getByText('0.1.0')).toBeInTheDocument();
        });

        it('displays runtime information', async () => {
            await renderSystemStatus();

            expect(screen.getByText('Runtime')).toBeInTheDocument();
            expect(screen.getByText('.NET 10.0.0')).toBeInTheDocument();
        });

        it('displays operating system information', async () => {
            await renderSystemStatus();

            expect(screen.getByText('Operating System')).toBeInTheDocument();
            expect(screen.getByText('Windows 11')).toBeInTheDocument();
        });

        it('displays architecture information', async () => {
            await renderSystemStatus();

            expect(screen.getByText('Architecture')).toBeInTheDocument();
            expect(screen.getByText('x64')).toBeInTheDocument();
        });

        it('displays system root information', async () => {
            await renderSystemStatus();

            expect(screen.getByText('System Root')).toBeInTheDocument();
            expect(screen.getByText('/opt/magpie')).toBeInTheDocument();
        });

        it('displays node ID information', async () => {
            await renderSystemStatus();

            expect(screen.getByText('Node ID')).toBeInTheDocument();
            expect(screen.getByText('node-abc123')).toBeInTheDocument();
        });

        it('displays uptime information', async () => {
            await renderSystemStatus();

            expect(screen.getByText('Uptime')).toBeInTheDocument();

            const uptimeLabel = screen.getByText('Uptime');
            const uptimeItem = uptimeLabel.closest('.system-info__item');
            const uptimeValue = uptimeItem?.querySelector('.system-info__value');
            expect(uptimeValue).toBeTruthy();
            expect(uptimeValue?.textContent).not.toBe('-');
        });
    });

    describe('Accessibility', () => {
        it('has properly labeled sections', async () => {
            await renderSystemStatus();

            expect(screen.getByRole('region', { name: /core services/i })).toBeInTheDocument();
            expect(screen.getByRole('region', { name: /integrations/i })).toBeInTheDocument();
            expect(screen.getByRole('region', { name: /download queue/i })).toBeInTheDocument();
            expect(screen.getByRole('region', { name: /system information/i })).toBeInTheDocument();
        });

        it('has icon marked as decorative', async () => {
            await renderSystemStatus();

            const icon = screen.getByTestId('system-status-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });

        it('service cards have data-status attribute', async () => {
            await renderSystemStatus();

            const serviceCards = document.querySelectorAll('[data-component="service-card"]');
            expect(serviceCards.length).toBe(3);

            serviceCards.forEach((card) => {
                expect(card).toHaveAttribute('data-status');
            });
        });
    });
});
