import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect, type ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { ApiError } from '@/lib/api';
import type { ApiRootFolder } from '@/hooks/useRootFolders';
import type {
    LibraryImportCandidateGroup,
    LibraryImportCandidateGroupQueryParams,
} from '@/hooks/useLibraryImportCandidateGroups';
import type { ManualMatchTarget } from './ManualMatchDialog';
import type { ImportScanControllerValue } from './useImportScanController';

const useLibraryImportCandidateGroupsMock = vi.fn();
const useLibraryImportGroupCandidatesMock = vi.fn();
const acceptGroupMutateAsync = vi.fn();
const bulkAcceptGroupsMutateAsync = vi.fn();
const refetchMock = vi.fn().mockResolvedValue({});
let manualMatchProp: ManualMatchTarget | null = null;
let autoSelectMonitoring = true;

vi.mock('@/hooks/useLibraryImportCandidateGroups', () => ({
    useLibraryImportCandidateGroups: (params: LibraryImportCandidateGroupQueryParams) =>
        useLibraryImportCandidateGroupsMock(params),
}));

vi.mock('@/hooks/useLibraryImportGroupCandidates', () => ({
    useLibraryImportGroupCandidates: (params: unknown) => useLibraryImportGroupCandidatesMock(params),
}));

vi.mock('@/hooks/useAcceptLibraryImportCandidateGroup', () => ({
    useAcceptLibraryImportCandidateGroup: () => ({
        mutateAsync: acceptGroupMutateAsync,
        isPending: false,
        invalidate: vi.fn(),
    }),
}));

vi.mock('@/hooks/useBulkAcceptLibraryImportCandidateGroups', () => ({
    useBulkAcceptLibraryImportCandidateGroups: () => ({ mutateAsync: bulkAcceptGroupsMutateAsync, isPending: false }),
}));

vi.mock('./AcceptImportDialog', () => ({
    AcceptImportDialog: ({ action, onClose }: { action: ((mode: 'Inherit') => void) | null; onClose: () => void }) => {
        useEffect(() => {
            if (action && autoSelectMonitoring) action('Inherit');
        }, [action]);
        return action && !autoSelectMonitoring ? (
            <div role="dialog" aria-label="Accept import">
                <button onClick={() => action('Inherit')}>Choose Inherit and accept</button>
                <button onClick={onClose}>Cancel</button>
            </div>
        ) : null;
    },
}));

const moviesFolder: ApiRootFolder = {
    id: 'folder-1',
    name: 'Movies',
    path: '/media/movies',
    contentType: 'movie',
    isDefault: true,
    defaultMonitoring: 'Monitored',
    upgradeFileHandling: 'Replace',
    autoImport: false,
    defaults: { movie: { qualityTierListId: 'quality-default' } },
    createdAt: '2024-01-01T00:00:00Z',
};

