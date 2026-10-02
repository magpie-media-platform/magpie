import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { createMemoryRouter, RouterProvider, MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';

import { SubjectsPage } from './SubjectsPage';
import type { ApiSubject, ApiMetadataContainer, SubjectQueryParams } from '@/hooks/useSubjects';

const mockNavigate = vi.fn();

function createMatchMedia(width: number) {
    return vi.fn((query: string) => {
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
    });
}

vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

const mockRefetch = vi.fn();
vi.mock('@/hooks/useSubjects', () => ({
    useSubjects: vi.fn(),
    useSubject: vi.fn(),
}));

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockUseLibraryTypes = vi.fn();
const mockTypeIconName = vi.fn();

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...original,
        useLibraryTypes: () => mockUseLibraryTypes(),
        useTypeIconName: () => mockTypeIconName,
    };
});

import { useSubjects } from '@/hooks/useSubjects';

function createMockMetadataContainer(title: string, imageUrl?: string): ApiMetadataContainer {
    return {
        id: `mc-${title.toLowerCase().replace(/\s+/g, '-')}`,
        createdAt: '2024-01-01T00:00:00Z',
        contributions: [
            {
                id: `contrib-${title.toLowerCase().replace(/\s+/g, '-')}`,
                ordinal: 0,
                locked: false,
                providerType: 'provider-a',
                title,
                images: imageUrl ? [{ type: 'poster', url: imageUrl }] : [],
            },
        ],
    };
}

const mockApiSubjects: ApiSubject[] = [
    {
        id: 'subject-1',
        metadataContainerId: 'mc-1',
        kind: 'person',
        dateLastRefreshed: '2024-01-20T14:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Bryan Cranston'),
    },
    {
        id: 'subject-2',
        metadataContainerId: 'mc-2',
        kind: 'person',
        dateLastRefreshed: '2024-01-18T09:30:00Z',
        createdAt: '2024-01-02T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Denis Villeneuve'),
    },
    {
        id: 'subject-3',
        metadataContainerId: 'mc-3',
        kind: 'studio',
        dateLastRefreshed: '2024-01-26T12:00:00Z',
        createdAt: '2024-01-03T00:00:00Z',
        metadataContainer: createMockMetadataContainer('A24'),
    },
];

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });
}

