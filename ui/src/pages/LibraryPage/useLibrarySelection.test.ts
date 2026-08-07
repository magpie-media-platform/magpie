import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import { type ContentListItem } from '@/types';

import { useLibrarySelection } from './useLibrarySelection';

function item(id: string, title: string): ContentListItem {
    return {
        id,
        title,
        level: 'movie',
        levelLabel: 'Movie',
        contentType: 'movie',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'missing',
    };
}

const contentItems = [item('a', 'Alpha'), item('b', 'Beta'), item('c', 'Gamma')];
const pageRowIds = contentItems.map((i) => i.id);

describe('useLibrarySelection', () => {
    it('exposes the selected page records as id/title pairs', () => {
        const { result } = renderHook(() => useLibrarySelection({ pageRowIds, totalItems: 3, contentItems }));

        act(() => result.current.handleRowSelectionChange({ a: true }));

        expect(result.current.selectedItems).toEqual([{ id: 'a', title: 'Alpha' }]);
        expect(result.current.selectedPageCount).toBe(1);
        expect(result.current.selectedCount).toBe(1);
        expect(result.current.hasSelection).toBe(true);
    });

    it('routes a multi-row selection change through the header toggle', () => {
        const { result } = renderHook(() => useLibrarySelection({ pageRowIds, totalItems: 3, contentItems }));

        act(() => result.current.handleRowSelectionChange({ a: true, b: true, c: true }));

        expect(result.current.selectedCount).toBe(3);
        expect(result.current.selectedPageCount).toBe(3);
    });

    it('reports zero selected after the selection is cleared', () => {
        const { result } = renderHook(() => useLibrarySelection({ pageRowIds, totalItems: 3, contentItems }));

        act(() => result.current.handleRowSelectionChange({ a: true }));
        act(() => result.current.handleRowSelectionChange({ a: true, b: true }));
        expect(result.current.displaySelectedCount).toBe(2);

        act(() => result.current.handleClearSelection());

        expect(result.current.hasSelection).toBe(false);
        expect(result.current.selectedCount).toBe(0);
        expect(result.current.displaySelectedCount).toBe(0);
    });
});
