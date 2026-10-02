import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect, type ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import type { LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

const useLibraryImportGroupCandidatesMock = vi.fn();
vi.mock('@/hooks/useLibraryImportGroupCandidates', () => ({
    useLibraryImportGroupCandidates: (params: unknown) => useLibraryImportGroupCandidatesMock(params),
}));

const bulkAcceptMutateAsync = vi.fn();
const leafAcceptAction = vi.fn();
let autoSelectMonitoring = true;
vi.mock('@/hooks/useBulkAcceptLibraryImportCandidates', () => ({
    useBulkAcceptLibraryImportCandidates: () => ({ mutateAsync: bulkAcceptMutateAsync, isPending: false }),
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

vi.mock('./CandidateLeafTable', () => ({
    CandidateLeafTable: ({
        items,
        requestAccept,
    }: {
        items: LibraryImportCandidate[];
        requestAccept: (action: (mode: 'Inherit') => void) => void;
    }) => (
        <div data-testid="candidate-leaf-table">
            {items.map((i) => i.filePath).join(',')}
            <button onClick={() => requestAccept(leafAcceptAction)}>Retry</button>
        </div>
    ),
}));

const tvLevels = [
    { slug: 'show', displayName: 'Show', parentSlug: null, standardFactFields: [] },
    { slug: 'season', displayName: 'Season', parentSlug: 'show', standardFactFields: [] },
    { slug: 'episode', displayName: 'Episode', parentSlug: 'season', standardFactFields: [] },
];

const movieLevels = [{ slug: 'movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] }];

const musicLevels = [
    { slug: 'artist', displayName: 'Artist', parentSlug: null, standardFactFields: [] },
    { slug: 'album', displayName: 'Album', parentSlug: 'artist', standardFactFields: [] },
    { slug: 'track', displayName: 'Track', parentSlug: 'album', standardFactFields: [] },
];

let mockContentTypes: unknown[] = [{ id: 'tv', name: 'TV', pluralName: 'TV Shows', slug: 'tv', levels: tvLevels }];
vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => ({ contentTypes: mockContentTypes, subjectTypes: [], isLoading: false, error: null }),
}));

import { commonParentPath } from './commonParentPath';
import { LibraryImportGroupPage } from './LibraryImportGroupPage';

function episode(
    id: string,
    season: string | undefined,
    overrides: Partial<LibraryImportCandidate> = {},
): LibraryImportCandidate {
    return {
        id,
        rootFolderId: 'folder-1',
        scanRunId: 'scan-1',
        filePath: `/media/tv/Bluey/${id}.mkv`,
        contentType: 'tv',
        status: 'Pending',
        parsedSignals: season === undefined ? {} : { season: [season] },
        matches: [],
        matchCount: 0,
        createdAt: '2024-01-01T00:00:00Z',
        ...overrides,
    };
}

function matchOf(): LibraryImportCandidate['matches'][number] {
    return { confidence: 0.95, providerType: 'tvmaze', externalIdKey: 'tvmaze', externalId: '44796', title: 'Bluey' };
}

function movie(id: string): LibraryImportCandidate {
    return {
        id,
        rootFolderId: 'folder-1',
        scanRunId: 'scan-1',
        filePath: `/media/movies/${id}.mkv`,
        contentType: 'movie',
        status: 'Pending',
        parsedSignals: {},
        matches: [],
        matchCount: 0,
        createdAt: '2024-01-01T00:00:00Z',
    };
}

function setCandidates(items: LibraryImportCandidate[]) {
    useLibraryImportGroupCandidatesMock.mockReturnValue({
        items,
        isLoading: false,
        isFetching: false,
        isError: false,
        error: null,
        refetch: vi.fn(),
    });
}

function createTestWrapper(initialEntry: string) {
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
                    <ToastStateProvider>
                        <ToastProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastProvider>
                    </ToastStateProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

function bucketRows(): HTMLElement[] {
    const [, body] = within(screen.getByTestId('library-import-group-buckets')).getAllByRole('rowgroup');
    return within(body).getAllByRole('row');
}

describe('commonParentPath', () => {
    it('stops at the first mismatched segment instead of joining later matches', () => {
        expect(commonParentPath(['/media/A/shared/a.mkv', '/media/B/shared/b.mkv'])).toBe('/media');
    });

    it('preserves a Windows root and separator', () => {
        expect(commonParentPath(['C:\\media\\A\\a.mkv', 'C:\\media\\B\\b.mkv'])).toBe('C:\\media');
        expect(commonParentPath(['C:\\a.mkv', 'D:\\b.mkv'])).toBe('');
    });
});

describe('LibraryImportGroupPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        autoSelectMonitoring = true;
        mockContentTypes = [{ id: 'tv', name: 'TV', pluralName: 'TV Shows', slug: 'tv', levels: tvLevels }];
        bulkAcceptMutateAsync.mockResolvedValue({ queued: [], skipped: [] });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('explains that group actions apply only to loaded files when the response is truncated', () => {
        // Arrange
        useLibraryImportGroupCandidatesMock.mockReturnValue({
            items: [],
            isLoading: false,
            isError: false,
            isTruncated: true,
        });

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=group'),
        });

        // Assert
        expect(screen.getByText('Not every file is listed')).toBeInTheDocument();
        expect(screen.getByText(/Counts and actions apply only to the loaded files/)).toBeInTheDocument();
        expect(screen.getByText('No files were loaded')).toBeInTheDocument();
        expect(screen.queryByText('This group has no files left')).not.toBeInTheDocument();
    });

    it('groups candidates by the next-level signal, with an explicit unassigned bucket', () => {
        setCandidates([episode('e1', '1'), episode('e2', '1'), episode('e3', '2'), episode('e4', undefined)]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const rows = bucketRows();
        expect(rows).toHaveLength(3);

        expect(rows[0]).toHaveTextContent('Season 1');
        expect(rows[0]).toHaveTextContent('Episode: 2');
        expect(rows[1]).toHaveTextContent('Season 2');
        expect(rows[1]).toHaveTextContent('Episode: 1');
        expect(rows[2]).toHaveTextContent('Unassigned Season');
        expect(rows[2]).toHaveTextContent('Episode: 1');
        expect(within(rows[0]).getByTitle('/media/tv/Bluey')).toBeInTheDocument();
    });

    it('buckets a music group by the album level signal', () => {
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates([
            episode('t1', undefined, { contentType: 'music', parsedSignals: { album: ['The Wall'] } }),
            episode('t2', undefined, { contentType: 'music', parsedSignals: { album: ['Animals'] } }),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=query%3Apink+floyd&title=Pink+Floyd'),
        });

        const rows = bucketRows();
        expect(rows).toHaveLength(2);
        expect(rows[0]).toHaveTextContent('Album Animals');
        expect(rows[1]).toHaveTextContent('Album The Wall');
    });

    it('renders the leaf table instead of one all-of-it unassigned bucket when no file carries the level signal', () => {
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates([
            episode('t1', undefined, { contentType: 'music' }),
            episode('t2', undefined, { contentType: 'music' }),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=query%3Apink+floyd&title=Pink+Floyd'),
        });

        expect(screen.queryByTestId('library-import-group-buckets')).toBeNull();
        expect(screen.queryByText('Unassigned Album')).toBeNull();
        expect(screen.getByTestId('candidate-leaf-table')).toHaveTextContent('/media/tv/Bluey/t1.mkv');
    });

    it('opens the monitoring dialog before running a leaf retry', async () => {
        const user = userEvent.setup();
        autoSelectMonitoring = false;
        setCandidates([movie('movie-1')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Fmovies&title=Movies'),
        });

        await user.click(screen.getByRole('button', { name: 'Retry' }));

        expect(screen.getByRole('dialog', { name: 'Accept import' })).toBeInTheDocument();
        expect(leafAcceptAction).not.toHaveBeenCalled();
    });

    it('tells the user where to go next when the group has emptied', () => {
        setCandidates([]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        expect(screen.getByTestId('library-import-group-empty')).toHaveTextContent(
            'Nothing is left to import here. Go back to the import queue for the other groups.',
        );
    });

    it('pluralizes a dig-in level name that already ends in s without doubling it', () => {
        mockContentTypes = [
            {
                id: 'tv',
                name: 'TV',
                pluralName: 'TV Shows',
                slug: 'tv',
                levels: [
                    { slug: 'show', displayName: 'Show', parentSlug: null, standardFactFields: [] },
                    { slug: 'season', displayName: 'Season', parentSlug: 'show', standardFactFields: [] },
                    { slug: 'episode', displayName: 'Series', parentSlug: 'season', standardFactFields: [] },
                ],
            },
        ];
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const firstRow = bucketRows()[0];
        expect(within(firstRow).getByRole('link', { name: /^Series$/ })).toBeInTheDocument();
        expect(within(firstRow).queryByRole('link', { name: /^Seriess$/ })).toBeNull();
    });

    it('digs into a bucket by extending the path search param one level deeper', async () => {
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const firstRow = bucketRows()[0];
        const href = within(firstRow)
            .getByRole('link', { name: /^Episode$/ })
            .getAttribute('href');

        expect(href).toContain('path=1');
        expect(href).toContain('key=%2Fmedia%2Ftv%2FBluey');
    });

    it('labels the dig-in button with the immediate child level of the bucket being dug into, pluralized', () => {
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const rows = bucketRows();
        expect(rows).toHaveLength(2);
        expect(within(rows[0]).getByRole('link', { name: /^Episode$/ })).toBeInTheDocument();
        expect(within(rows[0]).queryByRole('link', { name: /^Dig in$/ })).toBeNull();
        expect(within(rows[1]).getByRole('link', { name: /^Episode$/ })).toBeInTheDocument();
        expect(within(rows[1]).queryByRole('link', { name: /^Dig in$/ })).toBeNull();
    });

    it('accepting a bucket bulk-accepts exactly that subset of candidate ids', async () => {
        const user = userEvent.setup();
        setCandidates([
            episode('e1', '1', { matches: [matchOf()], matchCount: 1 }),
            episode('e2', '1', { matches: [matchOf()], matchCount: 1 }),
            episode('e3', '2', { matches: [matchOf()], matchCount: 1 }),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const firstRow = bucketRows()[0];
        await user.click(within(firstRow).getByRole('button', { name: /^Accept$/ }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({
                ids: ['e1', 'e2'],
                monitoringMode: 'Inherit',
            });
        });
    });

    it('requires monitoring before accepting and uses the chosen mode', async () => {
        const user = userEvent.setup();
        autoSelectMonitoring = false;
        setCandidates([episode('e1', '1', { matches: [matchOf()], matchCount: 1 })]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const row = bucketRows()[0];
        const accept = within(row).getByRole('button', { name: /^Accept$/ });
        await user.click(accept);

        expect(screen.getByRole('dialog', { name: 'Accept import' })).toBeInTheDocument();
        expect(bulkAcceptMutateAsync).not.toHaveBeenCalled();

        await user.click(screen.getByRole('button', { name: 'Choose Inherit and accept' }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['e1'], monitoringMode: 'Inherit' });
        });
    });

    it('keeps the bucket Accept button mounted while its accept dialog is open', async () => {
        const user = userEvent.setup();
        autoSelectMonitoring = false;
        setCandidates([episode('e1', '1', { matches: [matchOf()], matchCount: 1 })]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const accept = within(bucketRows()[0]).getByRole('button', { name: /^Accept$/ });
        await user.click(accept);

        expect(screen.getByRole('dialog', { name: 'Accept import' })).toBeInTheDocument();
        expect(accept).toBeInTheDocument();
    });

    it('disables Accept for a bucket whose candidates are all still matching pending', () => {
        setCandidates([
            episode('e1', '1', { status: 'MatchingPending' }),
            episode('e2', '1', { status: 'MatchingPending' }),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const firstRow = bucketRows()[0];
        expect(within(firstRow).getByRole('button', { name: /^Matching pending$/ })).toBeDisabled();
        expect(within(firstRow).queryByRole('button', { name: /^Accept$/ })).toBeNull();
    });

    it('enables Accept for a bucket with a matched, claimable candidate', () => {
        setCandidates([
            episode('e1', '1', { matches: [matchOf()], matchCount: 1 }),
            episode('e2', '1', { status: 'Queued' }),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const firstRow = bucketRows()[0];
        expect(within(firstRow).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();
    });

    it('enables Accept for a bucket with only a matched failed candidate', () => {
        setCandidates([episode('e1', '1', { matches: [matchOf()], matchCount: 1, status: 'Failed' })]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const row = bucketRows()[0];
        expect(within(row).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();
    });

    it('disables Accept for a bucket where the match and the pending status sit on different files', () => {
        setCandidates([
            episode('e1', '1', { matches: [matchOf()], matchCount: 1, status: 'Queued' }),
            episode('e2', '1'),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const firstRow = bucketRows()[0];
        expect(within(firstRow).getByRole('button', { name: /^Accept$/ })).toBeDisabled();
    });

    it('accepting one bucket leaves another bucket Accept button enabled (concurrent accepts are independent)', async () => {
        const user = userEvent.setup();
        setCandidates([
            episode('e1', '1', { matches: [matchOf()], matchCount: 1 }),
            episode('e2', '2', { matches: [matchOf()], matchCount: 1 }),
        ]);
        let resolveAccept: (value: { queued: unknown[]; skipped: unknown[] }) => void = () => {};
        bulkAcceptMutateAsync.mockImplementationOnce(
            () =>
                new Promise((resolve) => {
                    resolveAccept = resolve;
                }),
        );
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const rows = bucketRows();
        await user.click(within(rows[0]).getByRole('button', { name: /^Accept$/ }));

        expect(within(rows[0]).getByRole('button', { name: /^Accept$/ })).toBeDisabled();
        expect(within(rows[1]).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();

        resolveAccept({ queued: [], skipped: [] });
        await waitFor(() => {
            expect(within(rows[0]).getByRole('button', { name: /^Accept$/ })).not.toBeDisabled();
        });
    });

    it('shows a checkbox per bucket and the desktop bulk toolbar once one is checked', async () => {
        const user = userEvent.setup();
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
        const rows = bucketRows();
        await user.click(within(rows[0]).getByRole('checkbox', { name: 'Select Season 1' }));

        expect(screen.getByText('1 selected')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Accept selected' })).toBeInTheDocument();
    });

    it('checks every bucket on the page from the header checkbox', async () => {
        const user = userEvent.setup();
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        await user.click(screen.getByRole('checkbox', { name: 'Select all groups' }));

        expect(screen.getByText('2 selected')).toBeInTheDocument();
        expect(within(bucketRows()[0]).getByRole('checkbox', { name: 'Select Season 1' })).toBeChecked();
        expect(within(bucketRows()[1]).getByRole('checkbox', { name: 'Select Season 2' })).toBeChecked();
    });

    it('shows the bucket selection overlay only while mobile selection mode is active', async () => {
        const user = userEvent.setup();
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        expect(document.querySelectorAll('[data-action="toggle-row-mobile"]').length).toBe(0);

        await user.click(screen.getByRole('button', { name: 'Select' }));
        expect(document.querySelectorAll('[data-action="toggle-row-mobile"]').length).toBe(2);

        await user.click(screen.getByRole('button', { name: 'Select', pressed: true }));
        expect(document.querySelectorAll('[data-action="toggle-row-mobile"]').length).toBe(0);
    });

    it('names each bucket on its selection overlay circle', async () => {
        const user = userEvent.setup();
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        await user.click(screen.getByRole('button', { name: 'Select' }));
        expect(screen.getByRole('button', { name: 'Select Season 2' })).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Select Season 1' }));
        expect(screen.getByRole('button', { name: 'Deselect Season 1' })).toBeInTheDocument();
    });

    it('bulk-accepts the union of eligible selected buckets and skips an ineligible one with the aggregate toast', async () => {
        const user = userEvent.setup();
        setCandidates([
            episode('e1', '1', { matches: [matchOf()], matchCount: 1 }),
            episode('e2', '1', { matches: [matchOf()], matchCount: 1 }),
            episode('e3', '2'),
        ]);
        bulkAcceptMutateAsync.mockResolvedValueOnce({
            queued: [
                { candidateId: 'e1', jobExecutionId: 'job-1' },
                { candidateId: 'e2', jobExecutionId: 'job-2' },
            ],
            skipped: [],
        });
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const rows = bucketRows();
        await user.click(within(rows[0]).getByRole('checkbox', { name: 'Select Season 1' }));
        await user.click(within(rows[1]).getByRole('checkbox', { name: 'Select Season 2' }));
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));

        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({
                ids: ['e1', 'e2'],
                monitoringMode: 'Inherit',
            });
        });
        expect(bulkAcceptMutateAsync).toHaveBeenCalledTimes(1);
        await waitFor(() => {
            expect(screen.getByText('Import partially queued')).toBeInTheDocument();
        });
        expect(screen.getByText('2 files queued for import. 1 group is not ready to import.')).toBeInTheDocument();
    });

    it('reports nothing queued, with no mutation call, when every selected bucket is ineligible', async () => {
        const user = userEvent.setup();
        setCandidates([episode('e1', '1'), episode('e2', '2')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const rows = bucketRows();
        await user.click(within(rows[0]).getByRole('checkbox', { name: 'Select Season 1' }));
        await user.click(screen.getByRole('button', { name: 'Accept selected' }));

        await waitFor(() => {
            expect(screen.getByText('Nothing queued for import')).toBeInTheDocument();
        });
        expect(screen.getByText('1 group is not ready to import.')).toBeInTheDocument();
        expect(bulkAcceptMutateAsync).not.toHaveBeenCalled();
    });

    it('drops the bucket selection when navigating back up a level', async () => {
        const user = userEvent.setup();
        mockContentTypes = [
            {
                id: 'tv',
                name: 'TV',
                pluralName: 'TV Shows',
                slug: 'tv',
                levels: [
                    { slug: 'show', displayName: 'Show', parentSlug: null, standardFactFields: [] },
                    { slug: 'season', displayName: 'Season', parentSlug: 'show', standardFactFields: [] },
                    { slug: 'disc', displayName: 'Disc', parentSlug: 'season', standardFactFields: [] },
                    { slug: 'episode', displayName: 'Episode', parentSlug: 'disc', standardFactFields: [] },
                ],
            },
        ];
        setCandidates([
            episode('e1', undefined, { parsedSignals: { season: ['1'], disc: ['1'] } }),
            episode('e2', undefined, { parsedSignals: { season: ['1'], disc: ['2'] } }),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1'),
        });

        const discRows = bucketRows();
        expect(discRows).toHaveLength(2);
        await user.click(within(discRows[0]).getByRole('checkbox', { name: 'Select Disc 1' }));
        expect(screen.getByText('1 selected')).toBeInTheDocument();

        await user.click(screen.getAllByRole('link', { name: 'Bluey' })[0]);

        await waitFor(() => {
            expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
        });
    });

    it('renders the leaf-level per-file table once a bucket has no further child level', () => {
        setCandidates([episode('e1', '1')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1'),
        });

        expect(screen.getByTestId('candidate-leaf-table')).toHaveTextContent('/media/tv/Bluey/e1.mkv');
        expect(screen.queryByTestId('library-import-group-buckets')).toBeNull();
    });

    it('renders a single-leaf (movie-shaped) group straight to the leaf table with no grouped rows', () => {
        mockContentTypes = [{ id: 'movie', name: 'Movies', pluralName: 'Movies', slug: 'movie', levels: movieLevels }];
        setCandidates([movie('m1')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=query%3Athe+matrix&title=The+Matrix'),
        });

        expect(screen.getByTestId('candidate-leaf-table')).toHaveTextContent('/media/movies/m1.mkv');
        expect(screen.queryByTestId('library-import-group-buckets')).toBeNull();
    });

    it('paginates the bucket list at the same page size as the rest of the app', () => {
        setCandidates(Array.from({ length: 26 }, (_, index) => episode(`e${index}`, String(index + 1))));
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        const rows = bucketRows();
        expect(rows).toHaveLength(25);
        expect(screen.getByRole('navigation', { name: /pagination/i })).toBeInTheDocument();
    });

    it('names the drilled group in the document title', () => {
        setCandidates([episode('e1', '1')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        expect(document.title).toBe('Bluey - Magpie');
    });

    it('builds a breadcrumb trail from the library import root through the drilled path', () => {
        setCandidates([episode('e1', '1')]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(
                '/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&rootFolderId=folder-1&path=1',
            ),
        });

        const breadcrumb = screen.getByRole('navigation', { name: /breadcrumb/i });
        expect(breadcrumb).toHaveTextContent('Library import');
        expect(breadcrumb).toHaveTextContent('Bluey');
        expect(breadcrumb).toHaveTextContent('Season 1');
    });
});
