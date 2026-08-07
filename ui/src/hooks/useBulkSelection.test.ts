import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import { useBulkSelection } from './useBulkSelection';

const pageIds = ['a', 'b', 'c'];

describe('useBulkSelection', () => {
    it('defaults to an empty page selection', () => {
        const { result } = renderHook(() => useBulkSelection({ pageRowIds: pageIds, totalCount: 10 }));

        expect(result.current.allPages).toBe(false);
        expect(result.current.hasSelection).toBe(false);
        expect(result.current.selectedCount).toBe(0);
        expect(result.current.headerChecked).toBe(false);
        expect(result.current.showAcrossPagesPrompt).toBe(false);
        expect(result.current.resolve()).toEqual({ mode: 'ids', ids: [] });
    });

    it('picks individual rows in page mode', () => {
        const { result } = renderHook(() => useBulkSelection({ pageRowIds: pageIds, totalCount: 10 }));

        act(() => result.current.toggleRow('a', true));

        expect(result.current.isRowChecked('a')).toBe(true);
        expect(result.current.selectedCount).toBe(1);
        expect(result.current.headerChecked).toBe('indeterminate');
        expect(result.current.resolve()).toEqual({ mode: 'ids', ids: ['a'] });
    });

    it('offers the across-pages prompt only when the whole page is checked and more rows exist', () => {
        const { result } = renderHook(() => useBulkSelection({ pageRowIds: pageIds, totalCount: 10 }));

        act(() => result.current.toggleHeader(true));

        expect(result.current.headerChecked).toBe(true);
        expect(result.current.showAcrossPagesPrompt).toBe(true);
        expect(result.current.resolve()).toEqual({ mode: 'ids', ids: pageIds });
    });

    it('does not offer the prompt when the page already holds every matching row', () => {
        const { result } = renderHook(() => useBulkSelection({ pageRowIds: pageIds, totalCount: 3 }));

        act(() => result.current.toggleHeader(true));

        expect(result.current.showAcrossPagesPrompt).toBe(false);
    });

    it('promotes to select-all-across-pages and resolves to a filter exclusion set', () => {
        const { result } = renderHook(() => useBulkSelection({ pageRowIds: pageIds, totalCount: 10 }));

        act(() => result.current.toggleHeader(true));
        act(() => result.current.selectAllAcrossPages());

        expect(result.current.allPages).toBe(true);
        expect(result.current.selectedCount).toBe(10);
        expect(result.current.showAcrossPagesPrompt).toBe(false);
        expect(result.current.resolve()).toEqual({ mode: 'all', excludeIds: [] });
    });

    it('records hand-deselected rows as exclusions once select-all is active', () => {
        const { result } = renderHook(() => useBulkSelection({ pageRowIds: pageIds, totalCount: 10 }));

        act(() => result.current.selectAllAcrossPages());
        act(() => result.current.toggleRow('b', false));

        expect(result.current.isRowChecked('b')).toBe(false);
        expect(result.current.isRowChecked('a')).toBe(true);
        expect(result.current.selectedCount).toBe(9);
        expect(result.current.resolve()).toEqual({ mode: 'all', excludeIds: ['b'] });
    });

    it('clears everything back to the empty page-select state', () => {
        const { result } = renderHook(() => useBulkSelection({ pageRowIds: pageIds, totalCount: 10 }));

        act(() => result.current.selectAllAcrossPages());
        act(() => result.current.clear());

        expect(result.current.allPages).toBe(false);
        expect(result.current.hasSelection).toBe(false);
        expect(result.current.resolve()).toEqual({ mode: 'ids', ids: [] });
    });
});
