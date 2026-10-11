import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect, type ReactNode, type RefObject } from 'react';

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
    AcceptImportDialog: ({
        action,
        onClose,
    }: {
        action: (((mode: 'Inherit') => void) & { fallbackFocusRef?: RefObject<HTMLElement | null> }) | null;
        onClose: () => void;
    }) => {
        useEffect(() => {
            if (action && autoSelectMonitoring) action('Inherit');
        }, [action]);
        return action && !autoSelectMonitoring ? (
            <div role="dialog" aria-label="Accept import" data-fallback-focus={action.fallbackFocusRef?.current?.id}>
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
        scopeCounts,
        scopeAction,
    }: {
        items: LibraryImportCandidate[];
        requestAccept: (action: (mode: 'Inherit') => void) => void;
        scopeCounts?: ReactNode;
        scopeAction?: ReactNode;
    }) => (
        <div data-testid="candidate-leaf-table">
            {items.map((i) => i.filePath).join(',')}
            <button onClick={() => requestAccept(leafAcceptAction)}>Retry</button>
            {scopeCounts}
            {scopeAction}
        </div>
    ),
}));

const tvLevels = [
    { slug: 'show', displayName: 'Show', parentSlug: null, standardFactFields: [] },
    { slug: 'season', displayName: 'Season', parentSlug: 'show', standardFactFields: [] },
    { slug: 'episode', displayName: 'Episode', parentSlug: 'season', standardFactFields: [] },
];

