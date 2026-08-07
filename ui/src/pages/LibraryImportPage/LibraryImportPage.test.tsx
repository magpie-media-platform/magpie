import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiRootFolder } from '@/hooks/useRootFolders';
import type { LibraryImportCandidate, LibraryImportCandidateQueryParams } from '@/hooks/useLibraryImportCandidates';
import type { ImportScanControllerValue } from './useImportScanController';

const useLibraryImportCandidatesMock = vi.fn();
const bulkAcceptMutateAsync = vi.fn();
const refetchMock = vi.fn().mockResolvedValue({});
let manualMatchProp: LibraryImportCandidate | null = null;

vi.mock('@/hooks/useLibraryImportCandidates', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useLibraryImportCandidates')>();
    return {
        ...actual,
        useLibraryImportCandidates: (params: LibraryImportCandidateQueryParams) =>
            useLibraryImportCandidatesMock(params),
    };
});

vi.mock('@/hooks/useBulkAcceptLibraryImportCandidates', () => ({
    useBulkAcceptLibraryImportCandidates: () => ({ mutateAsync: bulkAcceptMutateAsync, isPending: false }),
}));

const moviesFolder: ApiRootFolder = {
    id: 'folder-1',
    name: 'Movies',
    path: '/media/movies',
    contentType: 'movie',
    isDefault: true,
    defaultMonitoring: true,
    upgradeFileHandling: 'Replace',
    defaults: { movie: { qualityTierListId: 'quality-default' } },
    createdAt: '2024-01-01T00:00:00Z',
};

const tvFolder: ApiRootFolder = {
    id: 'folder-2',
    name: 'TV',
    path: '/media/tv',
    contentType: 'tv',
    isDefault: true,
    defaultMonitoring: true,
    upgradeFileHandling: 'Replace',
    defaults: { series: { qualityTierListId: 'quality-default' } },
    createdAt: '2024-01-01T00:00:00Z',
};

let activeFolders: ApiRootFolder[] = [moviesFolder];

vi.mock('@/hooks/useRootFolders', () => ({
    useRootFolders: () => ({ folders: activeFolders, isLoading: false, refetch: vi.fn() }),
}));

vi.mock('@/hooks/useUsers', () => ({
    useCurrentUser: () => ({ currentUser: null }),
}));

const scanController: ImportScanControllerValue = {
    isScanning: false,
    executionId: null,
    progress: null,
    statusMessage: null,
    lastSummary: null,
    isStarting: false,
    startScan: vi.fn().mockResolvedValue(undefined),
    startScanAll: vi.fn().mockResolvedValue(undefined),
    clearSummary: vi.fn(),
};
vi.mock('./useImportScanController', () => ({
    useImportScanController: () => scanController,
}));

vi.mock('./ManualMatchDialog', () => ({
    ManualMatchDialog: ({ match }: { match: LibraryImportCandidate | null }) => {
        manualMatchProp = match;
        return match ? <div data-testid="manual-match-dialog-open">{match.id}</div> : null;
    },
}));

vi.mock('@/components/content/PreviewDrawer', () => ({
    PreviewDrawer: ({ open }: { open: boolean }) => (open ? <div data-testid="preview-drawer-open" /> : null),
}));

import { LibraryImportPage } from './LibraryImportPage';

function installMatchMedia(width: number) {
    vi.stubGlobal(
        'matchMedia',
        vi.fn((query: string) => {
            const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
            const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);

            return {
                matches:
                    (minWidth === null || width >= Number(minWidth[1])) &&
                    (maxWidth === null || width <= Number(maxWidth[1])),
                media: query,
                onchange: null,
                addListener: () => {},
                removeListener: () => {},
                addEventListener: () => {},
                removeEventListener: () => {},
                dispatchEvent: () => false,
            };
        }),
    );
}

