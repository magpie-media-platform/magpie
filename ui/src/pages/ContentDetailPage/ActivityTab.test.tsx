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

        expect(screen.getByRole('button', { name: /sort by activity/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sort by status/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sort by type/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sort by timestamp/i })).toBeInTheDocument();
        const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
        expect(headers.some((t) => t === 'Detail')).toBe(true);
        expect(screen.getAllByRole('button', { name: /^detail$/i }).length).toBeGreaterThan(0);
    });

    it('binds compact sort controls to the URL-backed activity sort state', async () => {
        const user = userEvent.setup();
        renderTab('/?sort=type:desc&page=2');

        await waitFor(() => {
            expect(screen.getByTestId('activity-tab__table')).toBeInTheDocument();
        });

        const controls = screen.getByTestId('activity-tab__sort-controls');
        const field = within(controls).getByRole('combobox', { name: 'Sort activity by' });
        expect(field).toHaveTextContent('Type');
        expect(within(controls).getByRole('button', { name: 'Sort activity by Type ascending' })).toBeEnabled();

        await user.click(field);
        expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
            'Activity',
            'Status',
            'Type',
            'Timestamp',
        ]);
        await user.click(screen.getByRole('option', { name: 'Timestamp' }));

        await waitFor(() => {
            const params = new URLSearchParams(screen.getByTestId('activity-tab__location-search').textContent ?? '');
            expect(params.get('sort')).toBe('timestamp:asc');
            expect(params.get('page')).toBeNull();
        });

        const getTableOrder = () =>
            Array.from(document.querySelectorAll('[data-testid="activity-tab__table"] tbody tr')).map((row) =>
                row.getAttribute('data-row-id'),
            );
        expect(getTableOrder()).toEqual(['AuditRecord-audit-1', 'JobExecution-job-1', 'JobExecution-job-acq-1']);

        await user.click(screen.getByRole('button', { name: 'Sort activity by Timestamp descending' }));

        await waitFor(() => {
            const params = new URLSearchParams(screen.getByTestId('activity-tab__location-search').textContent ?? '');
            expect(params.get('sort')).toBeNull();
        });
        expect(getTableOrder()).toEqual(['JobExecution-job-acq-1', 'JobExecution-job-1', 'AuditRecord-audit-1']);
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

    it('opens Details first and loads Trace context only after its tab is selected', async () => {
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

        await user.click(within(dialog).getByRole('tab', { name: 'Trace context' }));
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

    it('pins and highlights the clicked event when the chain omits it (outside the newest-20 window)', async () => {
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
        await user.click(within(dialog).getByRole('tab', { name: 'Trace context' }));
        await waitFor(() => {
            expect(within(dialog).getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const selfStep = within(dialog).getByTestId('related-events__self-step');
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

        renderTab();

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
    });
});
