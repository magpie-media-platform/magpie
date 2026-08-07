import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import type { LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';
import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';

const mockToast = vi.hoisted(() => vi.fn());

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    isApiError: (value: unknown): boolean => typeof value === 'object' && value !== null && 'status' in value,
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params ?? {})) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
}));

vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: mockToast }),
}));

const mockContentTypes: LibraryTypeDefinition[] = [
    { id: 'movie', name: 'Movies', pluralName: 'Movies', slug: 'movie', levels: [] },
    { id: 'tv', name: 'TV Shows', pluralName: 'TV Shows', slug: 'tv', levels: [] },
];

vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => ({
        contentTypes: mockContentTypes,
        subjectTypes: [],
        isLoading: false,
        error: null,
        refetch: vi.fn(),
    }),
}));

vi.mock('@phosphor-icons/react', () => {
    const Icon = () => <svg aria-hidden="true" />;

    return {
        CaretDoubleLeftIcon: Icon,
        CaretDoubleRightIcon: Icon,
        CaretLeftIcon: Icon,
        CaretRightIcon: Icon,
        EyeIcon: Icon,
        LinkSimpleIcon: Icon,
        MagnifyingGlassIcon: Icon,
        SparkleIcon: Icon,
        WarningCircleIcon: Icon,
        XIcon: Icon,
    };
});

vi.mock('@/components/ui/Dialog', () => ({
    Dialog: ({ open, children }: { open?: boolean; children: ReactNode }) => (open ? <div>{children}</div> : null),
    DialogBody: ({ children, ...props }: { children: ReactNode }) => <div {...props}>{children}</div>,
    DialogContent: ({ children, ...props }: { children: ReactNode }) => <div {...props}>{children}</div>,
    DialogDescription: ({ children, ...props }: { children: ReactNode }) => <p {...props}>{children}</p>,
    DialogHeader: ({ children, ...props }: { children: ReactNode }) => <div {...props}>{children}</div>,
    DialogTitle: ({ children, ...props }: { children: ReactNode }) => <h2 {...props}>{children}</h2>,
}));

vi.mock('@/components/content/PreviewDrawer', () => ({
    PreviewDrawer: ({ open }: { open: boolean }) => (open ? <div data-testid="preview-drawer-open" /> : null),
}));

import { apiGet, apiPost, buildUrl } from '@/lib/api';
import { ManualMatchDialog } from './ManualMatchDialog';

const baseCandidate = {
    providerType: 'provider-a',
    externalIdSource: 'provider-a.movie',
    externalId: '550',
    contentType: 'movie',
    level: null,
    title: 'Fight Club',
    year: 1999,
    thumbnailUrl: 'http://images/poster.jpg',
};

function buildCandidate(overrides: Partial<typeof baseCandidate> = {}) {
    return { ...baseCandidate, ...overrides };
}

const candidate = buildCandidate();

function buildUnmatched(overrides: Partial<LibraryImportCandidate> = {}): LibraryImportCandidate {
    return {
        id: 'empty-1',
        rootFolderId: 'folder-1',
        scanRunId: 'scan-1',
        filePath: '/media/movies/Fight.Club.1999.mkv',
        contentType: 'movie',
        status: 'Pending',
        parsedSignals: { title: ['Fight Club'], year: ['1999'] },
        matches: [],
        matchCount: 0,
        createdAt: '2024-01-01T00:00:00Z',
        ...overrides,
    };
}

function titleInput(): HTMLInputElement {
    return document.getElementById('manual-match-title') as HTMLInputElement;
}

function yearInput(): HTMLInputElement {
    return document.getElementById('manual-match-year') as HTMLInputElement;
}

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