function buildMatched(overrides: Partial<LibraryImportCandidate> = {}): LibraryImportCandidate {
    return {
        id: 'match-1',
        rootFolderId: 'folder-1',
        scanRunId: 'scan-1',
        filePath: '/media/movies/Fight.Club.1999.mkv',
        contentType: 'movie',
        status: 'Pending',
        parsedSignals: { title: ['Fight Club'], year: ['1999'] },
        matches: [
            {
                confidence: 0.95,
                providerType: 'provider-a',
                externalIdKey: 'provider-a.movie',
                externalId: '550',
                title: 'Fight Club',
                subtitle: '1999',
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
        filePath: '/media/movies/Unknown.File.mkv',
        parsedSignals: { title: ['Unknown File'] },
        matches: [],
        matchCount: 0,
        ...overrides,
    });
}

function setItems(items: LibraryImportCandidate[], totalItems = items.length) {
    useLibraryImportCandidatesMock.mockImplementation((params?: LibraryImportCandidateQueryParams) => ({
        items,
        pagination: { pageNumber: params?.pageNumber ?? 1, pageSize: 25, totalItems },
        isLoading: false,
        isFetching: false,
        isError: false,
        error: null,
        refetch: refetchMock,
    }));
}

function rowFor(matchId: string): HTMLElement {
    const cell = document.querySelector(`.library-import-page__file-cell[data-match-id="${matchId}"]`);
    const row = cell?.closest('tr');
    if (!row) {
        throw new Error(`No row found for match ${matchId}`);
    }
    return row as HTMLElement;
}

function createTestWrapper(initialEntry = '/library/import') {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={[initialEntry]}>
                    <TooltipProvider>
                        <ToastStateProvider>
                            <ToastProvider>
                                {children}
                                <ToastList />
                                <ToastViewport />
                            </ToastProvider>
                        </ToastStateProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

describe('LibraryImportPage', () => {
    beforeEach(() => {
        installMatchMedia(1024);
        vi.clearAllMocks();
        manualMatchProp = null;
        activeFolders = [moviesFolder];
        scanController.isScanning = false;
        scanController.isStarting = false;
        scanController.progress = null;
        scanController.statusMessage = null;
        scanController.lastSummary = null;
        bulkAcceptMutateAsync.mockResolvedValue({
            queued: [{ candidateId: 'match-1', jobExecutionId: 'job-1' }],
            skipped: [],
        });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it('renders the new single-table heading without the bucket tabs or intro paragraph', async () => {
        setItems([buildMatched()]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { level: 1, name: /library import/i })).toBeInTheDocument();
        });
        expect(document.querySelector('[data-view-tab]')).toBeNull();
        expect(screen.queryByText(/auto-accept/i)).toBeNull();
        expect(screen.queryByTestId('candidate-confidence')).toBeNull();
    });

    it('renders a matched row with its candidate, a Detail and Import action, and an enabled checkbox', async () => {
        setItems([buildMatched()]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(rowFor('match-1')).toBeInTheDocument();
        });
        const row = rowFor('match-1');
        expect(row).toHaveTextContent('Fight Club');
        expect(row).toHaveTextContent('provider-a');
        expect(within(row).getByRole('button', { name: /^Detail$/ })).toBeInTheDocument();
        expect(within(row).getByRole('button', { name: /^Import$/ })).toBeInTheDocument();
        expect(within(row).queryByRole('button', { name: /^Match$/ })).toBeNull();
        const checkbox = within(row).getByRole('checkbox', { name: /select fight\.club/i });
        expect(checkbox).not.toBeDisabled();
    });

    it('renders an unmatched row as "No match" with only a Match action and a disabled checkbox', async () => {
        setItems([buildUnmatched()]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(rowFor('empty-1')).toBeInTheDocument();
        });
        const row = rowFor('empty-1');
        expect(within(row).getByTestId('library-import-no-match')).toHaveTextContent('No match');
        expect(within(row).getByRole('button', { name: /^Match$/ })).toBeInTheDocument();
        expect(within(row).queryByRole('button', { name: /^Import$/ })).toBeNull();
        expect(within(row).queryByRole('button', { name: /^Detail$/ })).toBeNull();
        const checkbox = within(row).getByRole('checkbox', { name: /select unknown\.file/i });
        expect(checkbox).toBeDisabled();
    });

    it('queues a matched row for import via the shared bulk submission hook', async () => {
        const user = userEvent.setup();
        setItems([buildMatched()]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());
        await user.click(within(rowFor('match-1')).getByRole('button', { name: /^Import$/ }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'] });
        });
        expect(refetchMock).toHaveBeenCalled();
    });

    it('shows a disabled queued state for an in-flight candidate and excludes it from selection', async () => {
        setItems([buildMatched({ id: 'match-1', status: 'Queued' })]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());
        const row = rowFor('match-1');
        expect(within(row).queryByRole('button', { name: /^Import$/ })).toBeNull();
        expect(within(row).getByText(/import queued/i)).toBeInTheDocument();
        expect(within(row).getByRole('checkbox')).toBeDisabled();
    });

    it('bulk-imports the checked matched rows via the bulk-accept mutation', async () => {
        const user = userEvent.setup();
        setItems([buildMatched(), buildMatched({ id: 'match-2', filePath: '/media/movies/Heat.1995.mkv' })]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());
        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select fight\.club/i }));

        const acceptChecked = await screen.findByRole('button', { name: /import checked/i });
        expect(document.querySelector('[data-component="library-import-bulk-bar"]')).toBeTruthy();

        await user.click(acceptChecked);

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1'] });
        });
    });

    it('extends to select-all-across-pages and bulk-accepts via the filter', async () => {
        const user = userEvent.setup();
        setItems([buildMatched()], 30);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());
        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select fight\.club/i }));

        const extend = await screen.findByRole('button', { name: /select all 30 across all pages/i });
        await user.click(extend);

        expect(screen.getByText(/all 30 matching files across all pages are selected/i)).toBeInTheDocument();

        expect(useLibraryImportCandidatesMock).toHaveBeenCalledWith(
            expect.objectContaining({ status: 'Pending', hasMatches: true, pageSize: 1 }),
        );

        await user.click(screen.getByRole('button', { name: /import checked/i }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({
                filter: { rootFolderId: undefined, excludeIds: [] },
            });
        });
    });

    it('scopes the across-pages bulk-accept filter to the selected root folder', async () => {
        const user = userEvent.setup();
        setItems([buildMatched()], 30);
        render(<LibraryImportPage />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-1'),
        });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());
        await user.click(within(rowFor('match-1')).getByRole('checkbox', { name: /select fight\.club/i }));
        await user.click(await screen.findByRole('button', { name: /select all 30 across all pages/i }));
        await user.click(screen.getByRole('button', { name: /import checked/i }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({
                filter: { rootFolderId: 'folder-1', excludeIds: [] },
            });
        });
    });

    it('exposes a card-list select-all control that selects every matched row on the page', async () => {
        installMatchMedia(900);
        const user = userEvent.setup();
        setItems([buildMatched(), buildMatched({ id: 'match-2', filePath: '/media/movies/Heat.1995.mkv' })]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());
        expect(document.querySelector('[data-page="library-import"]')).toHaveAttribute('data-layout', 'card');

        const selectAll = screen.getByRole('checkbox', { name: /select all matched files on this page/i });
        await user.click(selectAll);

        expect(within(rowFor('match-1')).getByRole('checkbox', { name: /select fight\.club/i })).toBeChecked();
        expect(within(rowFor('match-2')).getByRole('checkbox', { name: /select heat/i })).toBeChecked();

        await user.click(await screen.findByRole('button', { name: /import checked/i }));
        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['match-1', 'match-2'] });
        });
    });

    it('card-list select-all on a full page surfaces the across-pages prompt, then clears', async () => {
        installMatchMedia(900);
        const user = userEvent.setup();
        setItems([buildMatched()], 30);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());

        const selectAll = screen.getByRole('checkbox', { name: /select all matched files on this page/i });
        await user.click(selectAll);

        expect(await screen.findByRole('button', { name: /select all 30 across all pages/i })).toBeInTheDocument();

        await user.click(selectAll);
        await waitFor(() => {
            expect(document.querySelector('[data-component="library-import-bulk-bar"]')).toBeNull();
        });
    });

    it('does not render the card-list select-all control when there are no matched rows', async () => {
        installMatchMedia(900);
        setItems([buildUnmatched()]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('empty-1')).toBeInTheDocument());
        expect(document.querySelector('[data-component="library-import-card-toolbar"]')).toBeNull();
    });

    it('opens the manual-match modal for an unmatched row', async () => {
        const user = userEvent.setup();
        setItems([buildUnmatched()]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('empty-1')).toBeInTheDocument());
        await user.click(within(rowFor('empty-1')).getByRole('button', { name: /^Match$/ }));

        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog-open')).toHaveTextContent('empty-1');
        });
        expect(manualMatchProp?.id).toBe('empty-1');
    });

    it('opens the preview drawer for a matched row Detail action', async () => {
        const user = userEvent.setup();
        setItems([buildMatched()]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());
        await user.click(within(rowFor('match-1')).getByRole('button', { name: /^Detail$/ }));

        await waitFor(() => {
            expect(screen.getByTestId('preview-drawer-open')).toBeInTheDocument();
        });
    });

    it('disables the root-folder select and Scan button while scanning, then re-enables both once it finishes', async () => {
        setItems([]);
        scanController.isScanning = true;
        const { rerender } = render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^Scan$/ })).toBeDisabled();
        });
        expect(document.querySelector('[data-filter="root-folder"]')).toBeDisabled();

        scanController.isScanning = false;
        rerender(<LibraryImportPage />);

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^Scan$/ })).not.toBeDisabled();
        });
        expect(document.querySelector('[data-filter="root-folder"]')).not.toBeDisabled();
    });

    it('enables scanning of the sole root folder when none is explicitly selected', async () => {
        setItems([]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByTestId('library-import-empty')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /^Scan$/ })).not.toBeDisabled();
        expect(screen.queryByTestId('library-import-scan-hint')).toBeNull();
        expect(screen.getByTestId('library-import-empty')).toHaveTextContent(/run a folder scan/i);
    });

    it('enables scan and clears the pick-a-folder guidance once a root folder is selected', async () => {
        setItems([]);
        render(<LibraryImportPage />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-1'),
        });

        await waitFor(() => {
            expect(screen.getByTestId('library-import-empty')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /^Scan$/ })).not.toBeDisabled();
        expect(screen.queryByTestId('library-import-scan-hint')).toBeNull();

        expect(screen.getByTestId('library-import-empty')).toHaveTextContent(
            /no files to import\. run a folder scan\./i,
        );
    });

    it('enables Scan on the default "All root folders" selection with multiple folders', async () => {
        activeFolders = [moviesFolder, tvFolder];
        setItems([]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByTestId('library-import-empty')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /^Scan$/ })).not.toBeDisabled();
        expect(screen.queryByTestId('library-import-scan-hint')).toBeNull();
        expect(screen.getByTestId('library-import-empty')).not.toHaveTextContent(/select a root folder/i);
        expect(screen.getByTestId('library-import-empty')).toHaveTextContent(/run a folder scan/i);
    });

    it('scans every root folder when Scan is clicked on the default "All" selection', async () => {
        const user = userEvent.setup();
        activeFolders = [moviesFolder, tvFolder];
        setItems([]);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(screen.getByTestId('library-import-empty')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /^Scan$/ }));

        await waitFor(() => {
            expect(scanController.startScanAll).toHaveBeenCalledWith([
                { id: 'folder-1', name: 'Movies' },
                { id: 'folder-2', name: 'TV' },
            ]);
        });
        expect(scanController.startScan).not.toHaveBeenCalled();
    });

    it('scans only the chosen folder when a specific root folder is selected', async () => {
        const user = userEvent.setup();
        activeFolders = [moviesFolder, tvFolder];
        setItems([]);
        render(<LibraryImportPage />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-2'),
        });

        await waitFor(() => expect(screen.getByTestId('library-import-empty')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /^Scan$/ }));

        await waitFor(() => {
            expect(scanController.startScan).toHaveBeenCalledWith('folder-2', 'TV');
        });
        expect(scanController.startScanAll).not.toHaveBeenCalled();
    });

    it('pages server-side: clicking next requests the next page', async () => {
        const user = userEvent.setup();
        setItems([buildMatched()], 30);
        render(<LibraryImportPage />, { wrapper: createTestWrapper() });

        await waitFor(() => expect(rowFor('match-1')).toBeInTheDocument());

        expect(useLibraryImportCandidatesMock).toHaveBeenCalledWith(
            expect.objectContaining({ pageNumber: 1, pageSize: 25 }),
        );

        await user.click(screen.getByRole('button', { name: /go to next page/i }));

        await waitFor(() => {
            expect(useLibraryImportCandidatesMock).toHaveBeenCalledWith(
                expect.objectContaining({ pageNumber: 2, pageSize: 25 }),
            );
        });
    });
});
