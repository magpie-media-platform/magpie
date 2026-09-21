import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

import { ActivityTab } from './ActivityTab';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';

const correlationId = '11111111-1111-1111-1111-111111111111';

const mockActivity = {
    items: [
        {
            id: 'job-1',
            kind: 'JobExecution',
            timestamp: '2024-05-01T10:00:00Z',
            correlationId,
            label: 'ContentMetadataRefreshItem',
            category: 'Metadata',
            status: 'Failed',
            detail: 'Provider A 404',
        },
        {
            id: 'job-acq-1',
            kind: 'JobExecution',
            timestamp: '2024-05-01T11:00:00Z',
            correlationId,
            label: 'ReleaseDownloadItem',
            category: 'Content',
            status: 'Failed',
            detail: 'No enabled download client is configured.',
        },
        {
            id: 'audit-1',
            kind: 'AuditRecord',
            timestamp: '2024-05-01T09:00:00Z',
            correlationId,
            label: 'GrabSkipped',
            category: 'Content',
            status: null,
            detail: 'No acceptable release.',
            entityId: 'content-1',
            entityType: 'ContentHierarchyItem',
        },
    ],
    totalItems: 3,
    pageNumber: 1,
    pageSize: 25,
};

const mockTree = {
    root: {
        id: 'job-1',
        correlationId,
        jobType: 'ContentMetadataRefreshItem',
        status: 'Failed',
        createdAt: '2024-05-01T10:00:00Z',
        completedAt: '2024-05-01T10:00:00Z',
        children: [],
    },
    totalCount: 1,
};

function setupApi() {
    vi.mocked(apiGet).mockImplementation((url: string) => {
        if (url.startsWith('/audit-history')) {
            return Promise.resolve(mockActivity);
        }
        if (url.startsWith('/jobs/')) {
            return Promise.resolve(mockTree);
        }
        return Promise.resolve(null);
    });
}

beforeEach(() => {
    vi.clearAllMocks();
    setupApi();
});

afterEach(() => {
    vi.restoreAllMocks();
});

function LocationProbe() {
    const { search } = useLocation();
    return <output data-testid="activity-tab__location-search">{search}</output>;
}

function TestWrapper({ children, initialEntry = '/' }: { children: ReactNode; initialEntry?: string }) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    return (
        <MemoryRouter initialEntries={[initialEntry]}>
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <ToastProvider>
                        <ToastStateProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                            <LocationProbe />
                        </ToastStateProvider>
                    </ToastProvider>
                </TooltipProvider>
            </QueryClientProvider>
        </MemoryRouter>
    );
}

function renderTab(initialEntry = '/') {
    return render(
        <TestWrapper initialEntry={initialEntry}>
            <ActivityTab contentHierarchyItemId="content-1" />
        </TestWrapper>,
    );
}

