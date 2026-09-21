import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { useListSorting } from './useListSorting';

describe('useListSorting', () => {
    it('reports no active option when there are no sort options', () => {
        const { result } = renderHook(() => useListSorting([], [], () => {}));

        expect(result.current.activeOption).toBeUndefined();
        expect(result.current.isDescending).toBe(false);
    });

    it('falls back to the first option and its initial direction when nothing is sorted', () => {
        const { result } = renderHook(() =>
            useListSorting([], [{ id: 'createdAt', label: 'Added', initialDirection: 'desc' }], () => {}),
        );

        expect(result.current.activeOption?.id).toBe('createdAt');
        expect(result.current.isDescending).toBe(true);
    });

    it('reports the direction of the active sort', () => {
        const { result } = renderHook(() =>
            useListSorting(
                [{ id: 'name', desc: false }],
                [
                    { id: 'name', label: 'Name' },
                    { id: 'createdAt', label: 'Added', initialDirection: 'desc' },
                ],
                () => {},
            ),
        );

        expect(result.current.activeOption?.id).toBe('name');
        expect(result.current.isDescending).toBe(false);
    });
});
