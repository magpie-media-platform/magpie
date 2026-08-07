import { render, screen, waitFor } from '@testing-library/react';
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

    it('fetches and renders the execution tree chronologically when active', async () => {
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const steps = screen.getAllByRole('listitem');
        expect(steps[0].textContent).toContain('First Step');
        expect(steps[1].textContent).toContain('Second Step');
        expect(apiGet).toHaveBeenCalledWith(`/jobs/${executionId}/execution-tree?include-siblings=true`);
    });

    it('renders the empty message when the execution tree is empty', async () => {
        vi.mocked(apiGet).mockResolvedValue({ root: null, totalCount: 0 });
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByText('No related events for this chain.')).toBeInTheDocument();
        });
    });

    it('does not show a capped notice for an execution tree', async () => {
        render(wrap(<RelatedEventsChain executionId={executionId} correlationId={correlationId} active />));

        await waitFor(() => {
            expect(screen.getByTestId('related-events__chain')).toBeInTheDocument();
        });
        expect(screen.queryByTestId('related-events-capped')).not.toBeInTheDocument();
    });

    it('combines matching audit activity with the execution tree and deduplicates the selected activity', async () => {
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
    });
});
