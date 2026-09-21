import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';
import type { ManualMatchTarget } from './ManualMatchDialog';
import type { ImportAcceptAction } from './AcceptImportDialog';

const bulkAcceptMutateAsync = vi.fn();
let manualMatchProp: ManualMatchTarget | null = null;

vi.mock('@/hooks/useBulkAcceptLibraryImportCandidates', () => ({
    useBulkAcceptLibraryImportCandidates: () => ({ mutateAsync: bulkAcceptMutateAsync, isPending: false }),
}));

vi.mock('./ManualMatchDialog', () => ({
    ManualMatchDialog: ({ target }: { target: ManualMatchTarget | null }) => {
        manualMatchProp = target;
        return target ? <div data-testid="manual-match-dialog-open">{target.kind}</div> : null;
    },
}));

vi.mock('@/components/content/PreviewDrawer', () => ({
    PreviewDrawer: ({ open }: { open: boolean }) => (open ? <div data-testid="preview-drawer-open" /> : null),
}));

import { CandidateLeafTable } from './CandidateLeafTable';

function buildMatched(overrides: Partial<LibraryImportCandidate> = {}): LibraryImportCandidate {
    return {
        id: 'match-1',
        rootFolderId: 'folder-1',
        scanRunId: 'scan-1',
        filePath: '/media/tv/Bluey/S01E01.mkv',
        contentType: 'tv',
        status: 'Pending',
        parsedSignals: { title: ['Bluey'], year: ['2018'] },
        matches: [
            {
                confidence: 0.95,
                providerType: 'tvmaze',
                externalIdKey: 'tvmaze',
                externalId: '44796',
                title: 'Bluey',
                subtitle: '2018',
            },
        ],
        matchCount: 1,
        createdAt: '2024-01-01T00:00:00Z',
        ...overrides,
    };
}

function buildUnmatched(overrides: Partial<LibraryImportCandidate> = {}): LibraryImportCandidate {
    return buildMatched({
        id: 'empty-1',
        filePath: '/media/tv/Bluey/S01E02.mkv',
        parsedSignals: { title: ['Bluey'] },
        matches: [],
        matchCount: 0,
        ...overrides,
    });
}

function rowFor(id: string): HTMLElement {
    const cell = document.querySelector(`.candidate-leaf-table__file-cell[data-candidate-id="${id}"]`);
    const row = cell?.closest('tr');
    if (!row) {
        throw new Error(`No row found for candidate ${id}`);
    }
    return row as HTMLElement;
}

function createTestWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>
        );
    };
}