describe('ManualMatchDialog', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockBuildUrl = vi.mocked(buildUrl);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue({
            matches: [{ candidate, inLibrary: false }],
            pageItemCount: 1,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: false },
        });
        mockBuildUrl.mockImplementation((path: string, params?: object) => {
            const searchParams = new URLSearchParams();
            for (const [key, value] of Object.entries(params ?? {})) {
                if (value !== undefined && value !== null) {
                    searchParams.set(key, String(value));
                }
            }
            const queryString = searchParams.toString();
            return queryString ? `${path}?${queryString}` : path;
        });
        mockApiPost.mockImplementation((url: string) => {
            if (url.endsWith('/match')) {
                return Promise.resolve({ id: 'empty-1' });
            }
            return Promise.reject(new Error(`unexpected ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('uses user-focused match copy', () => {
        render(<ManualMatchDialog match={buildUnmatched()} onClose={vi.fn()} onMatched={vi.fn()} />, {
            wrapper: createWrapper(),
        });

        expect(screen.getByText(/Search for a match for "Fight\.Club\.1999\.mkv"/)).toBeInTheDocument();
        expect(screen.queryByText(/Search providers/i)).not.toBeInTheDocument();
    });

    it('keeps the search action after the match fields', () => {
        render(<ManualMatchDialog match={buildUnmatched()} onClose={vi.fn()} onMatched={vi.fn()} />, {
            wrapper: createWrapper(),
        });

        const searchRow = document.querySelector('.manual-match-dialog__search-row');
        const fields = document.querySelector('.manual-match-dialog__fields');
        const searchButton = screen.getByRole('button', { name: /^Search$/ });

        expect(searchRow).not.toBeNull();
        expect(searchRow?.children[0]).toBe(fields);
        expect(searchRow?.children[1]).toBe(searchButton);
    });

    it('pre-fills the Title and Release year inputs with the parsed values', () => {
        render(
            <ManualMatchDialog
                match={buildUnmatched({
                    parsedSignals: { title: ['The Matrix'], year: ['1999'] },
                    contentType: 'movie',
                })}
                onClose={vi.fn()}
                onMatched={vi.fn()}
            />,
            { wrapper: createWrapper() },
        );

        expect(titleInput().value).toBe('The Matrix');
        expect(yearInput().value).toBe('1999');
    });

    it('pre-fills a title containing "and" so the visible inputs still carry the parsed values', () => {
        render(
            <ManualMatchDialog
                match={buildUnmatched({ parsedSignals: { title: ['Beauty and the Beast'], year: ['1991'] } })}
                onClose={vi.fn()}
                onMatched={vi.fn()}
            />,
            { wrapper: createWrapper() },
        );

        expect(titleInput().value).toBe('Beauty and the Beast');
        expect(yearInput().value).toBe('1991');
    });

    it('strips a stale year embedded in the parsed title before pre-filling', () => {
        render(
            <ManualMatchDialog
                match={buildUnmatched({
                    parsedSignals: { title: ['The Matrix 1999'], year: ['1999'] },
                    contentType: 'movie',
                })}
                onClose={vi.fn()}
                onMatched={vi.fn()}
            />,
            { wrapper: createWrapper() },
        );

        expect(titleInput().value).toBe('The Matrix');
        expect(yearInput().value).toBe('1999');
    });

    it('scopes the seeded search to the parsed title, year, and content type', async () => {
        const user = userEvent.setup();
        render(
            <ManualMatchDialog
                match={buildUnmatched({
                    parsedSignals: { title: ['The Matrix'], year: ['1999'] },
                    contentType: 'movie',
                })}
                onClose={vi.fn()}
                onMatched={vi.fn()}
            />,
            { wrapper: createWrapper() },
        );

        await user.click(screen.getByRole('button', { name: /^Search$/ }));

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith(expect.stringMatching(/^\/content-search\?query=/));
        });
        const seededQuery = (
            mockBuildUrl.mock.calls.find(([path]) => path === '/content-search')?.[1] as { query: string }
        ).query;
        expect(seededQuery).toContain('contentType = "movie"');
        expect(seededQuery).toContain('title contains "The Matrix"');
        expect(seededQuery).toContain('publishDate >= "1999-01-01"');
        expect(seededQuery).toContain('publishDate <= "1999-12-31"');
        expect(seededQuery).not.toBe('contentType = "movie"');
    });

    it('searches with the live field edits, not the stale seed', async () => {
        const user = userEvent.setup();
        render(<ManualMatchDialog match={buildUnmatched()} onClose={vi.fn()} onMatched={vi.fn()} />, {
            wrapper: createWrapper(),
        });

        await user.clear(titleInput());
        await user.type(titleInput(), 'Snatch');
        await user.click(screen.getByRole('button', { name: /^Search$/ }));

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith(expect.stringMatching(/^\/content-search\?query=/));
        });
        const seededQuery = (
            mockBuildUrl.mock.calls.find(([path]) => path === '/content-search')?.[1] as { query: string }
        ).query;
        expect(seededQuery).toContain('title contains "Snatch"');
        expect(seededQuery).not.toContain('Fight Club');
    });

    it('searches for matches and lists provider metadata candidates', async () => {
        const user = userEvent.setup();
        render(<ManualMatchDialog match={buildUnmatched()} onClose={vi.fn()} onMatched={vi.fn()} />, {
            wrapper: createWrapper(),
        });

        await user.click(screen.getByRole('button', { name: /^Search$/ }));

        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog__results')).toBeInTheDocument();
        });
        const results = screen.getByTestId('manual-match-dialog__results');
        expect(results).toHaveTextContent('Fight Club');
        expect(results).toHaveTextContent('provider-a');
        expect(mockBuildUrl).toHaveBeenCalledWith(
            '/content-search',
            expect.objectContaining({ query: expect.stringContaining('title contains "Fight Club"'), page: 1 }),
        );
        expect(mockApiGet).toHaveBeenCalledWith(expect.stringMatching(/^\/content-search\?query=.+&page=1$/));
    });

    it('paginates provider metadata candidates and resets to the first page for a new search', async () => {
        const firstPageCandidates = Array.from({ length: 3 }, (_, index) =>
            buildCandidate({
                externalId: String(1000 + index),
                title: `Candidate ${index + 1}`,
            }),
        );
        mockApiGet.mockImplementation((url: string) => {
            const page = new URLSearchParams(url.split('?')[1]).get('page');
            if (page === '2') {
                return Promise.resolve({
                    matches: [
                        {
                            candidate: buildCandidate({ externalId: '2001', title: 'Page 2 Candidate' }),
                            inLibrary: false,
                        },
                    ],
                    pageItemCount: 1,
                    existsInLibraryCount: 0,
                    pageInfo: { requestedPage: 2, hasMore: false },
                });
            }
            return Promise.resolve({
                matches: firstPageCandidates.map((candidate) => ({ candidate, inLibrary: false })),
                pageItemCount: 3,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 1, hasMore: true },
            });
        });
        const user = userEvent.setup();
        render(<ManualMatchDialog match={buildUnmatched()} onClose={vi.fn()} onMatched={vi.fn()} />, {
            wrapper: createWrapper(),
        });

        await user.click(screen.getByRole('button', { name: /^Search$/ }));

        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog__pagination')).toHaveTextContent('Page 1');
        });
        const nextButton = screen.getByLabelText('Go to next page') as HTMLButtonElement;
        expect(nextButton.disabled).toBe(false);
        expect(screen.getByText('Candidate 1')).toBeInTheDocument();
        expect(screen.getByText('Candidate 3')).toBeInTheDocument();
        expect(screen.queryByText('Candidate 4')).not.toBeInTheDocument();

        await user.click(nextButton);

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenLastCalledWith(expect.stringMatching(/^\/content-search\?query=.+&page=2$/));
        });
        expect(screen.getByTestId('manual-match-dialog__pagination')).toHaveTextContent('Page 2');
        expect(screen.getByText('Page 2 Candidate')).toBeInTheDocument();
        expect((screen.getByLabelText('Go to next page') as HTMLButtonElement).disabled).toBe(true);

        await user.click(screen.getByRole('button', { name: /^Search$/ }));

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenLastCalledWith(expect.stringMatching(/^\/content-search\?query=.+&page=1$/));
        });
        expect(screen.getByTestId('manual-match-dialog__pagination')).toHaveTextContent('Page 1');
    });

    it('surfaces parse details returned by candidate search', async () => {
        mockApiGet.mockRejectedValue({
            name: 'ApiError',
            status: 400,
            message: 'Bad request',
            details: { message: 'Unexpected token', position: 3, expected: [] },
        });
        const user = userEvent.setup();
        render(<ManualMatchDialog match={buildUnmatched()} onClose={vi.fn()} onMatched={vi.fn()} />, {
            wrapper: createWrapper(),
        });

        await user.click(screen.getByRole('button', { name: /^Search$/ }));

        expect(await screen.findByTestId('manual-match-dialog__error')).toHaveTextContent('Unexpected token');
    });

    it('uses the generic message for other candidate search failures', async () => {
        mockApiGet.mockRejectedValue(new Error('boom'));
        const user = userEvent.setup();
        render(<ManualMatchDialog match={buildUnmatched()} onClose={vi.fn()} onMatched={vi.fn()} />, {
            wrapper: createWrapper(),
        });

        await user.click(screen.getByRole('button', { name: /^Search$/ }));

        expect(await screen.findByTestId('manual-match-dialog__error')).toHaveTextContent(
            'Search failed. Please try again.',
        );
    });

    it('renders long filenames and candidate titles inside wrapping containers', async () => {
        const user = userEvent.setup();
        const longFileName = `${'VeryLongUnbrokenImportedFileName'.repeat(5)}.mkv`;
        const longTitle = 'VeryLongUnbrokenCandidateTitle'.repeat(5);
        mockApiGet.mockResolvedValue({
            matches: [{ candidate: { ...candidate, title: longTitle }, inLibrary: false }],
            pageItemCount: 1,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: false },
        });

        render(
            <ManualMatchDialog
                match={buildUnmatched({ filePath: `/media/movies/${longFileName}` })}
                onClose={vi.fn()}
                onMatched={vi.fn()}
            />,
            {
                wrapper: createWrapper(),
            },
        );

        const description = screen.getByText((content, element) => {
            return Boolean(
                element?.classList.contains('manual-match-dialog__description') && content.includes(longFileName),
            );
        });
        expect(description).toHaveClass('manual-match-dialog__description');

        await user.click(screen.getByRole('button', { name: /^Search$/ }));
        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog__results')).toBeInTheDocument();
        });

        const title = document.querySelector('.manual-match-dialog__candidate-title');
        expect(title).toHaveTextContent(longTitle);
        expect(title).toHaveClass('manual-match-dialog__candidate-title');
    });

    it('associates a chosen candidate without importing, then closes and refetches', async () => {
        const user = userEvent.setup();
        const onClose = vi.fn();
        const onMatched = vi.fn();
        render(<ManualMatchDialog match={buildUnmatched()} onClose={onClose} onMatched={onMatched} />, {
            wrapper: createWrapper(),
        });

        await user.click(screen.getByRole('button', { name: /^Search$/ }));
        await waitFor(() => {
            expect(screen.getByTestId('manual-match-dialog__results')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^Match$/ }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith(
                '/import/candidates/empty-1/match',
                expect.objectContaining({
                    providerType: 'provider-a',
                    externalIdKey: 'provider-a.movie',
                    externalId: '550',
                    title: 'Fight Club',
                    subtitle: '1999',
                    imageUrl: 'http://images/poster.jpg',
                }),
            );
        });
        expect(mockApiPost).not.toHaveBeenCalledWith(expect.stringContaining('/accept'), expect.anything());
        expect(onMatched).toHaveBeenCalled();
        expect(onClose).toHaveBeenCalled();
    });
});
