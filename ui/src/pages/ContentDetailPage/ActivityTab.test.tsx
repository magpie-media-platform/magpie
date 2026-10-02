import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { flushSync } from 'react-dom';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { LocalContentPreviewProvider } from '@/components/content/LocalContentPreview';

import { ActivityTab } from './ActivityTab';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});
vi.mock('@/hooks/useLibraryTypes', () => ({ useLibraryTypes: () => ({ subjectTypes: [] }) }));

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

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiGet).mockImplementation((url: string) => {
        if (url.startsWith('/audit-history')) {
            return Promise.resolve(mockActivity);
        }
        if (url.startsWith('/jobs/')) {
            return Promise.resolve(mockTree);
        }
        if (url === '/content/content-1') {
            return Promise.resolve({ id: 'content-1', name: 'Local', contentType: 'movie', level: 'movie' });
        }
        if (url === '/content/content-1/metadata') {
            return Promise.resolve({ title: 'Local title' });
        }
        return Promise.resolve(null);
    });
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
                            <LocalContentPreviewProvider>{children}</LocalContentPreviewProvider>
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
    it.each(['Details', 'Related activity'])('keeps ContentItem identity in %s', async (view) => {
        // Arrange
        const user = userEvent.setup();
        const contentItemId = '55555555-5555-5555-5555-555555555555';
        const hierarchyId = '66666666-6666-6666-6666-666666666666';
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({
                    items: [
                        {
                            id: 'audit-item',
                            kind: 'AuditRecord',
                            timestamp: '2024-05-01T09:00:00Z',
                            correlationId,
                            label: 'ItemUpdated',
                            category: 'Content',
                            detail: 'Item reference.',
                            entityType: 'ContentItem',
                            entityId: contentItemId,
                        },
                    ],
                    totalItems: 1,
                    pageNumber: 1,
                    pageSize: 25,
                });
            }
            if (url === `/content?contentItemId=${contentItemId}&pageSize=2`) {
                return Promise.resolve({
                    items: [{ id: hierarchyId, contentItem: { id: contentItemId } }],
                    totalItems: 1,
                });
            }
            if (url === `/content/${hierarchyId}`) {
                return Promise.resolve({ id: hierarchyId, name: 'Local', contentType: 'movie', level: 'movie' });
            }
            if (url === `/content/${hierarchyId}/metadata`) return Promise.resolve({ title: 'Resolved item' });
            return Promise.resolve(null);
        });
        renderTab();
        await user.click(
            within((await screen.findByText('Item reference.')).closest('tr')!).getByRole('button', { name: 'Detail' }),
        );
        const source = screen.getByRole('dialog', { name: 'Activity detail' });
        if (view === 'Related activity') await user.click(within(source).getByRole('tab', { name: view }));
        // Act
        await user.click(within(source).getByRole('button', { name: 'Open content preview' }));
        // Assert
        expect(await screen.findByRole('dialog', { name: 'Resolved item' })).toBeInTheDocument();
        expect(apiGet).toHaveBeenCalledWith(`/content?contentItemId=${contentItemId}&pageSize=2`, expect.anything());
        expect(vi.mocked(apiGet).mock.calls.some(([url]) => url === `/content/${contentItemId}`)).toBe(false);
    });

    it('does not invent a preview for an unrelated entity', async () => {
        // Arrange
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({
                    items: [
                        {
                            id: 'audit-unknown',
                            kind: 'AuditRecord',
                            timestamp: '2024-05-01T09:00:00Z',
                            correlationId,
                            label: 'Update',
                            category: 'Jobs',
                            detail: 'Other entity 33333333-3333-3333-3333-333333333333 updated.',
                            entityType: 'OtherEntity',
                            entityId: '33333333-3333-3333-3333-333333333333',
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
        await user.click(
            within(
                (await screen.findByText('Other entity 33333333-3333-3333-3333-333333333333 updated.')).closest('tr')!,
            ).getByRole('button', { name: 'Detail' }),
        );
        const source = screen.getByRole('dialog', { name: 'Activity detail' });
        // Assert
        expect(within(source).queryByRole('link', { name: 'Open content preview' })).not.toBeInTheDocument();
        await user.click(within(source).getByRole('tab', { name: 'Related activity' }));
        expect(within(source).queryByRole('link', { name: 'Open content preview' })).not.toBeInTheDocument();
        expect(
            vi.mocked(apiGet).mock.calls.some(([url]) => url === '/content/33333333-3333-3333-3333-333333333333'),
        ).toBe(false);
    });

    it.each(['close button', 'Escape', 'outside click'])(
        'keeps the selected activity and restores its preview link after %s',
        async (dismissal) => {
            // Arrange
            const user = userEvent.setup();
            // JSDOM returns a snapshot. Browsers expose live animation names during exit.
            const getComputedStyle = window.getComputedStyle.bind(window);
            vi.spyOn(window, 'getComputedStyle').mockImplementation(
                (element) =>
                    new Proxy(getComputedStyle(element), {
                        get: (style, property) =>
                            property === 'animationName'
                                ? getComputedStyle(element).animationName
                                : Reflect.get(style, property),
                    }),
            );
            render(
                <TestWrapper initialEntry="/library/content-1?tab=activity">
                    <style>{`
                    .sheet-content[data-state='open'] { animation-name: sheet-modal-in; }
                    .sheet-content[data-state='closed'] { animation-name: sheet-modal-out; }
                `}</style>
                    <ActivityTab contentHierarchyItemId="content-1" />
                </TestWrapper>,
            );
            const row = (await screen.findByText('No acceptable release.')).closest('tr')!;
            await user.click(within(row).getByRole('button', { name: 'Detail' }));
            const source = screen.getByRole('dialog', { name: 'Activity detail' });
            const trigger = within(source).getByRole('link', { name: 'Open content preview' });
            // Act
            await user.click(trigger);
            const preview = await screen.findByRole('dialog', { name: 'Local title' });
            // Commit the close before the document receives the click, as in a browser event.
            const flushClick = () => flushSync(() => {});
            document.body.addEventListener('click', flushClick, { once: true });
            if (dismissal === 'close button') {
                await user.click(within(preview).getByRole('button', { name: 'Close' }));
            } else if (dismissal === 'Escape') {
                await user.keyboard('{Escape}');
            } else {
                await user.click(document.querySelector('.sheet-overlay')!);
            }
            document.body.removeEventListener('click', flushClick);
            await waitFor(() => expect(preview).toHaveAttribute('data-state', 'closed'));
            // Assert
            expect(preview).toBeInTheDocument();
            expect(preview).toHaveAttribute('data-state', 'closed');
            expect(source).toBeInTheDocument();
            // Act
            fireEvent(
                preview,
                Object.assign(new Event('animationend', { bubbles: true }), { animationName: 'sheet-modal-out' }),
            );
            // Assert
            await waitFor(() => expect(screen.getByRole('dialog', { name: 'Activity detail' })).toBe(source));
            expect(within(source).getByTestId('related-events__self')).toHaveTextContent('No acceptable release.');
            expect(within(source).getByRole('tab', { name: 'Details' })).toHaveAttribute('data-state', 'active');
            await waitFor(() => expect(trigger).toHaveFocus());
            expect(screen.getByTestId('activity-tab__location-search')).toHaveTextContent('?tab=activity');
            // Act
            await user.click(trigger);
            const reopened = await screen.findByRole('dialog', { name: 'Local title' });
            await user.keyboard('{Escape}');
            fireEvent(
                reopened,
                Object.assign(new Event('animationend', { bubbles: true }), { animationName: 'sheet-modal-out' }),
            );
            // Assert
            await waitFor(() => expect(trigger).toHaveFocus());
            expect(within(source).getByTestId('related-events__self')).toHaveTextContent('No acceptable release.');
            // Act
            await user.click(within(source).getByRole('button', { name: 'Close' }));
            // Assert
            await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        },
    );

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