describe('CandidateLeafTable', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        manualMatchProp = null;
        bulkAcceptMutateAsync.mockResolvedValue({
            queued: [{ candidateId: 'match-1', jobExecutionId: 'job-1' }],
            skipped: [],
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders a matched row with its candidate, a Detail and Import action, and an enabled checkbox', async () => {
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        const row = rowFor('match-1');
        expect(row).toHaveTextContent('Bluey');
        expect(row).toHaveTextContent('tvmaze');
        expect(within(row).getByRole('button', { name: /^Detail$/ })).toBeInTheDocument();
        expect(within(row).getByRole('button', { name: /^Import$/ })).toBeInTheDocument();
        const checkbox = within(row).getByRole('checkbox', { name: /select s01e01/i });
        expect(checkbox).not.toBeDisabled();
    });

    it('exposes the full file path in the file row', () => {
        render(<CandidateLeafTable items={[buildUnmatched()]} emptyState={null} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });

        expect(screen.getByTitle('/media/tv/Bluey/S01E02.mkv')).toHaveTextContent('/media/tv/Bluey/S01E02.mkv');
    });

    it('renders an unmatched row as "No match" with only a Match action and a disabled checkbox', () => {
        render(
            <CandidateLeafTable
                items={[buildUnmatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        const row = rowFor('empty-1');
        expect(within(row).getByTestId('candidate-leaf-no-match')).toHaveTextContent('No match');
        expect(within(row).getByRole('button', { name: /^Match$/ })).toBeInTheDocument();
        expect(within(row).queryByRole('button', { name: /^Import$/ })).toBeNull();
        const checkbox = within(row).getByRole('checkbox', { name: /select s01e02/i });
        expect(checkbox).toBeDisabled();
    });

    it('renders a matching-pending row honestly, with no Match action and a disabled checkbox', () => {
        render(
            <CandidateLeafTable
                items={[buildUnmatched({ id: 'pending-1', status: 'MatchingPending' })]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        const row = rowFor('pending-1');
        expect(within(row).getByTestId('candidate-leaf-matching-pending')).toHaveTextContent('Matching pending');
        expect(within(row).queryByRole('button', { name: /^Match$/ })).toBeNull();
        expect(within(row).getByRole('button', { name: /^Matching pending$/ })).toBeDisabled();
        const checkbox = within(row).getByRole('checkbox', { name: /select s01e02/i });
        expect(checkbox).toBeDisabled();
    });

    it('queues a matched row for import via the shared bulk submission hook', async () => {
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        await user.click(within(rowFor('match-1')).getByRole('button', { name: /^Import$/ }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
    });

    it('reports when a single-file import is skipped instead of queued', async () => {
        const user = userEvent.setup();
        bulkAcceptMutateAsync.mockResolvedValueOnce({
            queued: [],
            skipped: [{ candidateId: 'match-1', reason: 'The file is already queued.' }],
        });
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );

        await user.click(within(rowFor('match-1')).getByRole('button', { name: /^Import$/ }));

        expect(await screen.findByText('Import skipped')).toBeInTheDocument();
        expect(screen.getByText('The file is already queued.')).toBeInTheDocument();
        expect(screen.queryByText('Import queued')).not.toBeInTheDocument();
    });

    it('shows Retry for a matched failed row and submits it through the accept mutation', async () => {
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched({ status: 'Failed', notes: 'provider offline' })]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );

        const row = rowFor('match-1');
        const retry = within(row).getByRole('button', { name: /^Retry$/ });
        expect(retry).toHaveAttribute('title', 'provider offline');
        await user.click(retry);

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
        expect(within(row).getByRole('checkbox', { name: /select s01e01/i })).not.toBeDisabled();
    });

    it('requires monitoring before importing and uses the chosen mode', async () => {
        const user = userEvent.setup();
        let pendingAccept: ImportAcceptAction | undefined;
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => {
                    pendingAccept = action;
                }}
            />,
            { wrapper: createTestWrapper() },
        );

        await user.click(within(rowFor('match-1')).getByRole('button', { name: /^Import$/ }));

        expect(pendingAccept).toBeDefined();
        expect(bulkAcceptMutateAsync).not.toHaveBeenCalled();

        void pendingAccept?.('Inherit');

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
    });

    it('importing one row leaves another row Import button enabled (concurrent imports are independent)', async () => {
        const user = userEvent.setup();
        let resolveAccept: (value: { queued: unknown[]; skipped: unknown[] }) => void = () => {};
        bulkAcceptMutateAsync.mockImplementationOnce(
            () =>
                new Promise((resolve) => {
                    resolveAccept = resolve;
                }),
        );
        render(
            <CandidateLeafTable
                items={[buildMatched(), buildMatched({ id: 'match-2', filePath: '/media/tv/Bluey/S01E03.mkv' })]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );

        await user.click(within(rowFor('match-1')).getByRole('button', { name: /^Import$/ }));

        expect(within(rowFor('match-1')).getByRole('button', { name: /^Import$/ })).toBeDisabled();
        expect(within(rowFor('match-2')).getByRole('button', { name: /^Import$/ })).not.toBeDisabled();

        resolveAccept({ queued: [], skipped: [] });
        await waitFor(() => {
            expect(within(rowFor('match-1')).getByRole('button', { name: /^Import$/ })).not.toBeDisabled();
        });
    });

    it('bulk-imports the checked matched rows via the bulk-accept mutation', async () => {
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched(), buildMatched({ id: 'match-2', filePath: '/media/tv/Bluey/S01E03.mkv' })]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );

        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i }));
        const acceptChecked = await screen.findByRole('button', { name: /import checked/i });

        await user.click(acceptChecked);

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
    });

    it('takes only the rows on the page when the header checkbox is ticked, and offers the rest through the banner', async () => {
        const user = userEvent.setup();
        const items = Array.from({ length: 26 }, (_, index) =>
            buildMatched({ id: `match-${index}`, filePath: `/media/tv/Bluey/S01E${index}.mkv` }),
        );
        render(
            <CandidateLeafTable
                items={items}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );

        await user.click(screen.getByRole('checkbox', { name: 'Select all matched files' }));

        expect(screen.getByText('25 selected')).toBeInTheDocument();
        expect(screen.getByText('All 25 on this page are selected.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select all 26 across all pages' })).toBeInTheDocument();
    });

    it('drops a checked row from the count and the submission once it is no longer submittable', async () => {
        const user = userEvent.setup();
        const other = buildMatched({ id: 'match-2', filePath: '/media/tv/Bluey/S01E03.mkv' });
        const { rerender } = render(
            <CandidateLeafTable
                items={[buildMatched(), other]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );

        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i }));
        await user.click(within(rowFor('match-2')).getByRole('checkbox', { name: /select s01e03/i }));
        expect(screen.getByText('2 selected')).toBeInTheDocument();

        rerender(
            <CandidateLeafTable
                items={[buildMatched({ status: 'Queued' }), other]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
        );
        expect(screen.getByText('1 selected')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /import checked/i }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-2'], monitoringMode: 'Inherit' });
        });
    });

    it('clears the whole selection from the desktop toolbar Clear button', async () => {
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i }));
        expect(screen.getByText('1 selected')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Clear selection' }));

        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
        expect(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i })).not.toBeChecked();
    });

    it('opens the manual-match dialog targeting the candidate for an unmatched row', async () => {
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildUnmatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        await user.click(within(rowFor('empty-1')).getByRole('button', { name: /^Match$/ }));

        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog-open')).toHaveTextContent('candidate');
        });
        expect(manualMatchProp?.kind).toBe('candidate');
        expect(manualMatchProp?.kind === 'candidate' && manualMatchProp.candidate.id).toBe('empty-1');
    });

    it('renders the provided empty state when there are no candidates', () => {
        render(
            <CandidateLeafTable
                items={[]}
                emptyState={<div data-testid="leaf-empty">Nothing here</div>}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );

        expect(screen.getByTestId('leaf-empty')).toBeInTheDocument();
    });
});

