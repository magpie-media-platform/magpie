import userEvent from '@testing-library/user-event';
import { render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { MagpieActivityEntryKind, type MagpieActivityEntry } from '@/hooks/useMagpieActivity';

import { RelatedEventsChain } from './RelatedEventsChain';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';

const correlationId = '55555555-5555-5555-5555-555555555555';
const executionId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

const mockExecutionTree = {
    root: {
        id: 'b',
        correlationId,
        jobType: 'SecondStep',
        status: 'Completed',
        createdAt: '2024-05-01T11:00:00Z',
        children: [
            {
                id: 'a',
                correlationId,
                jobType: 'FirstStep',
                status: 'Running',
                createdAt: '2024-05-01T10:00:00Z',
                children: [],
            },
        ],
    },
    totalCount: 2,
};

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiGet).mockImplementation((url: string) => {
        if (url.startsWith('/audit-history')) {
            return Promise.resolve({ items: [], totalItems: 0, pageNumber: 1, pageSize: 1000 });
        }
        return Promise.resolve(mockExecutionTree);
    });
});

afterEach(() => {
    vi.restoreAllMocks();
});

function wrap(children: ReactNode) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe('RelatedEventsChain', () => {
    it('does not fetch while inactive', () => {
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active={false} />));
        expect(apiGet).not.toHaveBeenCalled();
    });

    it('does not fetch when the execution id is null', () => {
        render(wrap(<RelatedEventsChain executionId={null} correlationId={correlationId} active />));
        expect(apiGet).not.toHaveBeenCalled();
    });

    it('fetches and renders the execution tree in real parent-child order, not chronologically', async () => {
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const steps = screen.getAllByRole('listitem');
        expect(steps).toHaveLength(2);
        expect(steps[0].textContent).toContain('Second Step');
        expect(steps[1].textContent).toContain('First Step');
        expect(apiGet).toHaveBeenCalledWith(`/jobs/${executionId}/execution-tree?include-siblings=true`);
    });

    it('draws an arrow only from the parent into its real child, nested under it', async () => {
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const root = screen.getByText('Second Step').closest('[data-kind="job"]');
        expect(root).not.toBeNull();
        const connector = root!.querySelector(':scope > .related-events__connector');
        expect(connector).not.toBeNull();
        const child = root!.querySelector('.related-events__children [data-kind="job"]');
        expect(child?.textContent).toContain('First Step');
    });

    it('renders a parent with several children as a branch, not a flat line', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({ items: [], totalItems: 0, pageNumber: 1, pageSize: 1000 });
            }
            return Promise.resolve({
                root: {
                    id: 'parent',
                    correlationId,
                    jobType: 'Fanout',
                    status: 'Completed',
                    createdAt: '2024-05-01T09:00:00Z',
                    children: [
                        {
                            id: 'kid-1',
                            correlationId,
                            jobType: 'KidOne',
                            status: 'Completed',
                            createdAt: '2024-05-01T09:05:00Z',
                            children: [],
                        },
                        {
                            id: 'kid-2',
                            correlationId,
                            jobType: 'KidTwo',
                            status: 'Completed',
                            createdAt: '2024-05-01T09:06:00Z',
                            children: [],
                        },
                    ],
                },
                totalCount: 3,
            });
        });

        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const parent = screen.getByText('Fanout').closest('[data-kind="job"]');
        const branch = parent!.querySelector(':scope > .related-events__children');
        expect(branch).not.toBeNull();
        const kids = within(branch as HTMLElement).getAllByRole('listitem');
        expect(kids).toHaveLength(2);
        expect(kids[0].textContent).toContain('Kid One');
        expect(kids[1].textContent).toContain('Kid Two');
    });

    it.each([
        ['parent', 'Page 1 of 26', 20],
        ['child-499', 'Page 26 of 26', 1],
    ])('Pagination_ShowsExactChainCount_WhenOpeningPageContaining%s', async (selectedId, pageLabel, count) => {
        // Arrange
        const user = userEvent.setup();
        vi.mocked(apiGet).mockResolvedValue({
            root: {
                id: 'parent',
                correlationId,
                jobType: 'Fanout',
                status: 'Completed',
                createdAt: '2024-05-01T09:00:00Z',
                children: Array.from({ length: 500 }, (_, index) => ({
                    id: `child-${index}`,
                    correlationId,
                    jobType: 'ChildJob',
                    status: 'Completed',
                    createdAt: '2024-05-01T09:05:00Z',
                    children: [],
                })),
            },
            totalCount: 501,
        });
        // Act
        render(
            wrap(
                <RelatedEventsChain
                    executionId={selectedId}
                    selfEntry={{
                        id: selectedId,
                        kind: MagpieActivityEntryKind.JobExecution,
                        timestamp: '2024-05-01T09:05:00Z',
                        label: 'SelectedJob',
                        category: 'Job',
                    }}
                />,
            ),
        );
        // Assert
        await screen.findByText(pageLabel);
        expect(screen.getAllByRole('listitem')).toHaveLength(count);
        expect(screen.getByTestId('related-events__self-step')).toHaveTextContent('Selected Job');
        const pager = within(screen.getByRole('navigation', { name: 'Job chain pagination' }));
        expect(pager.getByText('501 results')).toBeInTheDocument();

        // Act
        await user.click(pager.getByRole('button', { name: 'Go to first page' }));

        // Assert
        expect(screen.getAllByRole('listitem')).toHaveLength(20);

        // Act
        await user.click(pager.getByRole('button', { name: 'Go to next page' }));

        // Assert
        expect(screen.getAllByRole('listitem')).toHaveLength(20);
        expect(screen.getAllByText(/^Parent: /)).toHaveLength(20);

        // Act
        await user.click(pager.getByRole('button', { name: 'Go to last page' }));

        // Assert
        expect(screen.getAllByRole('listitem')).toHaveLength(1);
        expect(screen.getByText('Page 26 of 26')).toBeInTheDocument();
    });

    it('renders the empty message when the execution tree is empty', async () => {
        vi.mocked(apiGet).mockResolvedValue({ root: null, totalCount: 0 });
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByText('No activity is related to this one.')).toBeInTheDocument();
        });
    });

    it('does not show a capped notice for a complete execution tree', async () => {
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });
        expect(screen.queryByTestId('related-events-capped')).not.toBeInTheDocument();
        expect(screen.queryByTestId('related-events-content-capped')).not.toBeInTheDocument();
    });

    it('says where the chain was truncated when the execution tree hit the node limit', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({ items: [], totalItems: 0, pageNumber: 1, pageSize: 1000 });
            }
            return Promise.resolve({ ...mockExecutionTree, truncated: true });
        });

        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByTestId('related-events-capped')).toBeInTheDocument();
        });
        expect(screen.getByTestId('related-events-capped').textContent).toContain(
            'This chain is truncated at 2 activities.',
        );
        expect(screen.queryByTestId('related-events-content-capped')).not.toBeInTheDocument();
    });

    it.each([
        { treeTruncated: false, label: 'only the content activity' },
        { treeTruncated: true, label: 'both result sets' },
    ])('reports each truncated result set with its own count when $label hit the limit', async ({ treeTruncated }) => {
        const entry: MagpieActivityEntry = {
            id: 'audit-1',
            kind: MagpieActivityEntryKind.AuditRecord,
            timestamp: '2024-05-01T09:00:00Z',
            correlationId,
            label: 'GrabSkipped',
            category: 'Content',
            status: null,
            detail: 'No acceptable release.',
        };
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({ items: [entry], totalItems: 1500, pageNumber: 1, pageSize: 1000 });
            }
            return Promise.resolve({ ...mockExecutionTree, truncated: treeTruncated });
        });

        render(
            wrap(
                <RelatedEventsChain
                    executionId={executionId}
                    correlationId={correlationId}
                    seedEntityId="content-1"
                    active
                />,
            ),
        );

        await waitFor(() => {
            expect(screen.getByTestId('related-events-content-capped')).toBeInTheDocument();
        });
        expect(screen.getByTestId('related-events-content-capped').textContent).toContain(
            'Only the latest 1 of 1500 activities for this item were searched.',
        );
        expect(screen.queryByTestId('related-events-capped') !== null).toBe(treeTruncated);
    });

    it('puts a same-correlation activity with no lineage edge in a distinct group, not the arrow chain', async () => {
        const selfEntry: MagpieActivityEntry = {
            id: 'audit-1',
            kind: MagpieActivityEntryKind.AuditRecord,
            timestamp: '2024-05-01T09:00:00Z',
            correlationId,
            label: 'GrabSkipped',
            category: 'Content',
            status: null,
            detail: 'No acceptable release.',
        };
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({
                    items: [selfEntry, { ...selfEntry }, { ...selfEntry, id: 'other', correlationId: 'other' }],
                    totalItems: 3,
                    pageNumber: 1,
                    pageSize: 1000,
                });
            }
            return Promise.resolve(mockExecutionTree);
        });

        render(
            wrap(
                <RelatedEventsChain
                    executionId={executionId}
                    correlationId={correlationId}
                    seedEntityId="content-1"
                    selfEntry={selfEntry}
                    active
                />,
            ),
        );

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });
        expect(screen.getAllByRole('listitem')).toHaveLength(3);
        expect(screen.getAllByTestId('related-events__self-step')).toHaveLength(1);
        expect(screen.getByTestId('related-events__self-step')).toHaveTextContent('Grab Skipped');
        expect(screen.getByTestId('related-events__self-step')).toHaveTextContent('Recorded');

        const also = screen.getByTestId('related-events__also');
        expect(within(also).getByTestId('related-events__self-step')).toBeInTheDocument();
        expect(also.querySelector('.related-events__connector')).toBeNull();
    });

    it('Pagination_ShowsExactRelatedCount_WhenRelatedEntriesSpanPages', async () => {
        // Arrange
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({
                    items: Array.from({ length: 21 }, (_, index) => ({
                        id: `audit-${index}`,
                        kind: MagpieActivityEntryKind.AuditRecord,
                        timestamp: '2024-05-01T09:00:00Z',
                        correlationId,
                        label: 'GrabSkipped',
                        category: 'Content',
                        status: null,
                        detail: 'No acceptable release.',
                    })),
                    totalItems: 21,
                    pageNumber: 1,
                    pageSize: 1000,
                });
            }
            return Promise.resolve(mockExecutionTree);
        });

        // Act
        render(wrap(<RelatedEventsChain executionId={null} correlationId={correlationId} seedEntityId="content-1" />));

        // Assert
        const pager = within(await screen.findByRole('navigation', { name: 'Related activity pagination' }));
        expect(pager.getByText('Page 1 of 2')).toBeInTheDocument();
        expect(pager.getByText('21 results')).toBeInTheDocument();

        // Act
        await user.click(pager.getByRole('button', { name: 'Go to next page' }));

        // Assert
        expect(pager.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(pager.getByText('21 results')).toBeInTheDocument();
    });

    it('renders a lone activity with no relations sanely, with no arrows and no also section', async () => {
        const selfEntry: MagpieActivityEntry = {
            id: 'b',
            kind: MagpieActivityEntryKind.JobExecution,
            timestamp: '2024-05-01T11:00:00Z',
            correlationId,
            label: 'SecondStep',
            category: 'Job',
            status: 'Completed',
            detail: null,
        };
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve({ items: [], totalItems: 0, pageNumber: 1, pageSize: 1000 });
            }
            return Promise.resolve({
                root: {
                    id: 'b',
                    correlationId,
                    jobType: 'SecondStep',
                    status: 'Completed',
                    createdAt: selfEntry.timestamp,
                    children: [],
                },
                totalCount: 1,
            });
        });

        const { container } = render(
            wrap(<RelatedEventsChain executionId="b" correlationId={correlationId} selfEntry={selfEntry} active />),
        );

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });
        expect(screen.getAllByRole('listitem')).toHaveLength(1);
        expect(screen.getByTestId('related-events__self-step')).toHaveTextContent('Second Step');
        expect(screen.queryByTestId('related-events__also')).not.toBeInTheDocument();
        expect(container.querySelector('.related-events__connector')).toBeNull();
        expect(screen.getByText('No other activity is related to this one.')).toBeInTheDocument();
    });
});
