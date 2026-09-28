import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { SystemStatusPage } from './SystemStatusPage';
import type { SystemStatusData } from '@/hooks/useSystemStatus';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async (importOriginal) => ({
    ...(await importOriginal<typeof import('react-router-dom')>()),
    useNavigate: () => mockNavigate,
}));

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockSystemStatusData: SystemStatusData = {
    systemInfo: {
        version: '0.1.0',
        runtime: '.NET 10.0.0',
        operatingSystem: 'Windows 11',
        architecture: 'x64',
        systemRoot: '/opt/magpie',
        nodeId: 'node-abc123',
        startedAt: '2024-01-01T12:00:00Z',
    },
    diskSpaceTotals: {
        usedBytes: 2.1 * 1024 * 1024 * 1024 * 1024,
        totalBytes: 4 * 1024 * 1024 * 1024 * 1024,
        freeBytes: 1.9 * 1024 * 1024 * 1024 * 1024,
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
    isLoading: false,
};

vi.mock('@/hooks/useSystemStatus', () => ({
    useSystemStatus: () => mockSystemStatusData,
}));

vi.mock('./RestartHostPanel', () => ({
    RestartHostPanel: () => <div data-component="restart-host-panel" />,
}));

vi.mock('./DiagnosticsSection', () => ({
    DiagnosticsSection: () => <div data-component="diagnostics-section" />,
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
                <MemoryRouter>
                    <ToastProvider>
                        <ToastStateProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </MemoryRouter>
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

    it('opens the selected root folder for editing when a folder row is clicked', async () => {
        // Arrange
        await renderSystemStatus();

        // Act
        fireEvent.click(screen.getByText('TV Shows'));

        // Assert
        expect(mockNavigate).toHaveBeenCalledWith('/admin/root-folders?edit=rf-1');
    });

    it('opens the unqualified root folder list from Manage root folders', async () => {
        // Arrange
        await renderSystemStatus();

        // Act
        fireEvent.click(screen.getByRole('button', { name: 'Manage root folders' }));

        // Assert
        expect(mockNavigate).toHaveBeenCalledWith('/admin/root-folders');
    });

    it('renders the system status page with correct data-page attribute', async () => {
        await renderSystemStatus();

        expect(document.querySelector('[data-page="system-status"]')).toBeInTheDocument();
    });

    it('renders the page title', async () => {
        await renderSystemStatus();

        expect(screen.getByRole('heading', { name: 'System Status', level: 1 })).toBeInTheDocument();
    });

    it('does not render an Integrations section', async () => {
        await renderSystemStatus();

        expect(screen.queryByRole('heading', { name: 'Integrations' })).not.toBeInTheDocument();
        expect(document.querySelectorAll('[data-component="health-card"]').length).toBe(0);
    });

    it('does not request the removed scheduler status endpoint', async () => {
        // Arrange
        const fetchSpy = vi.spyOn(globalThis, 'fetch');

        // Act
        await renderSystemStatus();

        // Assert
        const urls = fetchSpy.mock.calls.map(([input]) => String(input));
        expect(urls.filter((url) => url.includes('scheduler-status'))).toEqual([]);
        fetchSpy.mockRestore();
    });

    describe('Doctor Section', () => {
        it('describes the issues included in the system scan', async () => {
            await renderSystemStatus();

            const note = screen.getByText('Issues found by the system scan, including unhealthy integrations.');
            expect(note).toBeInTheDocument();
            expect(note.textContent).toMatch(/unhealthy integration/i);
        });

        it('brands the doctor surface as Magpie Doctor', async () => {
            await renderSystemStatus();

            expect(screen.getByRole('heading', { name: /Magpie Doctor/, level: 2 })).toBeInTheDocument();
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

            expect(screen.getByRole('region', { name: /system information/i })).toBeInTheDocument();
        });

        it('has icon marked as decorative', async () => {
            await renderSystemStatus();

            const icon = screen.getByTestId('system-status-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });
});
