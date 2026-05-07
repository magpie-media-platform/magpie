import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter, useSearchParams } from 'react-router-dom';

import type { LogEntry } from './LogViewer';
import { LogViewer } from './LogViewer';

let currentSearchParams: URLSearchParams = new URLSearchParams();
function SearchParamsSpy() {
    const [params] = useSearchParams();
    currentSearchParams = params;
    return null;
}

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const now = Date.now();
const mockLogs: LogEntry[] = [
    {
        id: 'log-1',
        timestamp: new Date(now - 5000).toISOString(),
        level: 'info',
        source: 'Magpie.Core.Scheduler',
        message: 'Starting scheduled job: RefreshMetadataJob',
    },
    {
        id: 'log-2',
        timestamp: new Date(now - 10000).toISOString(),
        level: 'debug',
        source: 'Magpie.Core.Scheduler',
        message: 'Job queue depth: 3 pending, 1 running',
    },
    {
        id: 'log-3',
        timestamp: new Date(now - 15000).toISOString(),
        level: 'info',
        source: 'Magpie.Extensions.Sonarr',
        message: 'Syncing with Sonarr API at http://localhost:8989',
    },
    {
        id: 'log-4',
        timestamp: new Date(now - 20000).toISOString(),
        level: 'warn',
        source: 'Magpie.Core.Indexers',
        message: 'Indexer "NZBgeek" returned slow response (3.2s)',
    },
    {
        id: 'log-5',
        timestamp: new Date(now - 25000).toISOString(),
        level: 'error',
        source: 'Magpie.Core.DownloadClients',
        message: 'Failed to connect to qBittorrent at http://localhost:8080',
        exception: 'System.Net.Http.HttpRequestException: Connection refused',
    },
    {
        id: 'log-6',
        timestamp: new Date(now - 30000).toISOString(),
        level: 'info',
        source: 'Magpie.Api.Auth',
        message: 'User "admin" authenticated successfully',
    },
    {
        id: 'log-7',
        timestamp: new Date(now - 35000).toISOString(),
        level: 'debug',
        source: 'Magpie.Data.Database',
        message: 'Executing query: SELECT * FROM Content WHERE MonitoredStatus = 1',
    },
    {
        id: 'log-8',
        timestamp: new Date(now - 40000).toISOString(),
        level: 'info',
        source: 'Magpie.Core.MediaServers',
        message: 'Plex library scan completed: 1,234 items indexed',
    },
    {
        id: 'log-9',
        timestamp: new Date(now - 45000).toISOString(),
        level: 'warn',
        source: 'Magpie.Core.Scheduler',
        message: 'Job "SearchMissingJob" exceeded timeout (300s)',
    },
    {
        id: 'log-10',
        timestamp: new Date(now - 50000).toISOString(),
        level: 'debug',
        source: 'Magpie.Extensions.TMDb',
        message: 'Fetching metadata for tmdb:12345',
    },
    {
        id: 'log-11',
        timestamp: new Date(now - 55000).toISOString(),
        level: 'error',
        source: 'Magpie.Core.Indexers',
        message: 'Indexer "Prowlarr" returned HTTP 503',
        exception:
            'Magpie.Core.Exceptions.IndexerException: Service Unavailable\n   at Magpie.Core.Indexers.IndexerClient.SearchAsync()',
    },
    {
        id: 'log-12',
        timestamp: new Date(now - 60000).toISOString(),
        level: 'info',
        source: 'Magpie.Api.Startup',
        message: 'Application started on port 5000',
    },
];

const mockUseLogs = vi.fn();

vi.mock('@/hooks/useLogs', () => ({
    useLogs: (...args: unknown[]) => mockUseLogs(...args),
}));

function renderLogViewer(initialEntries: string[] = ['/']) {
    return render(
        <MemoryRouter initialEntries={initialEntries}>
            <LogViewer />
        </MemoryRouter>,
    );
}

function renderLogViewerWithPersist(initialEntries: string[] = ['/']) {
    return render(
        <MemoryRouter initialEntries={initialEntries}>
            <SearchParamsSpy />
            <LogViewer persistFilters />
        </MemoryRouter>,
    );
}

