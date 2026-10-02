import { act, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { LIBRARY_FILTER_DEFAULTS } from './libraryPageConstants';
import { useLibraryFilters } from './useLibraryFilters';

const mocks = vi.hoisted(() => ({ update: vi.fn(), create: vi.fn(), toast: vi.fn() }));
vi.mock('@/components/ui/Toast', () => ({ useToast: () => ({ toast: mocks.toast }) }));
vi.mock('@/hooks/useFilters', () => ({
    useContentFilters: () => ({
        filters: [{ id: 'saved-1', name: 'Saved', query: 'title contains "A"', criteriaCount: 1 }],
        isLoading: false,
        listError: null,
        refetch: vi.fn(),
    }),
    useCreateContentFilter: () => ({ mutateAsync: mocks.create }),
    useUpdateContentFilter: () => ({ mutateAsync: mocks.update }),
}));
vi.mock('@/lib/magpieQl', () => ({ parseMagpieQl: async () => ({ criteria: {}, error: null }) }));

function LibraryFilterPage() {
    const [params, updateParams] = useUrlFilterState(LIBRARY_FILTER_DEFAULTS);
    const { kind } = useParams();
    const filters = useLibraryFilters({
        params,
        updateParams,
        contentTypes: [],
        routeContentTypeSlug: kind,
        selectedLevel: null,
        user: null,
        isAdmin: true,
        canWriteFilters: true,
    });
    return (
        <>
            <input
                aria-label="Filter"
                value={filters.filterQuery}
                onChange={(event) => filters.handleFilterQueryChange(event.target.value)}
            />
            <button onClick={() => void filters.handleUpdateFilter()}>Update</button>
            <button onClick={() => void filters.handleSaveFilter('Created')}>Create</button>
            <output data-testid="request">{filters.debouncedFilterQuery}</output>
            <output data-testid="saved-query">{filters.referencedSavedFilter?.query}</output>
            <output data-testid="save-label">{filters.saveControlLabel}</output>
        </>
    );
}

beforeEach(() => {
    vi.resetAllMocks();
    vi.useFakeTimers();
});
afterEach(() => {
    vi.useRealTimers();
});

describe('useLibraryFilters', () => {
    it('save_PreservesNewSavedFilter_WhenEarlierUpdateCompletesAfterCreate', async () => {
        // Arrange
        let finishEarlier!: () => void;
        mocks.update.mockImplementation(
            () =>
                new Promise<void>((resolve) => {
                    finishEarlier = resolve;
                }),
        );
        mocks.create.mockResolvedValue({ id: 'created-2' });
        const router = createMemoryRouter([{ path: '/library/:kind', element: <LibraryFilterPage /> }], {
            initialEntries: ['/library/movie?savedFilter=saved-1'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Filter' });

        // Act
        fireEvent.change(input, { target: { value: 'title contains "AB"' } });
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Update' }));
        });
        fireEvent.change(input, { target: { value: 'title contains "ABC"' } });
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Create' }));
        });
        await act(async () => {
            finishEarlier();
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        // Assert
        expect(input).toHaveValue('title contains "ABC"');
        expect(screen.getByTestId('saved-query')).toHaveTextContent('title contains "ABC"');
        expect(router.state.location.search).toBe('?savedFilter=created-2');
    });

    it.each([
        ['Update', '?filter=title+contains+%22ABC%22'],
        ['Create', '?savedFilter=created-2'],
    ])('save_PreservesLatestSavedDraft_WhenEarlier%sCompletesLast', async (operation, search) => {
        // Arrange
        let finishEarlier!: (value: { id: string }) => void;
        mocks.update
            .mockImplementationOnce(
                () =>
                    new Promise((resolve) => {
                        finishEarlier = resolve;
                    }),
            )
            .mockResolvedValue({ id: 'saved-1' });
        mocks.create
            .mockImplementationOnce(
                () =>
                    new Promise((resolve) => {
                        finishEarlier = resolve;
                    }),
            )
            .mockResolvedValue({ id: 'created-2' });
        const router = createMemoryRouter([{ path: '/library/:kind', element: <LibraryFilterPage /> }], {
            initialEntries: ['/library/movie?savedFilter=saved-1'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Filter' });

        // Act
        fireEvent.change(input, { target: { value: 'title contains "AB"' } });
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: operation }));
        });
        fireEvent.change(input, { target: { value: 'title contains "ABC"' } });
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: operation }));
        });
        await act(async () => {
            finishEarlier({ id: 'created-1' });
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        // Assert
        expect(input).toHaveValue('title contains "ABC"');
        expect(screen.getByTestId('request')).toHaveTextContent('title contains "ABC"');
        expect(router.state.location.search).toBe(search);
    });

    it.each([0, 350])('save_PreservesReturnToOriginalQuery_WhenUpdateCompletesAfter%dMs', async (delay) => {
        // Arrange
        let finish!: () => void;
        mocks.update.mockImplementation(
            () =>
                new Promise<void>((resolve) => {
                    finish = resolve;
                }),
        );
        const router = createMemoryRouter([{ path: '/library/:kind', element: <LibraryFilterPage /> }], {
            initialEntries: ['/library/movie?savedFilter=saved-1'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Filter' });

        // Act
        fireEvent.change(input, { target: { value: 'title contains "AB"' } });
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Update' }));
        });
        fireEvent.change(input, { target: { value: 'title contains "A"' } });
        await act(async () => {
            vi.advanceTimersByTime(delay);
        });
        await act(async () => {
            finish();
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        // Assert
        expect(input).toHaveValue('title contains "A"');
        expect(screen.getByTestId('saved-query')).toHaveTextContent('title contains "AB"');
        expect(router.state.location.search).toBe('?filter=title+contains+%22A%22');
        expect(screen.getByTestId('request')).toHaveTextContent('title contains "A"');
    });

    it.each([
        { operation: 'Update', delay: 0, savedQuery: 'title contains "AB"' },
        { operation: 'Update', delay: 350, savedQuery: 'title contains "AB"' },
        { operation: 'Create', delay: 0, savedQuery: 'title contains "A"' },
        { operation: 'Create', delay: 350, savedQuery: 'title contains "A"' },
    ])(
        'save_PreservesLaterEntry_When$operation completes after $delay ms',
        async ({ operation, delay, savedQuery }) => {
            // Arrange
            let finish!: (value: { id: string }) => void;
            mocks.update.mockImplementation(
                () =>
                    new Promise((resolve) => {
                        finish = resolve;
                    }),
            );
            mocks.create.mockImplementation(
                () =>
                    new Promise((resolve) => {
                        finish = resolve;
                    }),
            );
            const router = createMemoryRouter([{ path: '/library/:kind', element: <LibraryFilterPage /> }], {
                initialEntries: ['/library/movie?savedFilter=saved-1'],
            });
            render(<RouterProvider router={router} />);
            const input = screen.getByRole('textbox', { name: 'Filter' });

            // Act
            fireEvent.change(input, { target: { value: 'title contains "AB"' } });
            await act(async () => {
                fireEvent.click(screen.getByRole('button', { name: operation }));
            });
            fireEvent.change(input, { target: { value: 'title contains "ABC"' } });
            await act(async () => {
                vi.advanceTimersByTime(delay);
            });
            await act(async () => {
                finish({ id: 'created-1' });
            });

            // Assert
            expect(input).toHaveValue('title contains "ABC"');
            expect(screen.getByTestId('saved-query')).toHaveTextContent(savedQuery);
            expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }));
            await act(async () => {
                vi.advanceTimersByTime(350);
            });
            expect(router.state.location.search).toBe('?filter=title+contains+%22ABC%22');
            expect(screen.getByTestId('request')).toHaveTextContent('title contains "ABC"');
        },
    );

    it.each([
        ['Update', '?savedFilter=saved-1'],
        ['Create', '?savedFilter=created-1'],
    ])('save_AppliesSuccessfulFilter_When%sCompletesWithoutLaterEntry', async (operation, search) => {
        // Arrange
        let finish!: (value: { id: string }) => void;
        mocks.update.mockImplementation(
            () =>
                new Promise((resolve) => {
                    finish = resolve;
                }),
        );
        mocks.create.mockImplementation(
            () =>
                new Promise((resolve) => {
                    finish = resolve;
                }),
        );
        const router = createMemoryRouter([{ path: '/library/:kind', element: <LibraryFilterPage /> }], {
            initialEntries: ['/library/movie?savedFilter=saved-1'],
        });
        render(<RouterProvider router={router} />);

        // Act
        fireEvent.change(screen.getByRole('textbox'), { target: { value: 'title contains "AB"' } });
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: operation }));
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        await act(async () => {
            finish({ id: 'created-1' });
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        // Assert
        expect(screen.getByRole('textbox')).toHaveValue('title contains "AB"');
        expect(screen.getByTestId('saved-query')).toHaveTextContent('title contains "AB"');
        expect(router.state.location.search).toBe(search);
    });

    it.each([
        { operation: 'Update', path: '/library/series?filter=title+contains+%22AB%22' },
        { operation: 'Create', path: '/library/series?filter=title+contains+%22AB%22' },
        { operation: 'Update', path: '/library/movie?filter=title+contains+%22AB%22' },
        { operation: 'Create', path: '/library/movie?filter=title+contains+%22AB%22' },
    ])('save_PreservesExplicitNavigation_When$operation completes at $path', async ({ operation, path }) => {
        // Arrange
        let finish!: (value: { id: string }) => void;
        mocks.update.mockImplementation(
            () =>
                new Promise((resolve) => {
                    finish = resolve;
                }),
        );
        mocks.create.mockImplementation(
            () =>
                new Promise((resolve) => {
                    finish = resolve;
                }),
        );
        const router = createMemoryRouter([{ path: '/library/:kind', element: <LibraryFilterPage /> }], {
            initialEntries: ['/library/movie?savedFilter=saved-1'],
        });
        render(<RouterProvider router={router} />);

        // Act
        fireEvent.change(screen.getByRole('textbox'), { target: { value: 'title contains "AB"' } });
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: operation }));
        });
        await act(async () => {
            await router.navigate(path);
        });
        await act(async () => {
            finish({ id: 'created-1' });
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        // Assert
        expect(router.state.location.pathname + router.state.location.search).toBe(path);
        expect(screen.getByRole('textbox')).toHaveValue('title contains "AB"');
        expect(screen.getByTestId('request')).toHaveTextContent('title contains "AB"');
    });
});
