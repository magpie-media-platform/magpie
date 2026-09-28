import { act, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { EditorView } from '@codemirror/view';

import { FilterQueryEditor } from '@/editor/FilterQueryEditor';
import { useDebouncedValue } from './useDebouncedValue';
import { useLiveUrlFilter } from './useLiveUrlFilter';
import { useUrlFilterState } from './useUrlFilterState';

function FilterPage({ advanced = false, replace = false }: { advanced?: boolean; replace?: boolean }) {
    const [state, setState] = useUrlFilterState({ filter: '', sort: '', savedFilter: '' });
    const location = useLocation();
    const liveFilter = useLiveUrlFilter({
        value: state.filter,
        onCommit: (value, replace, state) => {
            setState({ filter: value, savedFilter: '' }, { replace, state });
        },
        externalKey: state.savedFilter,
        replace,
    });
    const requestedFilter = useDebouncedValue(liveFilter.value, 300);

    return (
        <>
            {advanced ? (
                <FilterQueryEditor value={liveFilter.value} onChange={liveFilter.change} data-testid="editor" />
            ) : (
                <input
                    aria-label="Title"
                    value={liveFilter.value}
                    onChange={(event) => liveFilter.change(event.target.value)}
                />
            )}
            <output data-testid="route">{location.pathname + location.search}</output>
            <output data-testid="request">{requestedFilter}</output>
            <button type="button" onClick={() => liveFilter.change('', true)}>
                Clear
            </button>
            <button type="button" onClick={() => setState({ sort: 'title' }, { replace: true })}>
                Sort
            </button>
            <button type="button" onClick={() => setState({ savedFilter: 'saved-1' }, { replace: true })}>
                Restore saved
            </button>
        </>
    );
}

afterEach(() => {
    vi.useRealTimers();
});

describe('useLiveUrlFilter', () => {
    it('change_PreservesInputAndHistory_WhenTypingContinuesAfterPause', async () => {
        // Arrange
        vi.useFakeTimers();
        const router = createMemoryRouter([{ path: '/library/:kind', element: <FilterPage /> }], {
            initialEntries: ['/library/all'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Title' });
        input.focus();

        // Act
        fireEvent.change(input, { target: { value: 'A' } });
        // Assert
        expect(input).toHaveValue('A');
        expect(screen.getByTestId('route')).toHaveTextContent('/library/all');

        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        expect(screen.getByTestId('route')).toHaveTextContent('filter=A');
        expect(screen.getByTestId('request')).toHaveTextContent('A');

        fireEvent.change(input, { target: { value: 'AB' } });
        expect(input).toHaveValue('AB');
        expect(input).toHaveFocus();
        expect((input as HTMLInputElement).selectionStart).toBe(2);

        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        expect(screen.getByTestId('route')).toHaveTextContent('filter=AB');
        expect(screen.getByTestId('request')).toHaveTextContent('AB');

        await act(async () => {
            await router.navigate(-1);
        });
        expect(input).toHaveValue('');
        expect(screen.getByTestId('route')).toHaveTextContent('/library/all');

        await act(async () => {
            await router.navigate(1);
        });
        expect(input).toHaveValue('AB');
    });

    it('change_PreservesLatestInput_WhenOlderRouteFeedbackArrives', async () => {
        // Arrange
        vi.useFakeTimers();
        let finishNavigation!: (value: null) => void;
        const router = createMemoryRouter(
            [
                {
                    path: '/library/:kind',
                    element: <FilterPage />,
                    loader: () =>
                        new Promise((resolve) => {
                            finishNavigation = resolve;
                        }),
                },
            ],
            {
                initialEntries: ['/library/all'],
                hydrationData: { loaderData: { '0': null } },
            },
        );
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Title' });

        // Act
        fireEvent.change(input, { target: { value: 'A' } });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        fireEvent.change(input, { target: { value: 'AB' } });

        await act(async () => {
            finishNavigation(null);
        });
        // Assert
        expect(input).toHaveValue('AB');

        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        await act(async () => {
            finishNavigation(null);
        });
        expect(screen.getByTestId('route')).toHaveTextContent('filter=AB');
    });

    it('change_PreservesSortAndCancelsPendingWrite_WhenRouteChanges', async () => {
        // Arrange
        vi.useFakeTimers();
        const router = createMemoryRouter([{ path: '/library/:kind', element: <FilterPage /> }], {
            initialEntries: ['/library/all'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Title' });

        // Act
        fireEvent.change(input, { target: { value: 'AB' } });
        fireEvent.click(screen.getByRole('button', { name: 'Sort' }));
        // Assert
        expect(input).toHaveValue('AB');

        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        expect(screen.getByTestId('route')).toHaveTextContent('sort=title');
        expect(screen.getByTestId('route')).toHaveTextContent('filter=AB');

        fireEvent.change(input, { target: { value: 'ABC' } });
        await act(async () => {
            await router.navigate('/library/movie');
        });
        expect(input).toHaveValue('');

        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        expect(screen.getByTestId('route')).toHaveTextContent('/library/movie');
        expect(screen.getByTestId('route')).not.toHaveTextContent('filter=ABC');
    });

    it('change_RestoresSource_WhenSavedFilterSelectionUsesSameQuery', async () => {
        // Arrange
        vi.useFakeTimers();
        const router = createMemoryRouter([{ path: '/library/:kind', element: <FilterPage /> }], {
            initialEntries: ['/library/all?filter=A'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Title' });

        // Act
        fireEvent.change(input, { target: { value: 'AB' } });
        fireEvent.click(screen.getByRole('button', { name: 'Restore saved' }));
        // Assert
        expect(input).toHaveValue('A');

        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        expect(screen.getByTestId('route')).toHaveTextContent('savedFilter=saved-1');
        expect(screen.getByTestId('route')).not.toHaveTextContent('filter=AB');
    });

    it('change_ReplacesHistory_WhenSubjectFilterChanges', async () => {
        // Arrange
        vi.useFakeTimers();
        const router = createMemoryRouter(
            [
                { path: '/library/subjects/:kind', element: <FilterPage replace /> },
                { path: '/prior', element: <p>Prior route</p> },
            ],
            { initialEntries: ['/prior', '/library/subjects/all'], initialIndex: 1 },
        );
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Title' });

        // Act
        fireEvent.change(input, { target: { value: 'A' } });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        fireEvent.change(input, { target: { value: 'AB' } });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        await act(async () => {
            await router.navigate(-1);
        });
        // Assert
        expect(screen.getByText('Prior route')).toBeInTheDocument();
    });

    it('change_PreservesEditorAndCaret_WhenOlderRouteFeedbackArrives', async () => {
        // Arrange
        vi.useFakeTimers();
        let finishNavigation!: (value: null) => void;
        const router = createMemoryRouter(
            [
                {
                    path: '/library/:kind',
                    element: <FilterPage advanced />,
                    loader: () =>
                        new Promise((resolve) => {
                            finishNavigation = resolve;
                        }),
                },
            ],
            {
                initialEntries: ['/library/all'],
                hydrationData: { loaderData: { '0': null } },
            },
        );
        render(<RouterProvider router={router} />);
        const view = EditorView.findFromDOM(screen.getByTestId('editor'));
        expect(view).not.toBeNull();

        // Act
        act(() => {
            view!.dispatch({ changes: { from: 0, insert: 'name contains "A"' }, selection: { anchor: 17 } });
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        act(() => {
            view!.dispatch({ changes: { from: 16, insert: 'B' }, selection: { anchor: 17 } });
        });
        await act(async () => {
            finishNavigation(null);
        });

        // Assert
        expect(view!.state.doc.toString()).toBe('name contains "AB"');
        expect(view!.state.selection.main.head).toBe(17);

        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        await act(async () => {
            finishNavigation(null);
        });
        expect(screen.getByTestId('route')).toHaveTextContent('filter=name+contains+%22AB%22');
    });

    it('change_RestoresExplicitRoute_WhenValueWasPreviouslyCommitted', async () => {
        // Arrange
        vi.useFakeTimers();
        const router = createMemoryRouter([{ path: '/library/:kind', element: <FilterPage /> }], {
            initialEntries: ['/library/all?filter=A'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Title' });

        // Act
        fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
        fireEvent.change(input, { target: { value: 'B' } });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        expect(router.state.location.search).toBe('?filter=B');
        await act(async () => {
            await router.navigate('/library/all');
        });

        // Assert
        expect(input).toHaveValue('');
        await act(async () => {
            vi.advanceTimersByTime(350);
        });
        expect(screen.getByTestId('request')).toBeEmptyDOMElement();
        expect(router.state.location.search).toBe('');
        await act(async () => {
            await router.navigate(-1);
        });
        expect(input).toHaveValue('B');
        await act(async () => {
            await router.navigate(1);
        });
        expect(input).toHaveValue('');
    });

    it('change_CancelsDraft_WhenExplicitNavigationRepeatsCurrentRoute', async () => {
        // Arrange
        vi.useFakeTimers();
        const router = createMemoryRouter([{ path: '/library/:kind', element: <FilterPage /> }], {
            initialEntries: ['/library/all?filter=A'],
        });
        render(<RouterProvider router={router} />);
        const input = screen.getByRole('textbox', { name: 'Title' });

        // Act
        fireEvent.change(input, { target: { value: 'AB' } });
        await act(async () => {
            await router.navigate('/library/all?filter=A');
        });
        await act(async () => {
            vi.advanceTimersByTime(350);
        });

        // Assert
        expect(input).toHaveValue('A');
        expect(router.state.location.search).toBe('?filter=A');
        expect(screen.getByTestId('request')).toHaveTextContent(/^A$/);
    });
});