function renderSubjectsPage(initialSearchParams?: string) {
    const queryClient = createTestQueryClient();
    const initialEntries = initialSearchParams ? [`/?${initialSearchParams}`] : ['/'];

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={initialEntries}>
                <SubjectsPage />
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

function renderScopedSubjectsPage(subjectTypeSlug: string, initialSearchParams?: string) {
    const queryClient = createTestQueryClient();
    const search = initialSearchParams ? `?${initialSearchParams}` : '';

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[`/library/subjects/${subjectTypeSlug}${search}`]}>
                <Routes>
                    <Route path="/library/subjects/:subjectType" element={<SubjectsPage />} />
                </Routes>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('SubjectsPage', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        mockNavigate.mockClear();
        vi.clearAllMocks();
        vi.stubGlobal('matchMedia', createMatchMedia(1024));
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);

        mockUseLibraryTypes.mockReturnValue({
            contentTypes: [],
            subjectTypes: [
                { id: 'person', name: 'Person', pluralName: 'People', slug: 'person', iconName: 'person', levels: [] },
                {
                    id: 'studio',
                    name: 'Studio',
                    pluralName: 'Studios',
                    slug: 'studio',
                    iconName: 'buildings',
                    levels: [],
                },
            ],
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
        mockTypeIconName.mockImplementation(() => 'person');

        vi.mocked(useSubjects).mockReturnValue({
            subjects: mockApiSubjects,
            isLoading: false,
            error: null,
            totalItems: 0,
            pageNumber: 1,
            pageSize: 50,
            updateSubjectTags: vi.fn(),
            isUpdating: false,
            deleteSubject: vi.fn(),
            isDeleting: false,
            refetch: mockRefetch,
        });
    });

    afterEach(() => {
        vi.useRealTimers();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it('renders the page with correct data-page attribute', () => {
        renderSubjectsPage();

        expect(document.querySelector('[data-page="subjects"]')).toBeInTheDocument();
    });

    describe('terminology', () => {
        it('uses the generic umbrella title when unscoped', () => {
            renderSubjectsPage();

            expect(screen.getByRole('heading', { name: 'Subjects', level: 1 })).toBeInTheDocument();
        });

        it('shows the subject type friendly plural name when scoped', () => {
            renderScopedSubjectsPage('person');

            expect(screen.getByRole('heading', { name: 'People', level: 1 })).toBeInTheDocument();
            expect(screen.queryByRole('heading', { name: 'Subjects', level: 1 })).not.toBeInTheDocument();
        });

        it('uses extension-provided singular and plural names', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: [
                    {
                        id: 'story-guide',
                        name: 'Narrative Guide',
                        pluralName: 'Narrative Guides',
                        slug: 'story-guide',
                        levels: [],
                    },
                ],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            vi.mocked(useSubjects).mockReturnValue({
                subjects: [
                    {
                        ...mockApiSubjects[0],
                        id: 'subject-extension',
                        kind: 'story-guide',
                        metadataContainer: createMockMetadataContainer('Jane Doe'),
                    },
                ],
                isLoading: false,
                error: null,
                totalItems: 1,
                pageNumber: 1,
                pageSize: 50,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderScopedSubjectsPage('story-guide');

            expect(screen.getByRole('heading', { name: 'Narrative Guides', level: 1 })).toBeInTheDocument();
            const list = document.querySelector('[data-component="subject-list"]') as HTMLElement;
            expect(within(list).getByText('Narrative Guide')).toBeInTheDocument();
            expect(screen.queryByText('Story Guide')).not.toBeInTheDocument();
        });

        it('sets the browser tab title to the umbrella label when unscoped', () => {
            renderSubjectsPage();

            expect(document.title).toBe('Subjects - Magpie');
        });

        it('sets the browser tab title to the scoped plural name when scoped', () => {
            renderScopedSubjectsPage('studio');

            expect(screen.getByRole('heading', { name: 'Studios', level: 1 })).toBeInTheDocument();
            expect(document.title).toBe('Studios - Magpie');
        });

        it("resolves the scoped subject type's icon name", () => {
            renderScopedSubjectsPage('person');

            expect(mockTypeIconName).toHaveBeenCalledWith('person', { subject: true });
            expect(screen.getByTestId('subjects-page__icon')).toBeInTheDocument();
        });

        it('surfaces an operator-facing error title', () => {
            vi.mocked(useSubjects).mockReturnValue({
                subjects: [],
                isLoading: false,
                error: new Error('Network Error'),
                totalItems: 0,
                pageNumber: 1,
                pageSize: 50,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderSubjectsPage();

            expect(screen.getByText('Failed to load subjects')).toBeInTheDocument();
            expect(screen.queryByText('Failed to load people')).not.toBeInTheDocument();
        });
    });

    describe('advanced filter (FilterBar, like content)', () => {
        it.each([
            { state: 'initial', subjects: [], error: null },
            { state: 'loaded', subjects: mockApiSubjects, error: null },
            { state: 'failed', subjects: [], error: new Error('Request failed') },
        ])('filter_PreservesEntry_WhenSubjectRequestIsPendingWith$state data', async ({ subjects, error }) => {
            // Arrange
            vi.useFakeTimers();
            const initialRequest = { ...vi.mocked(useSubjects)(), subjects, isLoading: true };
            let finishRequest!: (result: ReturnType<typeof useSubjects>) => void;
            vi.mocked(useSubjects).mockImplementation(function useSubjectRequest() {
                const [result, setResult] = useState(initialRequest);
                finishRequest = setResult;
                return result;
            });
            const queryClient = createTestQueryClient();
            const router = createMemoryRouter([{ path: '/library/subjects/:subjectType', element: <SubjectsPage /> }], {
                initialEntries: ['/library/subjects/person?filter=title+contains+%22A%22'],
            });
            render(
                <QueryClientProvider client={queryClient}>
                    <RouterProvider router={router} />
                </QueryClientProvider>,
            );
            const input = screen.getByRole('textbox', { name: 'Name' });
            expect(input).toBeEnabled();
            expect(screen.getByRole('textbox', { name: 'Tag' })).toBeEnabled();

            // Act
            input.focus();
            fireEvent.change(input, { target: { value: 'AB' } });
            await act(async () => {
                vi.advanceTimersByTime(350);
            });
            fireEvent.change(input, { target: { value: 'ABC' } });
            act(() => {
                finishRequest({ ...initialRequest, subjects: error ? [] : mockApiSubjects, isLoading: false, error });
            });

            // Assert
            expect(input).toHaveValue('ABC');
            expect(input).toBeInTheDocument();
            expect(input).toHaveFocus();
            expect((input as HTMLInputElement).selectionStart).toBe(3);
            await act(async () => {
                vi.advanceTimersByTime(350);
            });
            expect(router.state.location.search).toBe('?filter=title+contains+%22ABC%22');
            expect(useSubjects).toHaveBeenLastCalledWith(
                expect.objectContaining({ filter: 'kind = "person" and (title contains "ABC")' }),
            );
        });

        it('starts expanded when the subjects table layout is visible at 1024px', () => {
            const matchMedia = createMatchMedia(1024);
            vi.spyOn(window, 'matchMedia').mockImplementation(matchMedia);

            renderSubjectsPage();

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(document.querySelector('.subjects-page__table')).toBeInTheDocument();
            expect(matchMedia).toHaveBeenCalledWith('(min-width: 1024px)');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('starts collapsed in the card layout at 1023px', () => {
            const matchMedia = createMatchMedia(1023);
            vi.spyOn(window, 'matchMedia').mockImplementation(matchMedia);

            renderSubjectsPage();

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(document.querySelector('.subjects-page__table')).not.toBeInTheDocument();
            expect(matchMedia).toHaveBeenCalledWith('(min-width: 1024px)');
            expect(filterBar).toHaveAttribute('data-expanded', 'false');
        });

        it('renders the shared FilterBar in subject context', () => {
            renderSubjectsPage();

            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
        });

        it('no longer renders the one-off SubjectFilters control', () => {
            renderSubjectsPage();

            expect(document.querySelector('[data-component="subject-filters"]')).not.toBeInTheDocument();
        });

        it('does not render any saved-filter UI (no Saved picker, no Save button)', async () => {
            const user = userEvent.setup();
            renderSubjectsPage('filter=kind+%3D+%22person%22');

            await user.click(document.querySelector('[data-action="toggle-filter"]') as HTMLElement);

            expect(document.querySelector('[data-action="apply-saved-filter"]')).not.toBeInTheDocument();
            expect(document.querySelector('[data-action="save-filter"]')).not.toBeInTheDocument();
            expect(screen.queryByText('Saved')).not.toBeInTheDocument();
        });

        it('advertises only the curated field set (kind, title, tag) in the advanced editor hint', async () => {
            const user = userEvent.setup();
            renderSubjectsPage('filter=kind+%3D+%22person%22');

            await user.click(document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement);

            const hint = document.querySelector('.filter-bar__hint');
            expect(hint).toBeInTheDocument();
            const hintText = hint!.textContent ?? '';
            expect(hintText).toContain('kind');
            expect(hintText).toContain('title');
            expect(hintText).toContain('tag');
            expect(hintText).not.toContain('genre');
            expect(hintText).not.toContain('monitoring');
        });

        it('sends the filter expression to the server as a query param', async () => {
            const calls: (SubjectQueryParams | undefined)[] = [];
            vi.mocked(useSubjects).mockImplementation((params) => {
                calls.push(params);
                return {
                    subjects: params?.filter ? [mockApiSubjects[0]] : mockApiSubjects,
                    isLoading: false,
                    error: null,
                    totalItems: 0,
                    pageNumber: 1,
                    pageSize: 50,
                    updateSubjectTags: vi.fn(),
                    isUpdating: false,
                    deleteSubject: vi.fn(),
                    isDeleting: false,
                    refetch: mockRefetch,
                };
            });

            renderSubjectsPage('filter=kind+%3D+%22person%22');

            await waitFor(() => {
                expect(calls.some((p) => p?.filter === 'kind = "person"')).toBe(true);
            });
        });

        it('folds the scoped kind into the server filter', async () => {
            const calls: (SubjectQueryParams | undefined)[] = [];
            vi.mocked(useSubjects).mockImplementation((params) => {
                calls.push(params);
                return {
                    subjects: mockApiSubjects.filter((s) => s.kind === 'person'),
                    isLoading: false,
                    error: null,
                    totalItems: 0,
                    pageNumber: 1,
                    pageSize: 50,
                    updateSubjectTags: vi.fn(),
                    isUpdating: false,
                    deleteSubject: vi.fn(),
                    isDeleting: false,
                    refetch: mockRefetch,
                };
            });

            renderScopedSubjectsPage('person');

            await waitFor(() => {
                expect(calls.some((p) => p?.filter === 'kind = "person"')).toBe(true);
            });
        });

        it('shows the scoped kind in the simple filter and prevents changing it', () => {
            renderScopedSubjectsPage('person');

            const kind = screen.getByRole('combobox', { name: 'Kind' });
            expect(kind).toHaveTextContent('Person');
            expect(kind).toHaveAttribute('data-disabled');
        });

        it('removes route-owned kind clauses while keeping other query filters', async () => {
            const calls: (SubjectQueryParams | undefined)[] = [];
            vi.mocked(useSubjects).mockImplementation((params) => {
                calls.push(params);
                return {
                    subjects: mockApiSubjects.filter((s) => s.kind === 'person'),
                    isLoading: false,
                    error: null,
                    totalItems: 0,
                    pageNumber: 1,
                    pageSize: 50,
                    updateSubjectTags: vi.fn(),
                    isUpdating: false,
                    deleteSubject: vi.fn(),
                    isDeleting: false,
                    refetch: mockRefetch,
                };
            });

            renderScopedSubjectsPage('person', 'filter=kind+%3D+%22studio%22+and+title+contains+%22Bryan%22');

            await waitFor(() => {
                expect(calls.some((p) => p?.filter === 'kind = "person" and (title contains "Bryan")')).toBe(true);
            });
        });

        it('groups top-level OR filters under the scoped kind', async () => {
            const calls: (SubjectQueryParams | undefined)[] = [];
            vi.mocked(useSubjects).mockImplementation((params) => {
                calls.push(params);
                return {
                    subjects: mockApiSubjects.filter((s) => s.kind === 'person'),
                    isLoading: false,
                    error: null,
                    totalItems: 0,
                    pageNumber: 1,
                    pageSize: 50,
                    updateSubjectTags: vi.fn(),
                    isUpdating: false,
                    deleteSubject: vi.fn(),
                    isDeleting: false,
                    refetch: mockRefetch,
                };
            });

            renderScopedSubjectsPage('person', 'filter=title+contains+%22Bryan%22+or+tag+has+%22favorite%22');

            await waitFor(() => {
                expect(
                    calls.some(
                        (p) => p?.filter === 'kind = "person" and (title contains "Bryan" or tag has "favorite")',
                    ),
                ).toBe(true);
            });
        });

        it('preserves opaque top-level OR filters that start with kind', async () => {
            const calls: (SubjectQueryParams | undefined)[] = [];
            vi.mocked(useSubjects).mockImplementation((params) => {
                calls.push(params);
                return {
                    subjects: mockApiSubjects.filter((s) => s.kind === 'person'),
                    isLoading: false,
                    error: null,
                    totalItems: 0,
                    pageNumber: 1,
                    pageSize: 50,
                    updateSubjectTags: vi.fn(),
                    isUpdating: false,
                    deleteSubject: vi.fn(),
                    isDeleting: false,
                    refetch: mockRefetch,
                };
            });

            renderScopedSubjectsPage('person', 'filter=kind+%3D+%22studio%22+or+title+contains+%22Bryan%22');

            await waitFor(() => {
                expect(
                    calls.some((p) => p?.filter === 'kind = "person" and (kind = "studio" or title contains "Bryan")'),
                ).toBe(true);
            });
        });
    });

    describe('list', () => {
        it('renders the subject list with rows from the server', () => {
            renderSubjectsPage();

            const list = document.querySelector('[data-component="subject-list"]') as HTMLElement;
            expect(list).toBeInTheDocument();
            expect(within(list).getByText('Bryan Cranston')).toBeInTheDocument();
            expect(within(list).getByText('A24')).toBeInTheDocument();
        });

        it('navigates to detail when a row is clicked', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const list = document.querySelector('[data-component="subject-list"]') as HTMLElement;
            const row = within(list).getByText('Bryan Cranston').closest('tr');
            await user.click(row!);

            expect(mockNavigate).toHaveBeenCalledWith('/subjects/subject-1');
        });

        it('requests title-ascending sort from the server by default', async () => {
            const calls: (SubjectQueryParams | undefined)[] = [];
            vi.mocked(useSubjects).mockImplementation((params) => {
                calls.push(params);
                return {
                    subjects: mockApiSubjects,
                    isLoading: false,
                    error: null,
                    totalItems: 0,
                    pageNumber: 1,
                    pageSize: 50,
                    updateSubjectTags: vi.fn(),
                    isUpdating: false,
                    deleteSubject: vi.fn(),
                    isDeleting: false,
                    refetch: mockRefetch,
                };
            });

            renderSubjectsPage();

            await waitFor(() => {
                expect(calls.some((p) => p?.sortBy === 'title' && p?.sortDirection === 'Ascending')).toBe(true);
            });
        });

        it('renders rows in server order without re-sorting the page client-side', () => {
            renderSubjectsPage();

            const rows = screen.getAllByRole('row');
            expect(rows[1]).toHaveTextContent('Bryan Cranston');
        });

        it('drives the server sort when a sortable column header is clicked', async () => {
            const user = userEvent.setup();
            const calls: (SubjectQueryParams | undefined)[] = [];
            vi.mocked(useSubjects).mockImplementation((params) => {
                calls.push(params);
                return {
                    subjects: mockApiSubjects,
                    isLoading: false,
                    error: null,
                    totalItems: 0,
                    pageNumber: 1,
                    pageSize: 50,
                    updateSubjectTags: vi.fn(),
                    isUpdating: false,
                    deleteSubject: vi.fn(),
                    isDeleting: false,
                    refetch: mockRefetch,
                };
            });

            renderSubjectsPage();

            await user.click(screen.getByRole('button', { name: /sort by kind/i }));

            await waitFor(() => {
                expect(calls.some((p) => p?.sortBy === 'kind')).toBe(true);
            });
        });

        it('navigates every subjects page from the card pager', async () => {
            // Arrange
            vi.spyOn(window, 'matchMedia').mockImplementation(createMatchMedia(900));
            vi.mocked(useSubjects).mockReturnValue({ ...vi.mocked(useSubjects)(), totalItems: 151 });
            const user = userEvent.setup();
            renderSubjectsPage();

            // Act
            await user.click(screen.getByRole('button', { name: 'Go to next page' }));

            // Assert
            expect(useSubjects).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2, pageSize: 50 }));
            await user.click(screen.getByRole('button', { name: 'Go to last page' }));
            expect(useSubjects).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 4 }));
            await user.click(screen.getByRole('button', { name: 'Go to previous page' }));
            expect(useSubjects).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 3 }));
            await user.click(screen.getByRole('button', { name: 'Go to first page' }));
            expect(useSubjects).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 1 }));
        });

        it('renders media-card router links without the desktop table in the card layout', () => {
            vi.spyOn(window, 'matchMedia').mockImplementation(createMatchMedia(900));
            renderSubjectsPage();

            const mobileList = screen.getByTestId('subjects-page__mobile-list');
            const card = within(mobileList).getByText('Bryan Cranston').closest('a');
            expect(card).toHaveAttribute('href', '/subjects/subject-1');
            expect(document.querySelector('.subjects-page__table')).not.toBeInTheDocument();
        });
    });

    describe('empty states', () => {
        function mockEmptySubjects() {
            vi.mocked(useSubjects).mockReturnValue({
                subjects: [],
                isLoading: false,
                error: null,
                totalItems: 0,
                pageNumber: 1,
                pageSize: 50,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });
        }

        it('hides the sortable column headers on the cold-start empty (no filter active)', () => {
            mockEmptySubjects();
            renderSubjectsPage();

            const list = document.querySelector('[data-component="subject-list"]') as HTMLElement;
            expect(within(list).getByText(/no .* yet/i)).toBeInTheDocument();
            expect(within(list).queryByRole('button', { name: /sort by title/i })).not.toBeInTheDocument();
        });

        it('keeps the sortable column headers live above the no-matches state when a filter matches nothing', () => {
            mockEmptySubjects();
            renderSubjectsPage('filter=kind+%3D+%22person%22');

            const list = document.querySelector('[data-component="subject-list"]') as HTMLElement;
            expect(within(list).getByText('No matches found')).toBeInTheDocument();
            expect(within(list).getByRole('button', { name: /sort by title/i })).toBeInTheDocument();
        });

        it('shows the loading state, not the empty state, while the first query is still pending', () => {
            vi.mocked(useSubjects).mockReturnValue({
                subjects: [],
                isLoading: true,
                error: null,
                totalItems: 0,
                pageNumber: 1,
                pageSize: 50,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderScopedSubjectsPage('person');

            const list = document.querySelector('[data-component="subject-list"]') as HTMLElement;
            expect(within(list).getByTestId('subjects-page__loading')).toBeInTheDocument();
            expect(within(list).queryByText(/no .* yet/i)).not.toBeInTheDocument();
        });
    });

    describe('error state', () => {
        it('shows retry button and calls refetch', async () => {
            const user = userEvent.setup();
            vi.mocked(useSubjects).mockReturnValue({
                subjects: [],
                isLoading: false,
                error: new Error('Network Error'),
                totalItems: 0,
                pageNumber: 1,
                pageSize: 50,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderSubjectsPage();

            await user.click(screen.getByRole('button', { name: 'Retry' }));
            expect(mockRefetch).toHaveBeenCalledTimes(1);
        });
    });

    describe('extension slot', () => {
        it('renders the subject list slot', () => {
            renderSubjectsPage();

            const slotWrapper = document.querySelector('[data-slot="subject.list"]');
            expect(slotWrapper).toBeInTheDocument();
        });
    });

    describe('URL persistence', () => {
        it('restores the filter expression from URL params and expands the panel', () => {
            renderSubjectsPage('filter=kind+%3D+%22person%22');

            const filterBar = document.querySelector('[data-component="filter-bar"]') as HTMLElement;
            expect(filterBar).toBeInTheDocument();
            expect(within(filterBar).getByText('Active')).toBeInTheDocument();
        });
    });
});
