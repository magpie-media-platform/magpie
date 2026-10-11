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
const restoreMutateAsync = vi.fn();
const rejectMutateAsync = vi.fn();
let manualMatchProp: ManualMatchTarget | null = null;
let manualMatchSuppressesToast: boolean | undefined;

vi.mock('@/hooks/useBulkAcceptLibraryImportCandidates', () => ({
    useBulkAcceptLibraryImportCandidates: () => ({ mutateAsync: bulkAcceptMutateAsync, isPending: false }),
}));

vi.mock('@/hooks/usePendingFileAssociations', () => ({
    useRestoreLibraryImportCandidates: () => ({ mutateAsync: restoreMutateAsync, isPending: false }),
    useRejectLibraryImportCandidates: () => ({ mutateAsync: rejectMutateAsync, isPending: false }),
}));

vi.mock('./ManualMatchDialog', () => ({
    ManualMatchDialog: ({
        target,
        suppressSuccessToast,
    }: {
        target: ManualMatchTarget | null;
        suppressSuccessToast?: boolean;
    }) => {
        manualMatchProp = target;
        manualMatchSuppressesToast = suppressSuccessToast;
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

    it('RowMenu_ListsDetailsPreviewImportAndReject_WhenTheFileIsMatchedAndPending', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        const row = rowFor('match-1');
        const checkbox = within(row).getByRole('checkbox', { name: /select s01e01/i });

        // Act
        await user.click(within(row).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Assert
        expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
            'View details',
            'Preview',
            'Import',
            'Reject',
        ]);
        expect(row.querySelector('[data-action="file-details"]')).toHaveTextContent('S01E01.mkv');
        expect(row.querySelector('[data-action="status-details"]')).toHaveTextContent('Pending');
        expect(checkbox).not.toBeDisabled();
    });

    it('FileDetails_ShowsTheFilenameMatchAndFullPath_WhenTheFilenameIsChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });

        // Act
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' }));

        // Assert
        const details = screen.getByRole('dialog', { name: 'S01E01.mkv' });
        expect(details).toHaveAttribute('data-dialog-content');
        expect(details).toHaveTextContent('Matched content');
        expect(details).toHaveTextContent('Bluey');
        expect(details).toHaveTextContent('/media/tv/Bluey/S01E01.mkv');
        expect(details).toHaveTextContent('1 of 1');
    });

    it('FileDetails_ShowsTheFailureReasonWithoutHover_WhenTheFailedStatusIsChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched({ status: 'Failed', notes: 'Import failed: No file was attached.' })]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );

        // Act
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Failed: S01E01.mkv. View details' }));

        // Assert
        const details = screen.getByRole('dialog', { name: 'S01E01.mkv' });
        expect(within(details).getByRole('alert')).toHaveTextContent('Import failed: No file was attached.');
        expect(within(details).getByRole('button', { name: 'Retry' })).toBeInTheDocument();
        expect(within(details).getByRole('button', { name: 'Reject' })).toBeInTheDocument();
    });

    it('FileDetails_ShowsTheNextFile_WhenNextFileIsChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched(), buildUnmatched({ status: 'Rejected' })]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' }));

        // Act
        await user.click(screen.getByRole('button', { name: 'Next file' }));

        // Assert
        const details = screen.getByRole('dialog', { name: 'S01E02.mkv' });
        expect(details).toHaveTextContent('2 of 2');
        expect(details).toHaveTextContent('Silenced');
        expect(within(details).getByRole('button', { name: 'Restore' })).toBeInTheDocument();
        const next = within(details).getByRole('button', { name: 'Next file' });
        expect(next).toHaveAttribute('aria-disabled', 'true');
        expect(next).not.toHaveAttribute('disabled');
        expect(next).toHaveFocus();
    });

    it('FileDetails_ReturnsFocusToTheFilename_WhenTheDetailsClose', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        const filename = within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' });
        await user.click(filename);

        // Act
        await user.keyboard('{Escape}');

        // Assert
        expect(screen.queryByRole('dialog', { name: 'S01E01.mkv' })).not.toBeInTheDocument();
        await waitFor(() => expect(filename).toHaveFocus());
    });

    it('FileDetails_ReturnsFocusToTheFilename_WhenTheDetailsCloseAfterAReject', async () => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockResolvedValue({ count: 1, error: null });
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        const filename = within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' });
        await user.click(filename);
        const details = screen.getByRole('dialog', { name: 'S01E01.mkv' });
        await user.click(within(details).getByRole('button', { name: 'Reject' }));
        await waitFor(() => expect(within(details).getByRole('button', { name: 'Reject' })).toBeEnabled());

        // Act
        await user.click(within(details).getByRole('button', { name: 'Close' }));

        // Assert
        await waitFor(() => expect(filename).toHaveFocus());
    });

    it('FileDetails_PutsFirstFocusOnAnEnabledControl_WhenTheFirstFileIsSilenced', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildUnmatched({ status: 'Rejected' }), buildMatched()]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );

        // Act
        await user.click(within(rowFor('empty-1')).getByRole('button', { name: 'S01E02.mkv' }));

        // Assert
        const details = screen.getByRole('dialog', { name: 'S01E02.mkv' });
        expect(within(details).getByRole('button', { name: 'Previous file' })).toHaveAttribute('aria-disabled', 'true');
        await waitFor(() => expect(within(details).getByRole('button', { name: 'Next file' })).toHaveFocus());
    });

    it('FileDetailsReject_ShowsNoToast_WhenTheRejectSucceeds', async () => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockResolvedValue({ count: 1, error: null });
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' }));
        const details = screen.getByRole('dialog', { name: 'S01E01.mkv' });

        // Act
        await user.click(within(details).getByRole('button', { name: 'Reject' }));

        // Assert
        await waitFor(() => expect(within(details).getByRole('button', { name: 'Reject' })).toBeEnabled());
        expect(rejectMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'] });
        expect(document.querySelector('[data-component="toast"]')).toBeNull();
        expect(within(details).queryByRole('alert')).toBeNull();
    });

    it('FileDetailsReject_ShowsTheFailureInTheDetails_WhenTheRejectFails', async () => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockRejectedValue(new Error('Network error'));
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' }));
        const details = screen.getByRole('dialog', { name: 'S01E01.mkv' });

        // Act
        await user.click(within(details).getByRole('button', { name: 'Reject' }));

        // Assert
        const failure = await within(details).findByRole('alert');
        expect(failure).toHaveTextContent('Reject failed');
        expect(failure).toHaveTextContent('Failed to reject the file. Please try again.');
        expect(document.querySelector('[data-component="toast"]')).toBeNull();
    });

    it('FileDetailsReject_PutsFocusOnTheFailure_WhenTheRejectFails', async () => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockRejectedValue(new Error('Network error'));
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' }));
        const details = screen.getByRole('dialog', { name: 'S01E01.mkv' });

        // Act
        await user.click(within(details).getByRole('button', { name: 'Reject' }));

        // Assert
        const failure = await within(details).findByRole('alert');
        expect(failure).toHaveFocus();
    });

    it('FileDetailsReject_ShowsTheFailureInAToast_WhenTheDetailsCloseBeforeTheRejectFails', async () => {
        // Arrange
        const user = userEvent.setup();
        let failReject: (error: Error) => void = () => {};
        rejectMutateAsync.mockImplementationOnce(
            () =>
                new Promise((_, reject) => {
                    failReject = reject;
                }),
        );
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        const filename = within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' });
        await user.click(filename);
        await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Reject' }));
        await user.keyboard('{Escape}');

        // Act
        failReject(new Error('Network error'));

        // Assert
        expect(await screen.findByTestId('toast-title')).toHaveTextContent('Reject failed');
        await user.click(filename);
        expect(within(screen.getByRole('dialog', { name: 'S01E01.mkv' })).queryByRole('alert')).toBeNull();
    });

    it('FileDetailsImport_ShowsTheSkipReasonInTheDetails_WhenTheImportIsSkipped', async () => {
        // Arrange
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
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' }));
        const details = screen.getByRole('dialog', { name: 'S01E01.mkv' });

        // Act
        await user.click(within(details).getByRole('button', { name: 'Import' }));

        // Assert
        const failure = await within(details).findByRole('alert');
        expect(failure).toHaveTextContent('Import skipped');
        expect(failure).toHaveTextContent('The file is already queued.');
        expect(document.querySelector('[data-component="toast"]')).toBeNull();
    });

    it('FileDetailsMatch_SuppressesTheMatchToast_WhenChosenInTheDetails', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<CandidateLeafTable items={[buildUnmatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('empty-1')).getByRole('button', { name: 'S01E02.mkv' }));
        const details = screen.getByRole('dialog', { name: 'S01E02.mkv' });

        // Act
        await user.click(within(details).getByRole('button', { name: 'Match' }));

        // Assert
        expect(screen.getByTestId('manual-match-dialog-open')).toHaveTextContent('candidate');
        expect(manualMatchSuppressesToast).toBe(true);
    });

    it('FileDetails_ReturnsFocusToTheFilename_WhenTheListChangesWhileTheDetailsAreOpen', async () => {
        // Arrange
        const user = userEvent.setup();
        const { rerender } = render(
            <CandidateLeafTable
                items={[buildMatched(), buildUnmatched()]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );
        const filename = within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' });
        await user.click(filename);
        rerender(
            <CandidateLeafTable
                items={[buildMatched(), buildUnmatched({ status: 'Rejected' })]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
        );

        // Act
        await user.keyboard('{Escape}');

        // Assert
        await waitFor(() => expect(filename).toHaveFocus());
    });

    it('RowMenu_OffersMatchButNoImport_WhenTheFileHasNoMatch', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<CandidateLeafTable items={[buildUnmatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        const row = rowFor('empty-1');
        const checkbox = within(row).getByRole('checkbox', { name: /select s01e02/i });

        // Act
        await user.click(within(row).getByRole('button', { name: 'Actions for S01E02.mkv' }));

        // Assert
        expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
            'View details',
            'Match',
            'Reject',
        ]);
        expect(within(row).getByTestId('candidate-leaf-no-match')).toHaveTextContent('No match');
        expect(checkbox).toBeDisabled();
    });

    it('FileDetails_ShowsTheMatchingRetryReason_WhenMatchingIsPending', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[
                    buildUnmatched({
                        id: 'pending-1',
                        status: 'MatchingPending',
                        notes: 'Matching will retry because the metadata search did not complete: provider-a timed out.',
                    }),
                ]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );
        const checkbox = within(rowFor('pending-1')).getByRole('checkbox', { name: /select s01e02/i });

        // Act
        await user.click(
            within(rowFor('pending-1')).getByRole('button', { name: 'Matching pending: S01E02.mkv. View details' }),
        );

        // Assert
        expect(screen.getByRole('dialog', { name: 'S01E02.mkv' })).toHaveTextContent('provider-a timed out.');
        expect(checkbox).toBeDisabled();
    });

    it('RowMenuImport_QueuesTheFileThroughTheBulkSubmission_WhenChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Import' }));

        // Assert
        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
    });

    it('RowMenuImport_ReportsTheSkipReason_WhenTheImportIsSkipped', async () => {
        // Arrange
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
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Import' }));

        // Assert
        expect(await screen.findByText('Import skipped')).toBeInTheDocument();
        expect(screen.getByText('The file is already queued.')).toBeInTheDocument();
        expect(screen.queryByText('Import queued')).not.toBeInTheDocument();
    });

    it('ImportSelected_ReportsNothingQueued_WhenTheServerSkipsEveryFile', async () => {
        // Arrange
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
        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i }));

        // Act
        await user.click(screen.getByRole('button', { name: 'Import selected' }));

        // Assert
        expect(await screen.findByText('Nothing queued for import')).toBeInTheDocument();
        expect(screen.getByText('0 files queued for import, 1 skipped.')).toBeInTheDocument();
        expect(screen.queryByText('Import queued')).not.toBeInTheDocument();
    });

    it('RowMenuRetry_QueuesTheFile_WhenTheFileFailed', async () => {
        // Arrange
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
        await user.click(within(row).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Retry' }));

        // Assert
        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
        expect(within(row).getByRole('checkbox', { name: /select s01e01/i })).not.toBeDisabled();
    });

    it('RowMenuImport_WaitsForTheMonitoringChoice_WhenChosen', async () => {
        // Arrange
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
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));
        await user.click(screen.getByRole('menuitem', { name: 'Import' }));
        expect(bulkAcceptMutateAsync).not.toHaveBeenCalled();

        // Act
        void pendingAccept?.('Inherit');

        // Assert
        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
    });

    it('RowMenuImport_KeepsImportEnabledForTheOtherFile_WhenOneFileIsQueuing', async () => {
        // Arrange
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
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Import' }));

        // Assert
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));
        expect(screen.getByRole('menuitem', { name: 'Import' })).toHaveAttribute('aria-disabled', 'true');
        await user.keyboard('{Escape}');
        await user.click(within(rowFor('match-2')).getByRole('button', { name: 'Actions for S01E03.mkv' }));
        expect(screen.getByRole('menuitem', { name: 'Import' })).not.toHaveAttribute('aria-disabled');
        await user.keyboard('{Escape}');
        resolveAccept({ queued: [], skipped: [] });
        expect(await screen.findByText('Import skipped')).toBeInTheDocument();
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));
        expect(screen.getByRole('menuitem', { name: 'Import' })).not.toHaveAttribute('aria-disabled');
    });

    it('ImportSelected_QueuesOnlyTheSelectedFiles_WhenChosen', async () => {
        // Arrange
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

        // Act
        await user.click(await screen.findByRole('button', { name: 'Import selected' }));

        // Assert
        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'], monitoringMode: 'Inherit' });
        });
    });

    it('RejectSelected_RejectsOnlyTheSelectedFiles_WhenTwoOfThreeFilesAreSelected', async () => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockResolvedValue({ count: 2 });
        render(
            <CandidateLeafTable
                items={[
                    buildMatched(),
                    buildMatched({ id: 'match-2', filePath: '/media/tv/Bluey/S01E03.mkv' }),
                    buildMatched({ id: 'match-3', filePath: '/media/tv/Bluey/S01E04.mkv', status: 'Failed' }),
                ]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );
        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i }));
        await user.click(within(rowFor('match-3')).getByRole('checkbox', { name: /select s01e04/i }));

        // Act
        await user.click(screen.getByRole('button', { name: 'Reject selected' }));

        // Assert
        expect(rejectMutateAsync).toHaveBeenCalledWith({ ids: ['match-1', 'match-3'] });
        expect(await screen.findByText('Matches rejected')).toBeInTheDocument();
        expect(
            screen.getByText('2 of 2 files rejected. Rescans keep them unmatched until you restore them.'),
        ).toBeInTheDocument();
    });

    it.each([
        ['Reject selected', 'Import selected'],
        ['Import selected', 'Reject selected'],
    ])('SelectedActions_DisableTheOtherAction_WhenOneRuns (%s)', async (running, other) => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockReturnValue(new Promise(() => {}));
        bulkAcceptMutateAsync.mockReturnValue(new Promise(() => {}));
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            { wrapper: createTestWrapper() },
        );
        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i }));

        // Act
        await user.click(screen.getByRole('button', { name: running }));

        // Assert
        expect(screen.getByRole('button', { name: other })).toBeDisabled();
    });

    it('ScopeAction_HidesWhileFilesAreSelected_WhenAFileIsSelected', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched()]}
                emptyState={<div />}
                requestAccept={vi.fn()}
                scopeAction={<button type="button">Retry failed (1)</button>}
            />,
            { wrapper: createTestWrapper() },
        );
        expect(screen.getByRole('button', { name: 'Retry failed (1)' })).toBeInTheDocument();

        // Act
        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select s01e01/i }));

        // Assert
        expect(screen.queryByRole('button', { name: 'Retry failed (1)' })).not.toBeInTheDocument();
        expect(screen.getByText('1 selected')).toBeInTheDocument();
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

    it('ImportSelected_LeavesOutASelectedFile_WhenItIsNoLongerSubmittable', async () => {
        // Arrange
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

        // Act
        await user.click(screen.getByRole('button', { name: 'Import selected' }));

        // Assert
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

    it('RowMenuPreview_OpensThePreviewAndKeepsTheTrigger_WhenChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        const trigger = within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' });
        await user.click(trigger);

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Preview' }));

        // Assert
        expect(await screen.findByTestId('preview-drawer-open')).toBeInTheDocument();
        expect(trigger).toBeInTheDocument();
    });

    it('RowMenuMatch_OpensTheManualMatchForTheFile_WhenTheFileHasNoMatch', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<CandidateLeafTable items={[buildUnmatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('empty-1')).getByRole('button', { name: 'Actions for S01E02.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Match' }));

        // Assert
        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog-open')).toHaveTextContent('candidate');
        });
        expect(manualMatchProp?.kind).toBe('candidate');
        expect(manualMatchProp?.kind === 'candidate' && manualMatchProp.candidate.id).toBe('empty-1');
        expect(manualMatchSuppressesToast).toBe(false);
    });

    it('RowMenuMatch_KeepsTheMatchToast_WhenTheFileOfTheDetailsLeavesTheList', async () => {
        // Arrange
        const user = userEvent.setup();
        const { rerender } = render(
            <CandidateLeafTable
                items={[buildMatched(), buildUnmatched()]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'S01E01.mkv' }));
        rerender(<CandidateLeafTable items={[buildUnmatched()]} emptyState={<div />} requestAccept={vi.fn()} />);
        await user.click(within(rowFor('empty-1')).getByRole('button', { name: 'Actions for S01E02.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Match' }));

        // Assert
        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog-open')).toHaveTextContent('candidate');
        });
        expect(manualMatchSuppressesToast).toBe(false);
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

    it('CardLayout_PutsTheFilenameAndTheFileMenuInTheCardHeader_WhenTheTableCollapses', () => {
        // Arrange
        const items = [buildMatched()];

        // Act
        render(
            <CandidateLeafTable
                items={items}
                emptyState={<div />}
                requestAccept={(action) => void action('Inherit')}
            />,
            {
                wrapper: createTestWrapper(),
            },
        );

        // Assert
        expect(document.querySelector('td[data-column="select"]')).toHaveAttribute('data-mobile-role', 'hidden');
        expect(document.querySelector('td[data-column="filename"]')).toHaveAttribute('data-mobile-role', 'header');
        expect(document.querySelector('td[data-column="match"]')).toHaveAttribute('data-mobile-role', 'field');
        expect(document.querySelector('td[data-column="status"]')).toHaveAttribute('data-mobile-role', 'field');
        expect(document.querySelector('td[data-column="actions"]')).toHaveAttribute('data-mobile-role', 'header');
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

        await user.click(screen.getByRole('button', { name: 'Select', pressed: true }));
        await user.click(screen.getByRole('button', { name: 'Select' }));

        expect(screen.getByRole('checkbox', { name: /select s01e01/i })).not.toBeChecked();
    });

    it('RowMenuRestore_RestoresTheFile_WhenTheFileIsSilenced', async () => {
        // Arrange
        const user = userEvent.setup();
        restoreMutateAsync.mockResolvedValue({ count: 1 });
        render(
            <CandidateLeafTable
                items={[buildUnmatched({ id: 'rejected-1', status: 'Rejected' })]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );
        const row = rowFor('rejected-1');
        await user.click(within(row).getByRole('button', { name: 'Actions for S01E02.mkv' }));
        expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['View details', 'Restore']);

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Restore' }));

        // Assert
        expect(within(row).getByRole('button', { name: 'Silenced: S01E02.mkv. View details' })).toBeInTheDocument();
        expect(within(row).getByRole('checkbox', { name: /select s01e02/i })).toBeDisabled();
        expect(restoreMutateAsync).toHaveBeenCalledWith({ ids: ['rejected-1'] });
    });

    it('RowMenuReject_RejectsTheFile_WhenTheFileIsPending', async () => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockResolvedValue({ count: 1, error: null });
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Reject' }));

        // Assert
        expect(rejectMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'] });
    });

    it('RowMenu_OffersNoReject_WhenAnImportOwnsTheFile', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildMatched({ status: 'Queued' })]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );

        const status = within(rowFor('match-1')).getByRole('button', { name: 'Queued: S01E01.mkv. View details' });

        // Act
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Assert
        expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['View details', 'Preview']);
        expect(status).toBeInTheDocument();
    });

    it('RowMenu_OffersNoReject_WhenTheFileIsInvalidated', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <CandidateLeafTable
                items={[buildUnmatched({ status: 'Invalidated' })]}
                emptyState={<div />}
                requestAccept={vi.fn()}
            />,
            { wrapper: createTestWrapper() },
        );

        // Act
        await user.click(within(rowFor('empty-1')).getByRole('button', { name: 'Actions for S01E02.mkv' }));

        // Assert
        expect(screen.queryByRole('menuitem', { name: 'Reject' })).toBeNull();
    });

    it('RowMenuReject_Warns_WhenTheServerRejectsNoFile', async () => {
        // Arrange
        const user = userEvent.setup();
        rejectMutateAsync.mockResolvedValue({ count: 0, error: null });
        render(<CandidateLeafTable items={[buildMatched()]} emptyState={<div />} requestAccept={vi.fn()} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('match-1')).getByRole('button', { name: 'Actions for S01E01.mkv' }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Reject' }));

        // Assert
        expect(await screen.findByText('Match not rejected')).toBeInTheDocument();
        expect(screen.queryByText('Match rejected')).toBeNull();
    });
});