const tvFolder: ApiRootFolder = {
    id: 'folder-2',
    name: 'TV',
    path: '/media/tv',
    contentType: 'tv',
    isDefault: true,
    defaultMonitoring: 'Monitored',
    upgradeFileHandling: 'Replace',
    autoImport: false,
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
    ManualMatchDialog: ({ target, onMatched }: { target: ManualMatchTarget | null; onMatched: () => void }) => {
        manualMatchProp = target;
        return target ? (
            <div data-testid="manual-match-dialog-open">
                {target.kind}
                <button onClick={onMatched}>Confirm match</button>
            </div>
        ) : null;
    },
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

function buildGroup(overrides: Partial<LibraryImportCandidateGroup> = {}): LibraryImportCandidateGroup {
    return {
        groupKey: '/media/tv/Bluey',
        kind: 'Directory',
        sourcePath: '/media/tv/Bluey',
        title: 'Bluey',
        contentType: 'tv',
        subtitle: '2018',
        match: {
            confidence: 1,
            providerType: 'tvmaze',
            externalIdKey: 'tvmaze',
            externalId: '44796',
            title: 'Bluey',
            subtitle: '2018',
        },
        leafCount: 157,
        topLevelSlug: 'show',
        leafLevelSlug: 'episode',
        leafLevelDisplayName: 'Episode',
        childLevelSlug: 'season',
        childLevelDisplayName: 'Season',
        statusCounts: { Pending: 157 },
        ...overrides,
    };
}

function buildUnmatchedGroup(overrides: Partial<LibraryImportCandidateGroup> = {}): LibraryImportCandidateGroup {
    return buildGroup({
        groupKey: 'query:unknown show',
        kind: 'Query',
        sourcePath: null,
        title: 'Unknown Show',
        subtitle: null,
        match: null,
        statusCounts: { Pending: 3 },
        ...overrides,
    });
}

function setGroups(items: LibraryImportCandidateGroup[], totalItems = items.length) {
    useLibraryImportCandidateGroupsMock.mockImplementation((params?: LibraryImportCandidateGroupQueryParams) => ({
        items,
        pagination: { pageNumber: params?.pageNumber ?? 1, pageSize: 25, totalItems },
        isLoading: false,
        isFetching: false,
        isError: false,
        error: null,
        refetch: refetchMock,
    }));
}

function rowFor(groupKey: string): HTMLElement {
    const cell = [...document.querySelectorAll('[data-group-key]')].find(
        (el) => el.getAttribute('data-group-key') === groupKey,
    );
    const row = cell?.closest('tr');
    if (!row) {
        throw new Error(`No row found for group ${groupKey}`);
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
        useLibraryImportGroupCandidatesMock.mockImplementation((params: { groupKey?: string } | null) => ({
            items: params?.groupKey?.startsWith('query:')
                ? [
                      { filePath: '/media/tv/Unknown Show/episode-1.mkv' },
                      { filePath: '/media/tv/Unknown Show/episode-2.mkv' },
                  ]
                : [],
        }));
        installMatchMedia(1024);
        vi.clearAllMocks();
        autoSelectMonitoring = true;
        manualMatchProp = null;
        activeFolders = [moviesFolder];
        scanController.isScanning = false;
        scanController.isStarting = false;
        scanController.progress = null;
        scanController.statusMessage = null;
        scanController.lastSummary = null;
        acceptGroupMutateAsync.mockResolvedValue({
            queued: [{ candidateId: 'c-1', jobExecutionId: 'job-1' }],
            skipped: [],
        });
        bulkAcceptGroupsMutateAsync.mockResolvedValue({
            queued: [{ candidateId: 'c-1', jobExecutionId: 'job-1' }],
            skipped: [],
        });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it('shows the scan summary as unavailable when the count request fails', () => {
        // Arrange
        setGroups([]);
        scanController.lastSummary = { rootFolderName: 'Archive', outcome: 'completed', toImport: null };

        // Act
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        // Assert
        expect(screen.getByText('Scan of "Archive" completed. The import count is unavailable.')).toBeInTheDocument();
        expect(screen.queryByText(/is up to date/)).not.toBeInTheDocument();
    });

    it('renders the heading without the old bucket tabs or intro paragraph', async () => {
        setGroups([buildGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByRole('heading', { level: 1, name: /library import/i })).toBeInTheDocument();
        });
        expect(document.querySelector('[data-view-tab]')).toBeNull();
    });

    it('renders a matched group row with its title, count, and leaf display name from the DTO', async () => {
        setGroups([buildGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        const row = rowFor('/media/tv/Bluey');
        expect(row).toHaveTextContent('Bluey');
        expect(row).toHaveTextContent('tvmaze');
        expect(row).toHaveTextContent('Episode: 157');
        expect(within(row).getByRole('button', { name: /^Accept$/ })).toBeInTheDocument();
        expect(within(row).getByRole('link', { name: 'Bluey 2018' })).toBeInTheDocument();
        expect(within(row).queryByRole('button', { name: /^Season$/ })).toBeNull();
        expect(within(row).queryByRole('button', { name: /^Match$/ })).toBeNull();
    });

    it('shows a directory group key as its source path', () => {
        setGroups([buildGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        expect(within(rowFor('/media/tv/Bluey')).getByTitle('/media/tv/Bluey')).toBeInTheDocument();
    });

    it('never exposes the match token as a path on a matched group with no directory', () => {
        setGroups([
            buildGroup({
                groupKey: 'match:movies:tmdb:tmdbId:603',
                kind: 'Match',
                sourcePath: null,
                title: 'The Matrix',
            }),
        ]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        const row = rowFor('match:movies:tmdb:tmdbId:603');
        expect(row).not.toHaveTextContent('match:movies:tmdb:tmdbId:603');
        expect(within(row).queryByTitle('match:movies:tmdb:tmdbId:603')).toBeNull();
    });

    it('never exposes the synthesized query token as a path on a query group', () => {
        setGroups([buildUnmatchedGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        const row = rowFor('query:unknown show');
        expect(row).not.toHaveTextContent('query:unknown show');
        expect(within(row).queryByTitle('query:unknown show')).toBeNull();
    });

    it('GroupLink_OpensTheFiles_WhenTheGroupHasOneLevel', async () => {
        // Arrange
        setGroups([
            buildGroup({
                groupKey: 'movie-1',
                title: '127 Hours',
                topLevelSlug: 'movie',
                leafLevelSlug: 'movie',
                leafLevelDisplayName: 'Movie',
                leafCount: 1,
            }),
        ]);

        // Act
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        // Assert
        await waitFor(() => expect(rowFor('movie-1')).toBeInTheDocument());
        const row = rowFor('movie-1');
        expect(within(row).getByRole('button', { name: /^Accept$/ })).toBeInTheDocument();
        expect(within(row).getByRole('link', { name: '127 Hours 2018' })).toHaveAttribute(
            'href',
            '/library/import?key=movie-1&title=127+Hours',
        );
    });

    it('renders a single-leaf group count without an s (singular DTO display name)', async () => {
        setGroups([buildGroup({ groupKey: 'movie-1', leafCount: 1, leafLevelDisplayName: 'Movie' })]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('movie-1')).toBeInTheDocument());
        expect(rowFor('movie-1')).toHaveTextContent('Movie: 1');
    });

    it('disables Accept for a matched group whose candidates are all already queued', async () => {
        setGroups([buildGroup({ groupKey: 'all-queued', statusCounts: { Queued: 157 } })]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('all-queued')).toBeInTheDocument());
        expect(within(rowFor('all-queued')).getByRole('button', { name: /^Accept$/ })).toBeDisabled();
    });

    it('enables Accept for a matched group with a pending candidate', async () => {
        setGroups([buildGroup({ groupKey: 'has-pending', statusCounts: { Pending: 1, Queued: 156 } })]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('has-pending')).toBeInTheDocument());
        expect(within(rowFor('has-pending')).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();
    });

    it('enables Accept for a matched group with only a failed candidate', async () => {
        setGroups([buildGroup({ groupKey: 'has-failed', statusCounts: { Failed: 1 } })]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('has-failed')).toBeInTheDocument());
        expect(within(rowFor('has-failed')).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();
    });

    it('accepting a matched group calls the group-accept endpoint with its key and the active root folder', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-2'),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('button', { name: /^Accept$/ }));

        await waitFor(() => {
            expect(acceptGroupMutateAsync).toHaveBeenCalledWith({
                groupKey: '/media/tv/Bluey',
                rootFolderId: 'folder-2',
                monitoringMode: 'Inherit',
            });
        });
    });

    it('requires monitoring before accepting and uses the chosen mode', async () => {
        const user = userEvent.setup();
        autoSelectMonitoring = false;
        setGroups([buildGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        const accept = within(rowFor('/media/tv/Bluey')).getByRole('button', { name: /^Accept$/ });
        await user.click(accept);

        expect(screen.getByRole('dialog', { name: 'Accept import' })).toBeInTheDocument();
        expect(acceptGroupMutateAsync).not.toHaveBeenCalled();

        await user.click(screen.getByRole('button', { name: 'Choose Inherit and accept' }));

        await waitFor(() => {
            expect(acceptGroupMutateAsync).toHaveBeenCalledWith({
                groupKey: '/media/tv/Bluey',
                monitoringMode: 'Inherit',
            });
        });
    });

    it('shows a gone-group toast when accepting a group 404s (the group no longer exists)', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup()]);
        acceptGroupMutateAsync.mockRejectedValueOnce(new ApiError(404, 'Not Found'));
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('button', { name: /^Accept$/ }));

        await waitFor(() => {
            expect(screen.getByText('Group is gone')).toBeInTheDocument();
        });
    });

    it('keeps the generic failure toast when accepting a group fails with a non-404 status', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup()]);
        acceptGroupMutateAsync.mockRejectedValueOnce(new ApiError(500, 'Internal Server Error'));
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('button', { name: /^Accept$/ }));

        await waitFor(() => {
            expect(screen.getByText('Import failed')).toBeInTheDocument();
        });
        expect(screen.queryByText('Group is gone')).not.toBeInTheDocument();
    });

    it('renders an unmatched group as "No match" with only a Match action, no Accept', async () => {
        setGroups([buildUnmatchedGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('query:unknown show')).toBeInTheDocument());
        const row = rowFor('query:unknown show');
        expect(row).toHaveTextContent('No match');
        expect(within(row).getByRole('button', { name: /^Match$/ })).toBeInTheDocument();
        expect(within(row).queryByRole('button', { name: /^Accept$/ })).toBeNull();
    });

    it('opens the manual-match dialog targeting the group for an unmatched row', async () => {
        const user = userEvent.setup();
        setGroups([buildUnmatchedGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('query:unknown show')).toBeInTheDocument());
        await user.click(within(rowFor('query:unknown show')).getByRole('button', { name: /^Match$/ }));

        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog-open')).toHaveTextContent('group');
        });
        expect(manualMatchProp?.kind).toBe('group');
        expect(manualMatchProp?.kind === 'group' && manualMatchProp.group.groupKey).toBe('query:unknown show');
    });

    it('keeps the group Match button mounted while its manual-match dialog is open', async () => {
        const user = userEvent.setup();
        setGroups([buildUnmatchedGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('query:unknown show')).toBeInTheDocument());
        const match = within(rowFor('query:unknown show')).getByRole('button', { name: /^Match$/ });
        await user.click(match);

        expect(await screen.findByTestId('manual-match-dialog-open')).toBeInTheDocument();
        expect(match).toBeInTheDocument();
    });

    it('drops a selected group from the selection once it is matched, so the pick does not outlive its key', async () => {
        const user = userEvent.setup();
        setGroups([buildUnmatchedGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('query:unknown show')).toBeInTheDocument());
        await user.click(within(rowFor('query:unknown show')).getByRole('checkbox', { name: 'Select Unknown Show' }));
        expect(screen.getByText('1 selected')).toBeInTheDocument();

        await user.click(within(rowFor('query:unknown show')).getByRole('button', { name: /^Match$/ }));
        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog-open')).toBeInTheDocument();
        });
        await user.click(screen.getByRole('button', { name: 'Confirm match' }));

        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
    });

    it('GroupRow_ShowsTheSilencedCountAndOnlyItsLink_WhenEveryFileIsSilenced', async () => {
        // Arrange
        setGroups([buildUnmatchedGroup({ groupKey: 'silenced-group', statusCounts: { Rejected: 1 } })]);

        // Act
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        // Assert
        await waitFor(() => expect(rowFor('silenced-group')).toBeInTheDocument());
        const row = rowFor('silenced-group');
        expect(row).toHaveTextContent('1 Silenced');
        expect(within(row).queryByRole('button', { name: /^Match$/ })).toBeNull();
        expect(within(row).queryByRole('button', { name: /^Accept$/ })).toBeNull();
        expect(within(row).getByRole('link', { name: 'Unknown Show' })).toBeInTheDocument();
    });

    it('renders a matching-pending group as pending and still offers a manual Match', async () => {
        setGroups([buildUnmatchedGroup({ groupKey: 'pending-group', statusCounts: { MatchingPending: 3 } })]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('pending-group')).toBeInTheDocument());
        const row = rowFor('pending-group');
        expect(row).toHaveTextContent('Matching pending');
        expect(row).toHaveTextContent('3 Matching pending');
        expect(row).not.toHaveTextContent('MatchingPending');
        expect(within(row).getByRole('button', { name: /^Match$/ })).toBeEnabled();
        expect(within(row).queryByRole('button', { name: /^Accept$/ })).toBeNull();
        expect(within(row).getByRole('link', { name: 'Unknown Show' })).toBeInTheDocument();
    });

    it('GroupLink_KeepsTheRootFolderFilter_WhenTheQueueIsFiltered', async () => {
        // Arrange
        setGroups([buildGroup()]);

        // Act
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-2'),
        });

        // Assert
        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        expect(within(rowFor('/media/tv/Bluey')).getByRole('link', { name: 'Bluey 2018' })).toHaveAttribute(
            'href',
            expect.stringMatching(/^\/library\/import\?key=.*rootFolderId=folder-2/),
        );
    });

    it('disables the root-folder select and Scan button while scanning, then re-enables both once it finishes', async () => {
        setGroups([]);
        scanController.isScanning = true;
        const { rerender } = render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^Scan$/ })).toBeDisabled();
        });
        expect(document.querySelector('[data-filter="root-folder"]')).toBeDisabled();

        scanController.isScanning = false;
        rerender(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />);

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^Scan$/ })).not.toBeDisabled();
        });
        expect(document.querySelector('[data-filter="root-folder"]')).not.toBeDisabled();
    });

    it('scans only the chosen folder when a specific root folder is selected', async () => {
        const user = userEvent.setup();
        activeFolders = [moviesFolder, tvFolder];
        setGroups([]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-2'),
        });

        await waitFor(() => expect(screen.getByTestId('library-import-empty-filtered')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /^Scan$/ }));

        await waitFor(() => {
            expect(scanController.startScan).toHaveBeenCalledWith('folder-2', 'TV');
        });
        expect(scanController.startScanAll).not.toHaveBeenCalled();
    });

    it('bulk-accepts every selected eligible group and skips a selected ineligible group with a toast', async () => {
        const user = userEvent.setup();
        setGroups([
            buildGroup(),
            buildGroup({ groupKey: 'all-queued', title: 'Stale Show', statusCounts: { Queued: 157 } }),
        ]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());

        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('checkbox', { name: 'Select Bluey' }));
        await user.click(within(rowFor('all-queued')).getByRole('checkbox', { name: 'Select Stale Show' }));
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));

        await waitFor(() => {
            expect(acceptGroupMutateAsync).toHaveBeenCalledWith({
                groupKey: '/media/tv/Bluey',
                rootFolderId: undefined,
                monitoringMode: 'Inherit',
            });
        });
        expect(acceptGroupMutateAsync).not.toHaveBeenCalledWith(expect.objectContaining({ groupKey: 'all-queued' }));
        await waitFor(() => {
            expect(screen.getByText('Import partially queued')).toBeInTheDocument();
        });
        expect(screen.getByText('1 file queued for import. 1 group is not ready to import.')).toBeInTheDocument();
    });

    it('reports one success toast for a page-mode bulk accept where every selected group is queued', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup(), buildGroup({ groupKey: 'group-b', title: 'Group B' })]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(screen.getByRole('checkbox', { name: 'Select all groups' }));
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));

        await waitFor(() => {
            expect(screen.getByText('Import queued')).toBeInTheDocument();
        });
        expect(screen.getByText('2 files queued for import.')).toBeInTheDocument();
        expect(screen.queryByText('Import partially queued')).not.toBeInTheDocument();
    });

    it('BulkAccept_TargetsSelectedGroupsAcrossPages_WhenReturningToTheFirstPage', async () => {
        // Arrange
        const user = userEvent.setup();
        useLibraryImportCandidateGroupsMock.mockImplementation((params: LibraryImportCandidateGroupQueryParams) => ({
            items:
                params.pageNumber === 2
                    ? [buildGroup({ groupKey: '/media/tv/Other', sourcePath: '/media/tv/Other', title: 'Other' })]
                    : [buildGroup()],
            pagination: { pageNumber: params.pageNumber, pageSize: 25, totalItems: 30 },
            isLoading: false,
            isFetching: false,
            isError: false,
            error: null,
            refetch: refetchMock,
        }));
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        // Act
        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('checkbox', { name: 'Select Bluey' }));
        await user.click(screen.getByRole('button', { name: /go to next page/i }));
        await waitFor(() => expect(rowFor('/media/tv/Other')).toBeInTheDocument());
        await user.click(within(rowFor('/media/tv/Other')).getByRole('checkbox', { name: 'Select Other' }));
        await user.click(screen.getByRole('button', { name: /go to previous page/i }));
        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());

        // Assert
        expect(within(rowFor('/media/tv/Bluey')).getByRole('checkbox', { name: 'Select Bluey' })).toBeChecked();
        expect(screen.getByText('2 selected')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));
        await waitFor(() => expect(acceptGroupMutateAsync).toHaveBeenCalledTimes(2));
        expect(acceptGroupMutateAsync).toHaveBeenCalledWith({
            groupKey: '/media/tv/Bluey',
            rootFolderId: undefined,
            monitoringMode: 'Inherit',
        });
        expect(acceptGroupMutateAsync).toHaveBeenCalledWith({
            groupKey: '/media/tv/Other',
            rootFolderId: undefined,
            monitoringMode: 'Inherit',
        });
    });

    it('BulkAccept_UsesRefreshedReadiness_WhenSelectedGroupBecomesReady', async () => {
        // Arrange
        const user = userEvent.setup();
        setGroups([buildGroup({ match: null, statusCounts: { MatchingPending: 157 } })]);
        const { rerender } = render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('checkbox', { name: 'Select Bluey' }));

        // Act
        setGroups([buildGroup()]);
        rerender(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />);
        expect(within(rowFor('/media/tv/Bluey')).getByRole('button', { name: 'Accept' })).toBeEnabled();
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));

        // Assert
        await waitFor(() =>
            expect(acceptGroupMutateAsync).toHaveBeenCalledWith({
                groupKey: '/media/tv/Bluey',
                rootFolderId: undefined,
                monitoringMode: 'Inherit',
            }),
        );
    });

    it('BulkAccept_SkipsRefreshedGroup_WhenSelectedGroupBecomesNotReady', async () => {
        // Arrange
        const user = userEvent.setup();
        setGroups([buildGroup()]);
        const { rerender } = render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });
        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('checkbox', { name: 'Select Bluey' }));

        // Act
        setGroups([buildGroup({ match: null, statusCounts: { MatchingPending: 157 } })]);
        rerender(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />);
        expect(within(rowFor('/media/tv/Bluey')).queryByRole('button', { name: 'Accept' })).toBeNull();
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));

        // Assert
        await waitFor(() => expect(screen.getByText('Nothing queued for import')).toBeInTheDocument());
        expect(acceptGroupMutateAsync).not.toHaveBeenCalled();
    });

    it('drops the selection when the root-folder filter changes', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-1'),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(within(rowFor('/media/tv/Bluey')).getByRole('checkbox', { name: 'Select Bluey' }));
        expect(screen.getByText('1 selected')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /clear filters/i }));

        await waitFor(() => {
            expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
        });
    });

    it('gives the group title the mobile card header role instead of a labelled field row', async () => {
        setGroups([buildGroup()]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        expect(document.querySelector('td[data-column="title"]')).toHaveAttribute('data-mobile-role', 'header');
    });

    it('offers a filtered empty state with a clear-filters action when a root folder holds nothing', async () => {
        const user = userEvent.setup();
        setGroups([]);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-1'),
        });

        await waitFor(() => expect(screen.getByTestId('library-import-empty-filtered')).toBeInTheDocument());
        expect(screen.queryByTestId('library-import-empty')).toBeNull();

        await user.click(within(screen.getByTestId('library-import-empty-filtered')).getByRole('button'));

        await waitFor(() => expect(screen.getByTestId('library-import-empty')).toBeInTheDocument());
    });

    it('pages server-side: clicking next requests the next page', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup()], 30);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());

        expect(useLibraryImportCandidateGroupsMock).toHaveBeenCalledWith(
            expect.objectContaining({ pageNumber: 1, pageSize: 25 }),
        );

        await user.click(screen.getByRole('button', { name: /go to next page/i }));

        await waitFor(() => {
            expect(useLibraryImportCandidateGroupsMock).toHaveBeenCalledWith(
                expect.objectContaining({ pageNumber: 2, pageSize: 25 }),
            );
        });
    });

    it('accepting one group leaves another group Accept button enabled (concurrent accepts are independent)', async () => {
        const user = userEvent.setup();
        setGroups([
            buildGroup({ groupKey: 'group-a', title: 'Group A' }),
            buildGroup({ groupKey: 'group-b', title: 'Group B' }),
        ]);
        let resolveAccept: (value: { queued: unknown[]; skipped: unknown[] }) => void = () => {};
        acceptGroupMutateAsync.mockImplementationOnce(
            () =>
                new Promise((resolve) => {
                    resolveAccept = resolve;
                }),
        );
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('group-a')).toBeInTheDocument());
        await user.click(within(rowFor('group-a')).getByRole('button', { name: /^Accept$/ }));

        expect(within(rowFor('group-a')).getByRole('button', { name: /^Accept$/ })).toBeDisabled();
        expect(within(rowFor('group-b')).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();

        resolveAccept({ queued: [], skipped: [] });
        await waitFor(() => {
            expect(within(rowFor('group-a')).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();
        });
    });

    it('offers the select-all-across-pages banner after selecting every group on the loaded page', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup()], 30);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(screen.getByRole('checkbox', { name: 'Select all groups' }));

        expect(screen.getByText(/all 1 on this page are selected/i)).toBeInTheDocument();
        const selectAcrossButton = screen.getByRole('button', { name: /select all 30 across all pages/i });
        expect(selectAcrossButton).toBeInTheDocument();

        await user.click(selectAcrossButton);

        expect(screen.getByText(/all 30 matching groups across all pages are selected/i)).toBeInTheDocument();
        expect(screen.getByText('30 selected')).toBeInTheDocument();
    });

    it('bulk-accepting in select-all-across-pages mode calls the group bulk-accept endpoint with the filter and excluded keys', async () => {
        const user = userEvent.setup();
        setGroups([buildGroup(), buildGroup({ groupKey: 'all-queued', title: 'Stale Show' })], 30);
        render(<LibraryImportPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?rootFolderId=folder-2'),
        });

        await waitFor(() => expect(rowFor('/media/tv/Bluey')).toBeInTheDocument());
        await user.click(screen.getByRole('checkbox', { name: 'Select all groups' }));
        await user.click(screen.getByRole('button', { name: /select all 30 across all pages/i }));
        await user.click(within(rowFor('all-queued')).getByRole('checkbox', { name: 'Select Stale Show' }));
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));

        await waitFor(() => {
            expect(bulkAcceptGroupsMutateAsync).toHaveBeenCalledWith({
                rootFolderId: 'folder-2',
                excludeGroupKeys: ['all-queued'],
                monitoringMode: 'Inherit',
            });
        });
        expect(acceptGroupMutateAsync).not.toHaveBeenCalled();
    });
});
