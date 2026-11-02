import { StrictMode, useEffect, useRef } from 'react';
import { act, render, renderHook, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { createMemoryRouter, MemoryRouter, RouterProvider, useLocation } from 'react-router-dom';

import { useContentView } from './useContentView';
import { useUrlFilterState } from './useUrlFilterState';

describe('useContentView', () => {
    beforeEach(() => {
        localStorage.clear();
    });

    it.each([
        {
            stored: 'gallery',
            search: '?view=list',
            expectedView: 'list',
            expectedSearch: '?view=list',
            expectedStored: '"gallery"',
        },
        {
            stored: 'list',
            search: '?view=gallery',
            expectedView: 'gallery',
            expectedSearch: '?view=gallery',
            expectedStored: '"list"',
        },
        {
            stored: 'gallery',
            search: '',
            expectedView: 'gallery',
            expectedSearch: '?view=gallery',
            expectedStored: '"gallery"',
        },
        {
            stored: 'gallery',
            search: '?view=invalid',
            expectedView: 'gallery',
            expectedSearch: '?view=gallery',
            expectedStored: '"gallery"',
        },
        {
            stored: 'invalid',
            search: '',
            expectedView: 'list',
            expectedSearch: '?view=list',
            expectedStored: '"invalid"',
        },
    ])(
        'UseContentView_ResolvesExpectedView_WhenStoredIs$storedAndSearchIs$search',
        ({ stored, search, expectedView, expectedSearch, expectedStored }) => {
            // Arrange
            localStorage.setItem('content-view', JSON.stringify(stored));

            // Act
            const { result } = renderHook(() => ({ ...useContentView('content-view'), search: useLocation().search }), {
                wrapper: ({ children }) => <MemoryRouter initialEntries={[`/items${search}`]}>{children}</MemoryRouter>,
            });

            // Assert
            expect(result.current.view).toBe(expectedView);
            expect(result.current.search).toBe(expectedSearch);
            expect(localStorage.getItem('content-view')).toBe(expectedStored);
        },
    );

    it('SetView_RestoresPreviousImplicitView_WhenNavigatingBackAndRemounting', async () => {
        // Arrange
        localStorage.setItem('content-view', '"gallery"');
        const user = userEvent.setup();
        function ContentView() {
            const { view, setView } = useContentView('content-view');
            return (
                <>
                    <span>{view}</span>
                    <button onClick={() => void setView('list')}>Use list</button>
                </>
            );
        }
        const router = createMemoryRouter(
            [
                { path: '/items', element: <ContentView /> },
                { path: '/away', element: <div>Away</div> },
            ],
            {
                initialEntries: [
                    { pathname: '/items', search: '?page=3', hash: '#results', state: { from: '/library' } },
                ],
            },
        );
        render(<RouterProvider router={router} />);
        expect(screen.getByText('gallery')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?page=3&view=gallery');

        // Act
        await user.click(screen.getByRole('button', { name: 'Use list' }));

        // Assert
        expect(screen.getByText('list')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?page=3&view=list');
        expect(router.state.location.hash).toBe('#results');
        expect(router.state.location.state).toEqual({ from: '/library' });
        expect(localStorage.getItem('content-view')).toBe('"list"');

        // Act
        await act(async () => {
            await router.navigate('/away');
        });
        await act(async () => {
            await router.navigate(-1);
        });

        // Assert
        expect(screen.getByText('list')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?page=3&view=list');

        // Act
        await act(async () => {
            await router.navigate(-1);
        });

        // Assert
        expect(screen.getByText('gallery')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?page=3&view=gallery');
        expect(router.state.location.hash).toBe('#results');
        expect(router.state.location.state).toEqual({ from: '/library' });
        expect(localStorage.getItem('content-view')).toBe('"list"');

        // Act
        await act(async () => {
            await router.navigate(1);
        });

        // Assert
        expect(screen.getByText('list')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?page=3&view=list');
    });

    it('SetView_RestoresInitialView_WhenTraversingHistoryAndRemounting', async () => {
        // Arrange
        const user = userEvent.setup();
        function ContentView() {
            const { view, setView } = useContentView('content-view');
            const [, setFilter] = useUrlFilterState({ page: '' });
            return (
                <>
                    <span>{view}</span>
                    <button onClick={() => setFilter({ page: '2' })}>Next page</button>
                    <button onClick={() => void setView('gallery')}>Use gallery</button>
                </>
            );
        }
        const router = createMemoryRouter(
            [
                { path: '/items', element: <ContentView /> },
                { path: '/away', element: <div>Away</div> },
            ],
            { initialEntries: ['/items?filter=movie'] },
        );
        render(<RouterProvider router={router} />);

        // Act
        await user.click(screen.getByRole('button', { name: 'Next page' }));
        await user.click(screen.getByRole('button', { name: 'Use gallery' }));
        await act(async () => {
            await router.navigate('/away');
        });
        await act(async () => {
            await router.navigate(-1);
        });

        // Assert
        expect(screen.getByText('gallery')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?filter=movie&view=gallery&page=2');

        // Act
        await act(async () => {
            await router.navigate(-1);
        });

        // Assert
        expect(screen.getByText('list')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?filter=movie&view=list&page=2');

        // Act
        await act(async () => {
            await router.navigate(-1);
        });

        // Assert
        expect(screen.getByText('list')).toBeInTheDocument();
        expect(router.state.location.search).toBe('?filter=movie&view=list');
        expect(localStorage.getItem('content-view')).toBe('"gallery"');
    });

    it('UseContentView_PreservesPreloadUrlWrite_WhenEarlierEffectClearsRouteState', async () => {
        // Arrange
        function ContentView() {
            const location = useLocation();
            const [, setFilter] = useUrlFilterState({ filter: '', query: '' });
            const preload = location.state as { query: string } | null;
            const applied = useRef<string | null>(null);
            useEffect(() => {
                if (preload?.query && applied.current !== preload.query) {
                    applied.current = preload.query;
                    setFilter({ filter: preload.query, query: preload.query }, { replace: true, state: null });
                }
            }, [preload, setFilter]);
            const { view } = useContentView('content-view');
            return <span>{view}</span>;
        }
        const router = createMemoryRouter([{ path: '/items', element: <ContentView /> }], {
            initialEntries: [{ pathname: '/items', search: '?keep=1', state: { query: 'movie' } }],
        });

        // Act
        render(
            <StrictMode>
                <RouterProvider router={router} />
            </StrictMode>,
        );

        // Assert
        await waitFor(() => {
            expect(Object.fromEntries(new URLSearchParams(router.state.location.search))).toEqual({
                keep: '1',
                filter: 'movie',
                query: 'movie',
                view: 'list',
            });
        });
        expect(router.state.location.state).toBeNull();
        expect(screen.getByText('list')).toBeInTheDocument();

        // Act
        await act(async () => {
            await router.navigate('/items?keep=2', { state: { query: 'series' } });
        });

        // Assert
        await waitFor(() => {
            expect(Object.fromEntries(new URLSearchParams(router.state.location.search))).toEqual({
                keep: '2',
                filter: 'series',
                query: 'series',
                view: 'list',
            });
        });
        expect(router.state.location.state).toBeNull();
    });

    it('UseContentView_PreservesUrlCanonicalization_WhenLaterEffectUpdatesSearch', async () => {
        // Arrange
        function ContentView() {
            const { view } = useContentView('content-view');
            const [filter, setFilter] = useUrlFilterState({ contentType: '', filter: '' });
            useEffect(() => {
                if (filter.contentType) {
                    setFilter({ filter: filter.contentType, contentType: '' }, { replace: true });
                }
            }, [filter.contentType, setFilter]);
            return <span>{view}</span>;
        }
        const router = createMemoryRouter([{ path: '/items', element: <ContentView /> }], {
            initialEntries: ['/items?keep=1&contentType=movie'],
        });

        // Act
        render(<RouterProvider router={router} />);

        // Assert
        await waitFor(() => {
            expect(Object.fromEntries(new URLSearchParams(router.state.location.search))).toEqual({
                keep: '1',
                filter: 'movie',
                view: 'list',
            });
        });
        expect(screen.getByText('list')).toBeInTheDocument();
    });
});
