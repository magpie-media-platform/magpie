import { describe, it, expect } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { type ReactNode } from 'react';

import { useUrlFilterState } from './useUrlFilterState';

function makeWrapper(initialUrl = '/') {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <MemoryRouter initialEntries={[initialUrl]}>{children}</MemoryRouter>;
    };
}

describe('useUrlFilterState', () => {
    it('returns default values when no URL params are set', () => {
        const { result } = renderHook(() => useUrlFilterState({ status: '', sort: 'title' }), {
            wrapper: makeWrapper(),
        });

        expect(result.current[0]).toEqual({ status: '', sort: 'title' });
    });

    it('reads initial URL params over defaults', () => {
        const { result } = renderHook(() => useUrlFilterState({ status: '', sort: 'title' }), {
            wrapper: makeWrapper('/?status=active&sort=date'),
        });

        expect(result.current[0]).toEqual({ status: 'active', sort: 'date' });
    });

    it('ignores URL params not in defaults', () => {
        const { result } = renderHook(() => useUrlFilterState({ status: '' }), {
            wrapper: makeWrapper('/?status=active&unrelated=ignored'),
        });

        const state = result.current[0];
        expect(state.status).toBe('active');
        expect((state as Record<string, string>).unrelated).toBeUndefined();
    });

    it('sets a URL param when a non-default value is provided', () => {
        const { result } = renderHook(() => useUrlFilterState({ status: '', sort: 'title' }), {
            wrapper: makeWrapper(),
        });

        act(() => {
            result.current[1]({ status: 'active' });
        });

        expect(result.current[0].status).toBe('active');
        expect(result.current[0].sort).toBe('title');
    });

    it('deletes a URL param when an empty string is provided', () => {
        const { result } = renderHook(() => useUrlFilterState({ status: '' }), {
            wrapper: makeWrapper('/?status=active'),
        });

        act(() => {
            result.current[1]({ status: '' });
        });

        expect(result.current[0].status).toBe('');
    });

    it('deletes a URL param when the value matches the non-empty default', () => {
        const { result } = renderHook(() => useUrlFilterState({ sort: 'title' }), {
            wrapper: makeWrapper('/?sort=date'),
        });

        act(() => {
            result.current[1]({ sort: 'title' });
        });

        expect(result.current[0].sort).toBe('title');
    });

    it('updates multiple params in a single call', () => {
        const { result } = renderHook(() => useUrlFilterState({ status: '', sort: 'title' }), {
            wrapper: makeWrapper(),
        });

        act(() => {
            result.current[1]({ status: 'active', sort: 'date' });
        });

        expect(result.current[0]).toEqual({ status: 'active', sort: 'date' });
    });

    it('resets multiple params to defaults in a single call', () => {
        const { result } = renderHook(() => useUrlFilterState({ status: '', sort: 'title' }), {
            wrapper: makeWrapper('/?status=active&sort=date'),
        });

        act(() => {
            result.current[1]({ status: '', sort: 'title' });
        });

        expect(result.current[0]).toEqual({ status: '', sort: 'title' });
    });

    it('returns a stable setter reference across renders', () => {
        const { result, rerender } = renderHook(() => useUrlFilterState({ status: '' }), {
            wrapper: makeWrapper(),
        });

        const firstSetter = result.current[1];
        rerender();
        expect(result.current[1]).toBe(firstSetter);
    });

    it('pushes a new history entry by default so back() restores the previous filter', () => {
        const { result } = renderHook(
            () => {
                const [filters, setFilter] = useUrlFilterState({ status: '' });
                const location = useLocation();
                const navigate = useNavigate();
                return { filters, setFilter, search: location.search, navigate };
            },
            { wrapper: makeWrapper('/') },
        );

        expect(result.current.search).toBe('');

        act(() => {
            result.current.setFilter({ status: 'active' });
        });

        expect(result.current.search).toContain('status=active');

        act(() => {
            void result.current.navigate(-1);
        });

        expect(result.current.search).toBe('');
    });

    it('replaces the current history entry when options.replace = true', () => {
        const { result } = renderHook(
            () => {
                const [filters, setFilter] = useUrlFilterState({ status: '' });
                const location = useLocation();
                const navigate = useNavigate();
                return { filters, setFilter, search: location.search, navigate };
            },
            { wrapper: makeWrapper('/') },
        );

        act(() => {
            result.current.setFilter({ status: 'active' }, { replace: true });
        });

        expect(result.current.search).toContain('status=active');

        act(() => {
            void result.current.navigate(-1);
        });

        const searchAfterFirstBack = result.current.search;
        expect(searchAfterFirstBack === '' || searchAfterFirstBack.includes('status=active')).toBe(true);
    });
});
