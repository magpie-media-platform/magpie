import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { MagpieActivityEntryKind, type MagpieActivityEntry } from '@/hooks/useMagpieActivity';

import { RelatedEventsModal } from './RelatedEventsModal';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';

const correlationId = '22222222-2222-2222-2222-222222222222';
const executionId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const mockExecutionTree = {
    root: {
        id: 'c',
        correlationId,
        jobType: 'ThirdStep',
        status: 'Completed',
        createdAt: '2024-05-01T12:00:00Z',
        children: [
            {
                id: 'b',
                correlationId,
                jobType: 'SecondStep',
                status: 'Completed',
                createdAt: '2024-05-01T11:00:00Z',
                children: [],
            },
        ],
    },
    totalCount: 2,
};

const selfEntry: MagpieActivityEntry = {
    id: 'a',
    kind: MagpieActivityEntryKind.AuditRecord,
    timestamp: '2024-05-01T10:00:00Z',
    correlationId,
    label: 'FirstStep',
    category: 'Content',
    status: null,
    detail: 'kicked off the chain',
};

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiGet).mockImplementation((url: string) => {
        if (url.startsWith('/audit-history')) {
            return Promise.resolve({ items: [selfEntry], totalItems: 1, pageNumber: 1, pageSize: 1000 });
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

describe('RelatedEventsModal', () => {
    it('does not render or fetch while closed', () => {
        render(
            wrap(
                <RelatedEventsModal
                    executionId={executionId}
                    correlationId={correlationId}
                    selfEntry={selfEntry}
                    open={false}
                    onOpenChange={() => {}}
                />,
            ),
        );
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(apiGet).not.toHaveBeenCalled();
    });

    it('shows the selected activity in the default Details view without fetching trace context', async () => {
        render(
            wrap(
                <RelatedEventsModal
                    executionId={executionId}
                    correlationId={correlationId}
                    selfEntry={selfEntry}
                    open
                    onOpenChange={() => {}}
                />,
            ),
        );

        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).getByRole('heading', { name: 'Activity detail' })).toBeInTheDocument();
        expect(within(dialog).getByRole('tab', { name: 'Details' })).toHaveAttribute('data-state', 'active');
        expect(within(dialog).getByText('First Step')).toBeInTheDocument();
        const self = within(dialog).getByTestId('related-events__self');
        expect(self).toHaveTextContent('kicked off the chain');
        expect(self).toHaveTextContent('Content');
        expect(self).toHaveTextContent('Recorded');

        expect(within(dialog).queryByTestId('related-events__chain')).not.toBeInTheDocument();
        expect(apiGet).not.toHaveBeenCalled();
    });

    it('keeps the dialog header and tabs outside the bounded active-panel scroll body', async () => {
        const user = userEvent.setup();
        render(
            wrap(
                <RelatedEventsModal
                    executionId={executionId}
                    correlationId={correlationId}
                    selfEntry={selfEntry}
                    open
                    onOpenChange={() => {}}
                />,
            ),
        );

        const dialog = await screen.findByRole('dialog');
        const tablist = within(dialog).getByRole('tablist');
        const detailsPanel = within(dialog).getByRole('tabpanel');

        expect(detailsPanel).toHaveClass('dialog-body', 'tabs-content', 'related-events__tab-content');
        expect(detailsPanel).toHaveAttribute('tabindex', '0');
        expect(detailsPanel).not.toContainElement(within(dialog).getByRole('heading', { name: 'Activity detail' }));
        expect(detailsPanel).not.toContainElement(tablist);

        await user.click(within(dialog).getByRole('tab', { name: 'Trace context' }));
        await waitFor(() => {
            expect(within(dialog).getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const tracePanel = within(dialog).getByRole('tabpanel');
        expect(tracePanel).toHaveClass('dialog-body', 'tabs-content', 'related-events__tab-content');
        expect(tracePanel).toHaveAttribute('tabindex', '0');
        expect(tracePanel).not.toContainElement(tablist);

        const css = readFileSync(
            resolve(process.cwd(), 'src/components/activity/RelatedEventsModal/RelatedEventsModal.css'),
            'utf8',
        );
        expect(css).toMatch(/\.related-events__tabs\s*{[\s\S]*?flex:\s*1 1 auto;[\s\S]*?min-height:\s*0;/);
        expect(css).toMatch(
            /\.related-events__tab-content\s*{[\s\S]*?min-height:\s*0;[\s\S]*?overflow:\s*hidden auto;[\s\S]*?overscroll-behavior:\s*contain;/,
        );
        expect(css).toMatch(/\.related-events__self-title\s*{[\s\S]*?overflow-wrap:\s*anywhere;/);
        expect(css).toMatch(/\.related-events__step-label\s*{[\s\S]*?overflow-wrap:\s*anywhere;/);
    });

    it('loads combined trace context on demand, ordered chronologically, with the selected activity highlighted once', async () => {
        const user = userEvent.setup();
        render(
            wrap(
                <RelatedEventsModal
                    executionId={executionId}
                    correlationId={correlationId}
                    selfEntry={selfEntry}
                    open
                    onOpenChange={() => {}}
                />,
            ),
        );

        const dialog = await screen.findByRole('dialog');
        await user.click(within(dialog).getByRole('tab', { name: 'Trace context' }));

        await waitFor(() => {
            expect(within(dialog).getByTestId('related-events__chain')).toBeInTheDocument();
        });
        expect(apiGet).toHaveBeenCalledWith(`/jobs/${executionId}/execution-tree?include-siblings=true`);

        const steps = within(dialog).getAllByRole('listitem');
        expect(steps[0].textContent).toContain('First Step');
        expect(steps[1].textContent).toContain('Second Step');
        expect(steps[2].textContent).toContain('Third Step');

        const selfStep = within(dialog).getByTestId('related-events__self-step');
        expect(selfStep).toHaveTextContent('Selected activity');
        expect(selfStep).toHaveTextContent('First Step');
        expect(within(dialog).getAllByTestId('related-events__self-step')).toHaveLength(1);
        expect(within(dialog).queryByTestId('related-events__self')).not.toBeInTheDocument();
    });

    it('returns to Details when the selected event changes', async () => {
        const user = userEvent.setup();
        const { rerender } = render(
            wrap(
                <RelatedEventsModal
                    executionId={executionId}
                    correlationId={correlationId}
                    selfEntry={selfEntry}
                    open
                    onOpenChange={() => {}}
                />,
            ),
        );

        const dialog = await screen.findByRole('dialog');
        await user.click(within(dialog).getByRole('tab', { name: 'Trace context' }));
        await waitFor(() => {
            expect(within(dialog).getByTestId('related-events__chain')).toBeInTheDocument();
        });

        const otherSelf: MagpieActivityEntry = { ...selfEntry, id: 'other', detail: 'a later step' };
        rerender(
            wrap(
                <RelatedEventsModal
                    executionId={executionId}
                    correlationId={correlationId}
                    selfEntry={otherSelf}
                    open
                    onOpenChange={() => {}}
                />,
            ),
        );

        expect(within(dialog).queryByTestId('related-events__chain')).not.toBeInTheDocument();
        expect(within(dialog).getByTestId('related-events__self')).toHaveTextContent('a later step');
        expect(within(dialog).getByRole('tab', { name: 'Details' })).toHaveAttribute('data-state', 'active');
    });
});
