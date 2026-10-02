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
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'missing',
    };
}

const contentItems = [{ ...item('a', 'Alpha'), tags: ['qa-exploration'] }, item('b', 'Beta'), item('c', 'Gamma')];
const pageRowIds = contentItems.map((i) => i.id);
const resetKey = 'ParentId null and ContentType = "kind-x"';

describe('useLibrarySelection', () => {
    it('exposes the selected page records with their tags', () => {
        const { result } = renderHook(() => useLibrarySelection({ pageRowIds, totalItems: 3, contentItems, resetKey }));

        act(() => result.current.selection.toggleRow('a', true));

        expect(result.current.selectedItems).toEqual([{ id: 'a', title: 'Alpha', tags: ['qa-exploration'] }]);
        expect(result.current.selection.pageSelectedCount).toBe(1);
        expect(result.current.selection.selectedCount).toBe(1);
        expect(result.current.selection.hasSelection).toBe(true);
    });

    it('selects the page through the header toggle', () => {
        const { result } = renderHook(() => useLibrarySelection({ pageRowIds, totalItems: 3, contentItems, resetKey }));

        act(() => result.current.selection.toggleHeader(true));

        expect(result.current.selection.selectedCount).toBe(3);
        expect(result.current.selection.pageSelectedCount).toBe(3);
    });

    it('reports zero selected after the selection is cleared', () => {
        const { result } = renderHook(() => useLibrarySelection({ pageRowIds, totalItems: 3, contentItems, resetKey }));

        act(() => result.current.selection.toggleRow('a', true));
        act(() => result.current.selection.toggleRow('b', true));
        expect(result.current.selection.selectedCount).toBe(2);

        act(() => result.current.selection.clear());

        expect(result.current.selection.hasSelection).toBe(false);
        expect(result.current.selection.selectedCount).toBe(0);
    });

    it('drops a select-all-across-pages when the filter scope changes', () => {
        const { result, rerender } = renderHook(
            (props: { resetKey: string }) =>
                useLibrarySelection({ pageRowIds, totalItems: 250, contentItems, resetKey: props.resetKey }),
            { initialProps: { resetKey } },
        );

        act(() => result.current.selection.selectAllAcrossPages());
        expect(result.current.selection.selectedCount).toBe(250);

        rerender({ resetKey: 'ParentId null and ContentType = "kind-y"' });

        expect(result.current.selection.allPages).toBe(false);
        expect(result.current.selection.selectedCount).toBe(0);
    });
});