const movieLevels = [{ slug: 'movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] }];

const bookLevels = [
    { slug: 'ebook', displayName: 'E-book', parentSlug: null, standardFactFields: [] },
    { slug: 'audiobook', displayName: 'Audiobook', parentSlug: null, standardFactFields: [] },
];

const musicLevels = [
    { slug: 'artist', displayName: 'Artist', parentSlug: null, standardFactFields: [] },
    { slug: 'album', displayName: 'Album', parentSlug: 'artist', standardFactFields: [] },
    { slug: 'track', displayName: 'Track', parentSlug: 'album', standardFactFields: [] },
];

const discLevels = [
    { slug: 'show', displayName: 'Show', parentSlug: null, standardFactFields: [] },
    { slug: 'season', displayName: 'Season', parentSlug: 'show', standardFactFields: [] },
    { slug: 'disc', displayName: 'Disc', parentSlug: 'season', standardFactFields: [] },
    { slug: 'episode', displayName: 'Episode', parentSlug: 'disc', standardFactFields: [] },
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

const blueyMatchedToSeason = [
    episode('e1', undefined, {
        parsedSignals: { season: ['1'], disc: ['1'] },
        matches: [{ ...matchOf(), levelSlug: 'season' }],
        matchCount: 1,
    }),
    episode('e2', undefined, {
        parsedSignals: { season: ['1'], disc: ['2'] },
        matches: [{ ...matchOf(), levelSlug: 'season' }],
        matchCount: 1,
    }),
];

const adeleWithOneMatchedFile = [
    episode('t1', undefined, {
        contentType: 'music',
        filePath: '/music/Adele/21/01 Rolling in the Deep.flac',
        parsedSignals: { artist: ['Adele'], album: ['21'], title: ['21'], year: ['2011'] },
        matches: [
            {
                confidence: 1,
                providerType: 'musicbrainz',
                externalIdKey: 'musicbrainz',
                externalId: 'adele-21',
                title: '21',
                subtitle: '2011',
                levelSlug: 'album',
            },
        ],
        matchCount: 1,
    }),
    episode('t2', undefined, {
        contentType: 'music',
        filePath: '/music/Adele/21/02 Rumour Has It.flac',
        parsedSignals: { artist: ['Adele'], album: ['21'], title: ['21'], year: ['2011'] },
    }),
    episode('t3', undefined, {
        contentType: 'music',
        filePath: '/music/Adele/25/01 Hello.flac',
        parsedSignals: { artist: ['Adele'], album: ['25'], title: ['25'], year: ['2015'] },
    }),
];

const beatlesAlbumMatch = {
    confidence: 1,
    providerType: 'musicbrainz',
    externalIdKey: 'musicbrainz',
    externalId: 'beatles-1',
    title: '1',
    subtitle: '2000',
    levelSlug: 'album',
};

const beatlesMatchedToAlbum = [
    episode('t1', undefined, {
        contentType: 'music',
        filePath: '/music/The Beatles/1/01 Love Me Do.flac',
        parsedSignals: { artist: ['The Beatles'], album: ['1'], title: ['1'], year: ['2000'] },
        matches: [beatlesAlbumMatch],
        matchCount: 1,
    }),
    episode('t2', undefined, {
        contentType: 'music',
        filePath: '/music/The Beatles/1/02 From Me to You.flac',
        parsedSignals: { artist: ['The Beatles'], album: ['1'], title: ['1'], year: ['2000'] },
        matches: [beatlesAlbumMatch],
        matchCount: 1,
    }),
];

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
        vi.unstubAllGlobals();
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
        expect(rows[2]).toHaveTextContent('Season Unassigned');
        expect(rows[2]).toHaveTextContent('Episode: 1');
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
        expect(screen.queryByText('Album Unassigned')).toBeNull();
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

    it('GroupLink_OpensTheGroupOneLevelDeeper_WhenRendered', () => {
        // Arrange
        setCandidates([episode('e1', '1'), episode('e2', '2')]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Assert
        const firstRow = bucketRows()[0];
        const href = within(firstRow).getByRole('link', { name: 'Season 1' }).getAttribute('href');
        expect(href).toContain('path=1');
        expect(href).toContain('key=%2Fmedia%2Ftv%2FBluey');
        expect(within(firstRow).queryByRole('button', { name: /^Episode$/ })).toBeNull();
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
        mockContentTypes = [{ id: 'tv', name: 'TV', pluralName: 'TV Shows', slug: 'tv', levels: discLevels }];
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

        await user.click(screen.getAllByRole('link', { name: 'Show Bluey' })[0]);

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

    it('LeafTable_ListsTheFilesInPathOrder_WhenTheServerReturnsThemInIdOrder', () => {
        // Arrange
        mockContentTypes = [{ id: 'movie', name: 'Movies', pluralName: 'Movies', slug: 'movie', levels: movieLevels }];
        setCandidates([movie('m10'), movie('m2'), movie('m1')]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Fmovies&title=Movies'),
        });

        // Assert
        expect(screen.getByTestId('candidate-leaf-table')).toHaveTextContent(
            '/media/movies/m1.mkv,/media/movies/m2.mkv,/media/movies/m10.mkv',
        );
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

    it('GroupContext_ShowsTheCurrentLevelAndItsSharedFolder_WhenABucketIsOpen', () => {
        // Arrange
        setCandidates([
            episode('e1', '1', { filePath: '/media/tv/Bluey/Season 1/e1.mkv' }),
            episode('e2', '1', { filePath: '/media/tv/Bluey/Season 1/e2.mkv' }),
            episode('e3', '2', { filePath: '/media/tv/Bluey/Season 2/e3.mkv' }),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1'),
        });

        // Assert
        expect(screen.getByRole('heading', { level: 2, name: 'Season 1' })).toBeInTheDocument();
        expect(screen.getByText('/media/tv/Bluey/Season 1')).toBeInTheDocument();
    });

    it.each([
        ['/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1', 'Season 1'],
        ['/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=', 'Season Unassigned'],
    ])('GroupContext_PutsTheLevelBeforeTheName_WhenTheBucketValueIsNotAName (%s)', (entry, expected) => {
        // Arrange
        setCandidates([episode('e1', '1'), episode('e2', undefined)]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(entry),
        });

        // Assert
        expect(screen.getByRole('heading', { level: 2, name: expected })).toBeInTheDocument();
        expect(document.querySelector('.library-import-group-page__level')).toBeNull();
    });

    it('GroupContext_ShowsTheYearOfTheMatchAndTheLevel_WhenTheGroupIsMatched', () => {
        // Arrange
        setCandidates([
            episode('e1', '1', {
                matches: [{ ...matchOf(), subtitle: '2018', levelSlug: 'show' }],
                matchCount: 1,
            }),
            episode('e2', '2'),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Assert
        expect(screen.getByText('2018 · Show', { selector: '.library-import-group-page__level' })).toBeInTheDocument();
    });

    it.each([
        ['audiobook', 'book', bookLevels, '2001 · Audiobook'],
        ['album', 'music', musicLevels, '2001 · Album'],
    ])(
        'GroupContext_ShowsTheLevelOfTheMatch_WhenTheMatchIsNotOnTheFirstRootLevel (%s)',
        (levelSlug, slug, levels, expected) => {
            // Arrange
            mockContentTypes = [{ id: slug, name: slug, pluralName: slug, slug, levels }];
            setCandidates([
                episode('b1', undefined, {
                    contentType: slug,
                    matches: [{ ...matchOf(), subtitle: '2001', levelSlug }],
                    matchCount: 1,
                }),
            ]);

            // Act
            render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
                wrapper: createTestWrapper('/library/import?key=%2Fmedia%2FDune&title=Dune'),
            });

            // Assert
            expect(screen.getByText(expected, { selector: '.library-import-group-page__level' })).toBeInTheDocument();
        },
    );

    it('GroupContext_ShowsNoLevel_WhenTheTypeHasTwoRootLevelsAndTheGroupHasNoMatch', () => {
        // Arrange
        mockContentTypes = [{ id: 'book', name: 'Book', pluralName: 'Books', slug: 'book', levels: bookLevels }];
        setCandidates([episode('b1', undefined, { contentType: 'book' })]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2FDune&title=Dune'),
        });

        // Assert
        expect(screen.getByRole('heading', { level: 2, name: 'Dune' })).toBeInTheDocument();
        expect(document.querySelector('.library-import-group-page__level')).toBeNull();
    });

    it('GroupContext_ShowsOnlyTheLevel_WhenTheNameHasTheYear', () => {
        // Arrange
        mockContentTypes = [{ id: 'movie', name: 'Movie', pluralName: 'Movies', slug: 'movie', levels: movieLevels }];
        setCandidates([
            {
                ...movie('m1'),
                matches: [{ ...matchOf(), subtitle: '1981', levelSlug: 'movie' }],
                matchCount: 1,
            },
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Fmovies%2FThief&title=Thief+%281981%29'),
        });

        // Assert
        expect(screen.getByText('Movie', { selector: '.library-import-group-page__level' })).toBeInTheDocument();
    });

    it.each([
        ['/library/import?key=%2Fmusic%2FTHE+BLUE+HEARTS&title=THE+BLUE+HEARTS', 'Artist'],
        ['/library/import?key=%2Fmusic%2FTHE+BLUE+HEARTS&title=THE+BLUE+HEARTS&path=THE+BLUE+HEARTS', '1987 · Album'],
    ])('GroupContext_ShowsTheParsedYearOnlyOnTheTitledLevel_WhenOpenedAt (%s)', (entry, expected) => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates([
            episode('t1', undefined, {
                contentType: 'music',
                parsedSignals: {
                    artist: ['THE BLUE HEARTS'],
                    album: ['THE BLUE HEARTS'],
                    title: ['THE BLUE HEARTS'],
                    year: ['1987'],
                    track: ['1'],
                },
            }),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(entry),
        });

        // Assert
        expect(screen.getByRole('heading', { level: 2, name: 'THE BLUE HEARTS' })).toBeInTheDocument();
        expect(screen.getByText(expected, { selector: '.library-import-group-page__level' })).toBeInTheDocument();
    });

    it.each([
        ['1989', '2014', '2014 · Album'],
        ['21', '2011', '2011 · Album'],
    ])('GroupContext_ShowsTheNameThenTheYearAndLevel_WhenTheAlbumNameIsAllDigits (%s)', (album, year, expected) => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates([
            episode('t1', undefined, {
                contentType: 'music',
                parsedSignals: { artist: ['Adele'], album: [album], title: [album], year: [year] },
            }),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(`/library/import?key=%2Fmusic%2FAdele&title=Adele&path=${album}`),
        });

        // Assert
        expect(screen.getByRole('heading', { level: 2, name: album })).toBeInTheDocument();
        expect(screen.getByText(expected, { selector: '.library-import-group-page__level' })).toBeInTheDocument();
    });

    it('Body_ListsTheFiles_WhenTheGroupMatchIsOnTheLevelAboveTheFiles', () => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates([
            episode('t1', undefined, {
                contentType: 'music',
                filePath: '/music/Various Artists/Big Compilation/01 Intro.flac',
                parsedSignals: { album: ['Big Compilation'], title: ['Big Compilation'] },
                matches: [
                    {
                        confidence: 1,
                        providerType: 'musicbrainz',
                        externalIdKey: 'musicbrainz',
                        externalId: 'big-compilation',
                        title: 'Big Compilation',
                        levelSlug: 'album',
                    },
                ],
                matchCount: 1,
            }),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmusic%2FVarious+Artists&title=Various+Artists'),
        });

        // Assert
        expect(screen.queryByTestId('library-import-group-buckets')).toBeNull();
        expect(screen.getByTestId('candidate-leaf-table')).toHaveTextContent(
            '/music/Various Artists/Big Compilation/01 Intro.flac',
        );
    });

    it('Buckets_ShowTheLevelBelowTheMatch_WhenTheGroupMatchIsBelowTheRootLevel', () => {
        // Arrange
        mockContentTypes = [{ id: 'tv', name: 'TV', pluralName: 'TV Shows', slug: 'tv', levels: discLevels }];
        setCandidates(blueyMatchedToSeason);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Assert
        const rows = bucketRows();
        expect(rows).toHaveLength(2);
        expect(rows[0]).toHaveTextContent('Disc 1');
        expect(rows[1]).toHaveTextContent('Disc 2');
        expect(within(rows[0]).getByRole('link', { name: 'Disc 1' })).toHaveAttribute(
            'href',
            '/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1&path=1',
        );
    });

    it('Breadcrumb_ShowsEachLevelOnce_WhenTheGroupMatchIsBelowTheRootLevel', () => {
        // Arrange
        mockContentTypes = [{ id: 'tv', name: 'TV', pluralName: 'TV Shows', slug: 'tv', levels: discLevels }];
        setCandidates(blueyMatchedToSeason);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1&path=1'),
        });

        // Assert
        const crumbs = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getAllByRole('listitem');
        expect(crumbs.map((crumb) => crumb.textContent)).toEqual(['Library import', 'Season Bluey', 'Disc 1']);
        expect(screen.getByRole('heading', { level: 2, name: 'Disc 1' })).toBeInTheDocument();
    });

    it.each([
        ['/library/import?key=%2Fmusic%2FAdele&title=Adele', ['Library import', 'Artist Adele']],
        ['/library/import?key=%2Fmusic%2FAdele&title=Adele&path=21', ['Library import', 'Artist Adele', 'Album 21']],
    ])('Breadcrumb_ShowsTheRootLevel_WhenOnlyOneFileHasAMatch (%s)', (entry, expected) => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates(adeleWithOneMatchedFile);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(entry),
        });

        // Assert
        const crumbs = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getAllByRole('listitem');
        expect(crumbs.map((crumb) => crumb.textContent)).toEqual(expected);
    });

    it('Buckets_FollowTheRootLevel_WhenOnlyOneFileHasAMatch', () => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates(adeleWithOneMatchedFile);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmusic%2FAdele&title=Adele'),
        });

        // Assert
        const rows = bucketRows();
        expect(rows).toHaveLength(2);
        expect(rows[0]).toHaveTextContent('Album 21');
        expect(rows[1]).toHaveTextContent('Album 25');
    });

    it('Body_ListsTheFilesOfTheAlbum_WhenOnlyOneFileHasAMatch', () => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates(adeleWithOneMatchedFile);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmusic%2FAdele&title=Adele&path=21'),
        });

        // Assert
        expect(screen.getByRole('heading', { level: 2, name: '21' })).toBeInTheDocument();
        expect(screen.getByText('2011 · Album', { selector: '.library-import-group-page__level' })).toBeInTheDocument();
        expect(screen.getByTestId('candidate-leaf-table')).toHaveTextContent(
            '/music/Adele/21/01 Rolling in the Deep.flac,/music/Adele/21/02 Rumour Has It.flac',
        );
    });

    it.each([
        '/library/import?key=%2Fmusic%2FThe+Beatles&title=The+Beatles',
        '/library/import?key=%2Fmusic%2FThe+Beatles&title=The+Beatles&path=1',
    ])('Body_ListsTheFilesOfTheGroup_WhenEveryFileHasTheAlbumMatchOfThePath (%s)', (entry) => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates(beatlesMatchedToAlbum);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(entry),
        });

        // Assert
        const crumbs = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getAllByRole('listitem');
        expect(crumbs.map((crumb) => crumb.textContent)).toEqual(['Library import', 'Album The Beatles']);
        expect(screen.getByRole('heading', { level: 2, name: 'The Beatles' })).toBeInTheDocument();
        expect(screen.getByText('2000 · Album', { selector: '.library-import-group-page__level' })).toBeInTheDocument();
        expect(screen.getByTestId('candidate-leaf-table')).toHaveTextContent(
            '/music/The Beatles/1/01 Love Me Do.flac,/music/The Beatles/1/02 From Me to You.flac',
        );
    });

    it('Buckets_FollowTheRootLevel_WhenTheFilesWithTheGroupMatchAreInMoreThanOneAlbum', () => {
        // Arrange
        mockContentTypes = [{ id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: musicLevels }];
        setCandidates([
            beatlesMatchedToAlbum[0],
            episode('t3', undefined, {
                contentType: 'music',
                filePath: '/music/The Beatles/Abbey Road/01 Come Together.flac',
                parsedSignals: { artist: ['The Beatles'], album: ['Abbey Road'], title: ['Abbey Road'] },
                matches: [beatlesAlbumMatch],
                matchCount: 1,
            }),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmusic%2FThe+Beatles&title=The+Beatles'),
        });

        // Assert
        const rows = bucketRows();
        expect(rows).toHaveLength(2);
        expect(rows[0]).toHaveTextContent('Album 1');
        expect(rows[1]).toHaveTextContent('Album Abbey Road');
    });

    it.each([
        ['/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey', '3 files · 1 failed'],
        ['/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1', '2 files · 1 failed'],
        ['/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=2', '1 file'],
    ])('Counts_CountTheFilesOfTheGroupAndItsDescendants_WhenOpenedAt (%s)', (entry, expected) => {
        // Arrange
        setCandidates([episode('e1', '1', { status: 'Failed' }), episode('e2', '1'), episode('e3', '2')]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper(entry),
        });

        // Assert
        expect(document.querySelector('.library-import-group-page__counts')?.textContent).toBe(expected);
    });

    it('BucketStatus_CountsTheFailedFilesOfEachBucket_WhenFilesFailed', () => {
        // Arrange
        setCandidates([
            episode('e1', '1', { status: 'Failed' }),
            episode('e2', '1', { status: 'Failed' }),
            episode('e3', '2'),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Assert
        const rows = bucketRows();
        expect(rows[0]).toHaveTextContent('2 Failed');
        expect(rows[1]).not.toHaveTextContent('Failed');
    });

    it('RetryFailed_QueuesOnlyTheFailedMatchedFilesOfTheGroup_WhenChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        setCandidates([
            episode('e1', '1', { status: 'Failed', matches: [matchOf()], matchCount: 1 }),
            episode('e2', '1', { matches: [matchOf()], matchCount: 1 }),
            episode('e3', '2', { status: 'Failed', matches: [matchOf()], matchCount: 1 }),
            episode('e4', '2', { status: 'Failed' }),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Act
        await user.click(screen.getByRole('button', { name: 'Retry failed (2)' }));

        // Assert
        await waitFor(() => {
            expect(bulkAcceptMutateAsync).toHaveBeenCalledWith({ ids: ['e1', 'e3'], monitoringMode: 'Inherit' });
        });
    });

    it('RetryFailed_GivesTheGroupHeadingTheFocusAfterAccept_WhenChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        autoSelectMonitoring = false;
        setCandidates([episode('e1', '1', { status: 'Failed', matches: [matchOf()], matchCount: 1 })]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Act
        await user.click(screen.getByRole('button', { name: 'Retry failed (1)' }));

        // Assert
        expect(screen.getByRole('dialog', { name: 'Accept import' })).toHaveAttribute(
            'data-fallback-focus',
            'library-import-group-title',
        );
        expect(screen.getByRole('heading', { name: 'Bluey' })).toHaveAttribute('tabindex', '-1');
    });

    it('RetryFailed_ReportsNothingQueued_WhenTheServerSkipsEveryFile', async () => {
        // Arrange
        const user = userEvent.setup();
        bulkAcceptMutateAsync.mockResolvedValueOnce({
            queued: [],
            skipped: [{ candidateId: 'e1', reason: 'The file is already queued.' }],
        });
        setCandidates([episode('e1', '1', { status: 'Failed', matches: [matchOf()], matchCount: 1 })]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Act
        await user.click(screen.getByRole('button', { name: 'Retry failed (1)' }));

        // Assert
        expect(await screen.findByText('Nothing queued for import')).toBeInTheDocument();
        expect(screen.getByText('0 files queued for import, 1 skipped.')).toBeInTheDocument();
        expect(screen.queryByText('Import queued')).not.toBeInTheDocument();
    });

    it('RetryFailed_ShowsItsLabelOnTheFab_WhenTheScreenIsAPhone', () => {
        // Arrange
        vi.stubGlobal('innerWidth', 390);
        setCandidates([episode('e1', '1', { status: 'Failed', matches: [matchOf()], matchCount: 1 })]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1'),
        });

        // Assert
        const fab = within(screen.getByTestId('candidate-leaf-table')).getByRole('button', {
            name: 'Retry failed (1)',
        });
        expect(fab).toHaveClass('quick-actions-button__fab');
        expect(fab).toHaveTextContent('Retry failed (1)');
    });

    it('RetryFailed_IsAbsent_WhenNoFileFailed', () => {
        // Arrange
        setCandidates([episode('e1', '1', { matches: [matchOf()], matchCount: 1 }), episode('e2', '2')]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Assert
        expect(screen.queryByRole('button', { name: /^Retry failed/ })).toBeNull();
    });

    it('RetryFailed_HidesWhileGroupsAreSelected_WhenABucketIsSelected', async () => {
        // Arrange
        const user = userEvent.setup();
        setCandidates([
            episode('e1', '1', { status: 'Failed', matches: [matchOf()], matchCount: 1 }),
            episode('e2', '2'),
        ]);
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey'),
        });

        // Act
        await user.click(within(bucketRows()[1]).getByRole('checkbox', { name: 'Select Season 2' }));

        // Assert
        expect(screen.queryByRole('button', { name: 'Retry failed (1)' })).toBeNull();
        expect(screen.getByText('1 selected')).toBeInTheDocument();
    });

    it('RetryFailed_ScopesToTheOpenBucket_WhenTheFilesAreListed', () => {
        // Arrange
        setCandidates([
            episode('e1', '1', { status: 'Failed', matches: [matchOf()], matchCount: 1 }),
            episode('e2', '2', { status: 'Failed', matches: [matchOf()], matchCount: 1 }),
        ]);

        // Act
        render(<LibraryImportGroupPage monitoringMode="" onMonitoringModeChange={() => {}} />, {
            wrapper: createTestWrapper('/library/import?key=%2Fmedia%2Ftv%2FBluey&title=Bluey&path=1'),
        });

        // Assert
        expect(
            within(screen.getByTestId('candidate-leaf-table')).getByRole('button', { name: 'Retry failed (1)' }),
        ).toBeInTheDocument();
    });
});
