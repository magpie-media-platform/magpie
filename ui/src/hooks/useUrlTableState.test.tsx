import { describe, it, expect } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { type ReactNode } from 'react';
import type { SortingState } from '@tanstack/react-table';

import { useUrlTableState } from './useUrlTableState';

const CONFIG = {
    defaultSort: [{ id: 'publishDate', desc: true }] as SortingState,
    defaultFilters: { type: '' },
    sortKeys: ['title', 'publishDate'] as const,
};

function makeWrapper(initialUrl = '/') {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <MemoryRouter initialEntries={[initialUrl]}>{children}</MemoryRouter>;
    };
}

describe('useUrlTableState', () => {
    it('returns defaults when no URL params are set', () => {
        const { result } = renderHook(() => useUrlTableState(CONFIG), { wrapper: makeWrapper() });

        expect(result.current.sorting).toEqual([{ id: 'publishDate', desc: true }]);
        expect(result.current.filters).toEqual({ type: '' });
        expect(result.current.page).toBe(1);
    });

    it('restores sort, filter, and page from the URL', () => {
        const { result } = renderHook(() => useUrlTableState(CONFIG), {
            wrapper: makeWrapper('/?sort=title:asc&type=Movie&page=3'),
        });

        expect(result.current.sorting).toEqual([{ id: 'title', desc: false }]);
        expect(result.current.filters.type).toBe('Movie');
        expect(result.current.page).toBe(3);
    });

    it('falls back to the default sort when the URL names an unknown column', () => {
        const { result } = renderHook(() => useUrlTableState(CONFIG), {
            wrapper: makeWrapper('/?sort=bogus:asc'),
        });

        expect(result.current.sorting).toEqual([{ id: 'publishDate', desc: true }]);
    });

    it('writes a non-default sort to the URL and clears the page', () => {
        const { result } = renderHook(
            () => {
                const state = useUrlTableState(CONFIG);
                const location = useLocation();
                return { state, search: location.search };
            },
            { wrapper: makeWrapper('/?page=4') },
        );

        act(() => {
            result.current.state.setSorting([{ id: 'title', desc: false }]);
        });

        expect(result.current.search).toContain('sort=title%3Aasc');
        expect(result.current.search).not.toContain('page=');
        expect(result.current.state.page).toBe(1);
    });

    it('drops the sort param when sorting matches the default', () => {
        const { result } = renderHook(
            () => {
                const state = useUrlTableState(CONFIG);
                const location = useLocation();
                return { state, search: location.search };
            },
            { wrapper: makeWrapper('/?sort=title:asc') },
        );

        act(() => {
            result.current.state.setSorting([{ id: 'publishDate', desc: true }]);
        });

        expect(result.current.search).not.toContain('sort=');
    });

    it('writes a non-default filter to the URL and clears the page', () => {
        const { result } = renderHook(
            () => {
                const state = useUrlTableState(CONFIG);
                const location = useLocation();
                return { state, search: location.search };
            },
            { wrapper: makeWrapper('/?page=5') },
        );

        act(() => {
            result.current.state.setFilters({ type: 'Movie' });
        });

        expect(result.current.search).toContain('type=Movie');
        expect(result.current.search).not.toContain('page=');
    });

    it('drops a filter param when reset to its default', () => {
        const { result } = renderHook(
            () => {
                const state = useUrlTableState(CONFIG);
                const location = useLocation();
                return { state, search: location.search };
            },
            { wrapper: makeWrapper('/?type=Movie') },
        );

        act(() => {
            result.current.state.setFilters({ type: '' });
        });

        expect(result.current.search).not.toContain('type=');
    });

    it('drops the page param on page 1 and sets it otherwise', () => {
        const { result } = renderHook(
            () => {
                const state = useUrlTableState(CONFIG);
                const location = useLocation();
                return { state, search: location.search };
            },
            { wrapper: makeWrapper() },
        );

        act(() => {
            result.current.state.setPage(2);
        });
        expect(result.current.search).toContain('page=2');

        act(() => {
            result.current.state.setPage(1);
        });
        expect(result.current.search).not.toContain('page=');
    });

    it('pushes sort changes so back() restores the previous view', () => {
        const { result } = renderHook(
            () => {
                const state = useUrlTableState(CONFIG);
                const location = useLocation();
                const navigate = useNavigate();
                return { state, search: location.search, navigate };
            },
            { wrapper: makeWrapper('/') },
        );

        act(() => {
            result.current.state.setSorting([{ id: 'title', desc: false }]);
        });
        expect(result.current.search).toContain('sort=title%3Aasc');

        act(() => {
            void result.current.navigate(-1);
        });
        expect(result.current.search).toBe('');
    });

    it('reflects an out-of-range URL page param verbatim, leaving any clamp to the consumer', () => {
        const { result } = renderHook(
            () => {
                const state = useUrlTableState(CONFIG);
                const location = useLocation();
                return { state, search: location.search };
            },
            { wrapper: makeWrapper('/?page=99') },
        );

        expect(result.current.state.page).toBe(99);
        expect(result.current.search).toBe('?page=99');
    });

    it('keeps stable setter references across renders', () => {
        const { result, rerender } = renderHook(() => useUrlTableState(CONFIG), { wrapper: makeWrapper() });

        const { setSorting, setFilters, setPage } = result.current;
        rerender();
        expect(result.current.setSorting).toBe(setSorting);
        expect(result.current.setFilters).toBe(setFilters);
        expect(result.current.setPage).toBe(setPage);
    });
});
