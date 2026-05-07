import { describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';

vi.mock('@/hooks/useFilters', () => ({
    useContentFilters: () => ({
        filters: [
            { id: 'f1', name: 'Filter One', query: '', createdAt: '', modifiedAt: '' },
            { id: 'f2', name: 'Filter Two', query: '', createdAt: '', modifiedAt: '' },
        ],
    }),
}));

vi.mock('@/hooks/useQualityTierLists', () => ({
    useQualityTierLists: () => ({
        tierLists: [
            { id: 'q1', name: 'Tier A' },
            { id: 'q2', name: 'Tier B' },
        ],
    }),
}));

vi.mock('@/hooks/useTags', () => ({
    useTags: () => ({
        tags: [{ name: 'action' }, { name: 'comedy' }],
    }),
}));

import { useFormOptions } from './useFormOptions';

describe('useFormOptions', () => {
    it('returns filter options with id and name', () => {
        const { result } = renderHook(() => useFormOptions());

        expect(result.current.filterOptions).toEqual([
            { id: 'f1', name: 'Filter One' },
            { id: 'f2', name: 'Filter Two' },
        ]);
    });

    it('returns quality tier list options with id and name', () => {
        const { result } = renderHook(() => useFormOptions());

        expect(result.current.qualityTierListOptions).toEqual([
            { id: 'q1', name: 'Tier A' },
            { id: 'q2', name: 'Tier B' },
        ]);
    });

    it('returns tag options with name as both id and name', () => {
        const { result } = renderHook(() => useFormOptions());

        expect(result.current.tagOptions).toEqual([
            { id: 'action', name: 'action' },
            { id: 'comedy', name: 'comedy' },
        ]);
    });
});