describe('ActivityTab', () => {
    it('renders the activity entries as a regular table fetched from audit history', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });
        expect(screen.getAllByText('Failed').length).toBeGreaterThan(0);
        expect(screen.getByText('Grab Skipped')).toBeInTheDocument();
    });

    it('renders an entry with no detail as a quiet placeholder, not "No detail" filler text', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({
                    items: [
                        {
                            id: 'job-autograb-1',
                            kind: 'JobExecution',
                            timestamp: '2024-05-01T12:00:00Z',
                            correlationId,
                            label: 'ReleaseAutoGrabItem',
                            category: 'Content',
                            status: 'Succeeded',
                            detail: null,
                        },
                    ],
                    totalItems: 1,
                    pageNumber: 1,
                    pageSize: 25,
                });
            }
            return Promise.resolve(null);
        });

        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        expect(screen.queryByText('No detail')).not.toBeInTheDocument();
        const emptyDetail = document.querySelector('.activity-tab__detail--empty');
        expect(emptyDetail).toBeInTheDocument();
    });

    it('is a clean chronological log with no event-type or skipped-grabs filters', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        expect(screen.queryByLabelText('Filter by event type')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /skipped grabs/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /clear filters/i })).not.toBeInTheDocument();

        const activityCalls = vi
            .mocked(apiGet)
            .mock.calls.map((c) => c[0] as string)
            .filter((u) => u.startsWith('/audit-history'));
        expect(activityCalls.length).toBeGreaterThan(0);
        expect(activityCalls.every((u) => !u.includes('status=') && !u.includes('operation='))).toBe(true);
    });

    it('renders Activity, Status, Type, Detail, and Timestamp columns plus a Detail button', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        expect(screen.getByRole('columnheader', { name: 'Activity' })).toBeInTheDocument();
        expect(screen.getByRole('columnheader', { name: 'Status' })).toBeInTheDocument();
        expect(screen.getByRole('columnheader', { name: 'Type' })).toBeInTheDocument();
        expect(screen.getByRole('columnheader', { name: 'Timestamp' })).toBeInTheDocument();
        const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
        expect(headers.some((t) => t === 'Detail')).toBe(true);
        expect(screen.getAllByRole('button', { name: /^detail$/i }).length).toBeGreaterThan(0);
    });

    it('preserves server order and offers no page-local sort controls', async () => {
        // Arrange
        renderTab('/?sort=timestamp:asc');
        // Act
        await screen.findByTestId('activity-tab__table');
        // Assert
        expect(screen.queryByRole('combobox', { name: 'Sort activity by' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /sort by/i })).not.toBeInTheDocument();
        expect(
            Array.from(document.querySelectorAll('[data-testid="activity-tab__table"] tbody tr')).map((row) =>
                row.getAttribute('data-row-id'),
            ),
        ).toEqual(['JobExecution-job-1', 'JobExecution-job-acq-1', 'AuditRecord-audit-1']);
    });

    it('fetches the last page when the URL page exceeds the total', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue({ ...mockActivity, totalItems: 40 });
        // Act
        renderTab('/?page=99');
        // Assert
        await waitFor(() =>
            expect(apiGet).toHaveBeenCalledWith('/audit-history?contentId=content-1&pageNumber=2&pageSize=25'),
        );
        expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument();
    });

    it.each(['0', '-1', '1.5', 'invalid'])('loads page one for invalid page %s', async (page) => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue(mockActivity);
        // Act
        renderTab(`/?page=${page}`);
        // Assert
        await waitFor(() =>
            expect(apiGet).toHaveBeenCalledWith('/audit-history?contentId=content-1&pageNumber=1&pageSize=25'),
        );
    });

    it('retries an activity load failure', async () => {
        // Arrange
        const user = userEvent.setup();
        vi.mocked(apiGet).mockRejectedValueOnce(new Error('Activity unavailable'));
        renderTab();
        // Act
        await user.click(await screen.findByRole('button', { name: 'Retry' }));
        // Assert
        expect(await screen.findByText('Grab Skipped')).toBeInTheDocument();
        expect(screen.queryByText('Activity unavailable')).not.toBeInTheDocument();
    });

    it('shows each row detail text clamped to keep rows compact', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        const detailCells = Array.from(document.querySelectorAll('.activity-tab__detail'));
        expect(detailCells.length).toBeGreaterThan(0);
        const refreshDetail = detailCells.find((c) => c.textContent === 'Provider A 404');
        expect(refreshDetail).toBeDefined();
        expect(refreshDetail).toHaveClass('line-clamp-2');
        expect(refreshDetail).toHaveAttribute('title', 'Provider A 404');
    });

    it('makes Status values meaningful and never shows a literal "event" or "audit" pill', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        expect(screen.queryByText(/^event$/i)).not.toBeInTheDocument();
        expect(screen.queryByText(/^audit$/i)).not.toBeInTheDocument();
        expect(screen.getByText('Grab Skipped')).toBeInTheDocument();
    });

    it('opens Details first and loads the related activity only after its tab is selected', async () => {
        const user = userEvent.setup();
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        const refreshRow = screen.getAllByRole('row').find((r) => r.textContent?.includes('Provider A 404'));
        expect(refreshRow).toBeDefined();
        await user.click(within(refreshRow!).getByRole('button', { name: /^detail$/i }));

        const dialog = await screen.findByRole('dialog');
        const self = within(dialog).getByTestId('related-events__self');
        expect(self).toHaveTextContent('Provider A 404');
        expect(within(dialog).queryByTestId('related-events__chain')).not.toBeInTheDocument();

        await user.click(within(dialog).getByRole('tab', { name: 'Related activity' }));
        await waitFor(() => {
            expect(within(dialog).getByTestId('related-events__chain')).toBeInTheDocument();
        });
        const steps = within(dialog).getAllByRole('listitem');
        expect(steps.some((step) => step.textContent?.includes('Grab Skipped'))).toBe(true);
        expect(steps.some((step) => step.textContent?.includes('Content Metadata Refresh Item'))).toBe(true);

        const selfStep = within(dialog).getByTestId('related-events__self-step');
        expect(selfStep).toHaveTextContent('Selected activity');
        expect(selfStep).toHaveTextContent('Content Metadata Refresh Item');
    });

    it('still shows and highlights the selected activity when the execution tree does not include it', async () => {
        const user = userEvent.setup();
        const unrelated = Array.from({ length: 20 }).map((_, i) => ({
            id: `create-${i}`,
            correlationId,
            jobType: 'Create',
            status: 'Completed',
            createdAt: `2024-04-30T08:${String(i).padStart(2, '0')}:00Z`,
            children: [],
        }));
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve(mockActivity);
            }
            if (url.startsWith('/jobs/')) {
                return Promise.resolve({ root: { ...unrelated[0], children: unrelated.slice(1) }, totalCount: 20 });
            }
            return Promise.resolve(null);
        });

        renderTab();
        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        const refreshRow = screen.getAllByRole('row').find((r) => r.textContent?.includes('Provider A 404'));
        expect(refreshRow).toBeDefined();
        await user.click(within(refreshRow!).getByRole('button', { name: /^detail$/i }));

        const dialog = await screen.findByRole('dialog');
        await user.click(within(dialog).getByRole('tab', { name: 'Related activity' }));
        await waitFor(() => {
            expect(within(dialog).getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const also = within(dialog).getByTestId('related-events__also');
        const selfStep = within(also).getByTestId('related-events__self-step');
        expect(selfStep).toHaveTextContent('Selected activity');
        expect(selfStep).toHaveTextContent('Content Metadata Refresh Item');
        expect(within(dialog).getAllByTestId('related-events__self-step')).toHaveLength(1);
    });

    it('wires the Detail button to the row correlation as a design-system button', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        const detailButtons = screen.getAllByRole('button', { name: /^detail$/i });
        detailButtons.forEach((button) => {
            expect(button).toHaveAttribute('data-component', 'button');
            expect(button).toHaveAttribute('data-variant', 'secondary');
            expect(button).toHaveAttribute('data-size', 'sm');
            expect(button).toHaveAttribute('data-action', 'view-detail');
            expect(button).toHaveAttribute('data-correlation-id', correlationId);
        });
    });

    it('never shows the legacy capped notice -- all activity is reachable through the pager', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({ ...mockActivity, capped: true });
            }
            if (url.startsWith('/jobs/')) {
                return Promise.resolve(mockTree);
            }
            return Promise.resolve(null);
        });

        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });
        expect(screen.queryByTestId('activity-capped')).not.toBeInTheDocument();
        expect(screen.queryByText(/most recent activity/i)).not.toBeInTheDocument();
    });

    it('renders an absolute timestamp for each row, not a relative "Just now"', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        const cells = document.querySelectorAll('.activity-tab__time');
        expect(cells.length).toBeGreaterThan(0);
        cells.forEach((cell) => {
            expect(cell.textContent).toMatch(/2024/);
            expect(cell.textContent).not.toMatch(/ago|Just now/);
        });
    });

    it('uses a neutral Recorded status for audit rows', async () => {
        renderTab();

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        const auditRow = screen.getAllByRole('row').find((r) => r.textContent?.includes('Grab Skipped'));
        expect(auditRow).toBeDefined();
        const statusCell = auditRow!.querySelector('.activity-tab__status-cell');
        expect(statusCell?.textContent).toContain('Recorded');
        expect(statusCell?.querySelector('.activity-tab__status-icon--neutral')).not.toBeNull();
    });

    it('renders a pager and advances the page when more than one page exists', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({
                    ...mockActivity,
                    totalItems: 60,
                    pageNumber: url.includes('pageNumber=2') ? 2 : 1,
                    pageSize: 25,
                });
            }
            if (url.startsWith('/jobs/')) {
                return Promise.resolve(mockTree);
            }
            return Promise.resolve(null);
        });

        renderTab('/?tab=activity&sort=timestamp:asc');

        await waitFor(() => {
            expect(screen.getByText(/Page 1 of 3/)).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /go to next page/i }));

        await waitFor(() => {
            const activityCalls = vi
                .mocked(apiGet)
                .mock.calls.map((c) => c[0] as string)
                .filter((u) => u.startsWith('/audit-history'));
            expect(activityCalls.some((u) => u.includes('pageNumber=2'))).toBe(true);
        });
        expect(screen.getByTestId('activity-tab__location-search')).toHaveTextContent(
            '?tab=activity&sort=timestamp%3Aasc&page=2',
        );

        await user.click(screen.getByRole('button', { name: /go to first page/i }));
        await waitFor(() => expect(screen.getByText('Page 1 of 3')).toBeInTheDocument());
        expect(screen.getByTestId('activity-tab__location-search').textContent).toBe(
            '?tab=activity&sort=timestamp%3Aasc',
        );
    });
});