describe('CandidateLeafTable (mobile card collapse)', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        manualMatchProp = null;
        bulkAcceptMutateAsync.mockResolvedValue({
            queued: [{ candidateId: 'match-1', jobExecutionId: 'job-1' }],
            skipped: [],
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('marks the filename cell as header role, match as field, actions as actions, and select as hidden (no layout-participating checkbox)', () => {
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        expect(document.querySelector('td[data-column="select"]')).toHaveAttribute('data-mobile-role', 'hidden');
        expect(document.querySelector('td[data-column="filename"]')).toHaveAttribute('data-mobile-role', 'header');
        expect(document.querySelector('td[data-column="match"]')).toHaveAttribute('data-mobile-role', 'field');
        expect(document.querySelector('td[data-column="actions"]')).toHaveAttribute('data-mobile-role', 'actions');
    });

    it('shows the selection overlay only for a submittable row while selection mode is active', async () => {
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        expect(document.querySelectorAll('[data-action="toggle-row-mobile"]').length).toBe(0);

        await user.click(screen.getByRole('button', { name: 'Select' }));
        expect(document.querySelectorAll('[data-action="toggle-row-mobile"]').length).toBe(1);
    });

    it('clears any selection made while exiting selection mode', async () => {
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByRole('checkbox', { name: /select s01e01/i }));
        expect(screen.getByRole('checkbox', { name: /select s01e01/i })).toBeChecked();

        await user.click(screen.getByRole('button', { name: 'Stop selecting' }));
        await user.click(screen.getByRole('button', { name: 'Select' }));

        expect(screen.getByRole('checkbox', { name: /select s01e01/i })).not.toBeChecked();
    });
});