describe('LogViewer', () => {
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
        mockUseLogs.mockReturnValue({
            logs: mockLogs,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
    });

    it('renders the log viewer with correct data-component attribute', () => {
        renderLogViewer();

        expect(document.querySelector('[data-component="log-viewer"]')).toBeInTheDocument();
    });

    it('renders the title', () => {
        renderLogViewer();

        expect(screen.getByRole('heading', { name: 'System Logs', level: 2 })).toBeInTheDocument();
    });

    describe('Level Filters', () => {
        it('renders all four level toggles', () => {
            renderLogViewer();

            expect(screen.getByLabelText('Show Debug logs')).toBeInTheDocument();
            expect(screen.getByLabelText('Show Info logs')).toBeInTheDocument();
            expect(screen.getByLabelText('Show Warn logs')).toBeInTheDocument();
            expect(screen.getByLabelText('Show Error logs')).toBeInTheDocument();
        });

        it('all level toggles are checked by default', () => {
            renderLogViewer();

            expect(screen.getByLabelText('Show Debug logs')).toBeChecked();
            expect(screen.getByLabelText('Show Info logs')).toBeChecked();
            expect(screen.getByLabelText('Show Warn logs')).toBeChecked();
            expect(screen.getByLabelText('Show Error logs')).toBeChecked();
        });

        it('unchecking a level hides logs of that level', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const logsContainer = document.querySelector('[role="log"]') as HTMLElement;
            expect(within(logsContainer).getAllByText(/DEBUG/i).length).toBeGreaterThan(0);

            await user.click(screen.getByLabelText('Show Debug logs'));

            expect(within(logsContainer).queryAllByText(/DEBUG/i).length).toBe(0);
        });

        it('checking a level shows logs of that level', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const logsContainer = document.querySelector('[role="log"]') as HTMLElement;

            await user.click(screen.getByLabelText('Show Info logs'));
            expect(within(logsContainer).queryAllByText(/INFO/i).length).toBe(0);

            await user.click(screen.getByLabelText('Show Info logs'));
            expect(within(logsContainer).getAllByText(/INFO/i).length).toBeGreaterThan(0);
        });
    });

    describe('Search Filter', () => {
        it('renders search input', () => {
            renderLogViewer();

            expect(screen.getByLabelText('Search log messages')).toBeInTheDocument();
        });

        it('filters logs by search term', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const searchInput = screen.getByLabelText('Search log messages');
            await user.type(searchInput, 'qBittorrent');

            await waitFor(() => {
                const logsContainer = document.querySelector('[role="log"]') as HTMLElement;
                const entries = logsContainer.querySelectorAll('[data-log-id]');
                expect(entries.length).toBeGreaterThan(0);

                entries.forEach((entry) => {
                    expect(entry.textContent?.toLowerCase()).toContain('qbittorrent');
                });
            });
        });

        it('shows clear button when search has value', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const searchInput = screen.getByLabelText('Search log messages');
            await user.type(searchInput, 'test');

            expect(screen.getByLabelText('Clear search')).toBeInTheDocument();
        });

        it('clears search when clear button is clicked', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const searchInput = screen.getByLabelText('Search log messages');
            await user.type(searchInput, 'test');

            await user.click(screen.getByLabelText('Clear search'));

            expect(searchInput).toHaveValue('');
        });
    });

    describe('Source Filter', () => {
        it('renders source filter dropdown', () => {
            renderLogViewer();

            expect(screen.getByLabelText('Filter by source')).toBeInTheDocument();
        });

        it('has All Sources option', () => {
            renderLogViewer();

            const sourceSelect = screen.getByLabelText('Filter by source');
            expect(within(sourceSelect).getByText('All Sources')).toBeInTheDocument();
        });
    });

    describe('Live Tail', () => {
        it('renders live tail button', () => {
            renderLogViewer();

            expect(screen.getByLabelText('Start live tail')).toBeInTheDocument();
        });

        it('toggles live tail state when clicked', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const liveButton = screen.getByLabelText('Start live tail');
            await user.click(liveButton);

            expect(screen.getByLabelText('Pause live tail')).toBeInTheDocument();
        });
    });

    describe('Log Entries', () => {
        it('renders log entries with correct structure', () => {
            renderLogViewer();

            const logsContainer = document.querySelector('[role="log"]') as HTMLElement;
            const entries = logsContainer.querySelectorAll('[data-log-id]');

            expect(entries.length).toBeGreaterThan(0);
        });

        it('entries have level data attribute', () => {
            renderLogViewer();

            const logsContainer = document.querySelector('[role="log"]') as HTMLElement;
            const entry = logsContainer.querySelector('[data-log-id]');

            expect(entry).toHaveAttribute('data-level');
        });

        it('shows copy button on hover', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const logsContainer = document.querySelector('[role="log"]') as HTMLElement;
            const entry = logsContainer.querySelector('[data-log-id]') as HTMLElement;

            await user.hover(entry);

            expect(within(entry).getByLabelText('Copy log entry')).toBeInTheDocument();
        });
    });

    describe('Exception Details', () => {
        it('shows expand button for entries with exceptions', () => {
            renderLogViewer();

            const errorEntries = document.querySelectorAll('[data-level="error"]');
            const entryWithException = Array.from(errorEntries).find((entry) =>
                entry.querySelector('[data-action="toggle-exception"]'),
            );

            expect(entryWithException).toBeDefined();
        });

        it('expands exception details when button is clicked', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            const errorEntries = document.querySelectorAll('[data-level="error"]');
            const entryWithException = Array.from(errorEntries).find((entry) =>
                entry.querySelector('[data-action="toggle-exception"]'),
            ) as HTMLElement;

            if (entryWithException) {
                const expandButton = within(entryWithException).getByLabelText('Show exception');
                await user.click(expandButton);

                expect(entryWithException.querySelector('.log-viewer__entry-exception')).toBeInTheDocument();
            }
        });
    });

    describe('Copy Cleanup', () => {
        it('clears copy timeout on unmount', async () => {
            const clearTimeoutSpy = vi.spyOn(globalThis, 'clearTimeout');

            const originalClipboard = navigator.clipboard;
            Object.defineProperty(navigator, 'clipboard', {
                value: { writeText: vi.fn().mockResolvedValue(undefined) },
                writable: true,
                configurable: true,
            });

            const user = userEvent.setup();
            const { unmount } = renderLogViewer();

            const logsContainer = document.querySelector('[role="log"]') as HTMLElement;
            const copyBtn = logsContainer.querySelector('[data-action="copy"]') as HTMLElement;

            await user.click(copyBtn);

            await waitFor(() => {
                const copiedBtn = logsContainer.querySelector(
                    '[data-action="copy"] .log-viewer__entry-btn--copied, [aria-label="Copied!"]',
                );
                expect(copiedBtn ?? logsContainer.querySelector('[data-action="copy"]')).toBeInTheDocument();
            });

            unmount();

            expect(clearTimeoutSpy).toHaveBeenCalled();

            clearTimeoutSpy.mockRestore();
            Object.defineProperty(navigator, 'clipboard', {
                value: originalClipboard,
                writable: true,
                configurable: true,
            });
        });
    });

    describe('Clear Filters', () => {
        it('shows clear filters button when filters are active', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            await user.click(screen.getByLabelText('Show Debug logs'));

            expect(screen.getByLabelText('Clear all filters')).toBeInTheDocument();
        });

        it('resets all filters when clear button is clicked', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            await user.click(screen.getByLabelText('Show Debug logs'));
            const searchInput = screen.getByLabelText('Search log messages');
            await user.type(searchInput, 'test');

            await user.click(screen.getByLabelText('Clear all filters'));

            expect(screen.getByLabelText('Show Debug logs')).toBeChecked();
            expect(searchInput).toHaveValue('');
        });
    });

    describe('Accessibility', () => {
        it('has proper ARIA attributes on log container', () => {
            renderLogViewer();

            const logsContainer = document.querySelector('[role="log"]');
            expect(logsContainer).toHaveAttribute('aria-label', 'System log entries');
        });

        it('level toggles have accessible labels', () => {
            renderLogViewer();

            const debugCheckbox = screen.getByLabelText('Show Debug logs');
            expect(debugCheckbox).toBeInTheDocument();
        });

        it('action buttons have accessible labels', () => {
            renderLogViewer();

            expect(screen.getByLabelText('Start live tail')).toBeInTheDocument();
            expect(screen.getByLabelText('Scroll to bottom')).toBeInTheDocument();
        });
    });

    describe('Loading State', () => {
        it('shows loading skeletons when loading prop is true', () => {
            render(
                <MemoryRouter>
                    <LogViewer loading />
                </MemoryRouter>,
            );

            const skeletons = document.querySelectorAll('.log-viewer__skeleton-row');
            expect(skeletons.length).toBeGreaterThan(0);
        });

        it('shows loading skeletons when useLogs is loading with no data', () => {
            mockUseLogs.mockReturnValue({
                logs: [],
                isLoading: true,
                error: null,
                refetch: vi.fn(),
            });

            render(
                <MemoryRouter>
                    <LogViewer />
                </MemoryRouter>,
            );

            const skeletons = document.querySelectorAll('.log-viewer__skeleton-row');
            expect(skeletons.length).toBeGreaterThan(0);
        });
    });

    describe('Empty State', () => {
        it('shows empty state message when no logs match filters', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            await user.click(screen.getByLabelText('Show Debug logs'));
            await user.click(screen.getByLabelText('Show Info logs'));
            await user.click(screen.getByLabelText('Show Warn logs'));
            await user.click(screen.getByLabelText('Show Error logs'));

            expect(screen.getByText('No log entries')).toBeInTheDocument();
            expect(screen.getByText('Try adjusting your filters')).toBeInTheDocument();
        });
    });

    describe('Persist Filters (search params)', () => {
        it('restores level filters from URL search params', () => {
            renderLogViewerWithPersist(['/?levels=error,warn']);

            expect(screen.getByLabelText('Show Error logs')).toBeChecked();
            expect(screen.getByLabelText('Show Warn logs')).toBeChecked();
            expect(screen.getByLabelText('Show Debug logs')).not.toBeChecked();
            expect(screen.getByLabelText('Show Info logs')).not.toBeChecked();
        });

        it('restores search term from URL search params', () => {
            renderLogViewerWithPersist(['/?search=qBittorrent']);

            expect(screen.getByLabelText('Search log messages')).toHaveValue('qBittorrent');
        });

        it('restores live tail from URL search params', () => {
            renderLogViewerWithPersist(['/?liveTail=true']);

            expect(screen.getByLabelText('Pause live tail')).toBeInTheDocument();
        });

        it('writes level filter changes to search params', async () => {
            const user = userEvent.setup();
            renderLogViewerWithPersist();

            await user.click(screen.getByLabelText('Show Debug logs'));

            await waitFor(() => {
                expect(currentSearchParams.get('levels')).toBeTruthy();
                expect(currentSearchParams.get('levels')).not.toContain('debug');
            });
        });

        it('writes search changes to search params', async () => {
            const user = userEvent.setup();
            renderLogViewerWithPersist();

            const searchInput = screen.getByLabelText('Search log messages');
            await user.type(searchInput, 'test');

            await waitFor(() => {
                expect(currentSearchParams.get('search')).toBe('test');
            });
        });

        it('writes live tail toggle to search params', async () => {
            const user = userEvent.setup();
            renderLogViewerWithPersist();

            await user.click(screen.getByLabelText('Start live tail'));

            await waitFor(() => {
                expect(currentSearchParams.get('liveTail')).toBe('true');
            });
        });

        it('clears search params when filters are reset', async () => {
            const user = userEvent.setup();
            renderLogViewerWithPersist(['/?levels=error&search=qBittorrent']);

            await user.click(screen.getByLabelText('Clear all filters'));

            await waitFor(() => {
                expect(currentSearchParams.get('levels')).toBeNull();
                expect(currentSearchParams.get('search')).toBeNull();
            });
        });
    });

    describe('useLogs integration', () => {
        it('passes liveTail state to useLogs', async () => {
            const user = userEvent.setup();
            renderLogViewer();

            expect(mockUseLogs).toHaveBeenCalledWith(false);

            await user.click(screen.getByLabelText('Start live tail'));

            expect(mockUseLogs).toHaveBeenCalledWith(true);
        });
    });
});
