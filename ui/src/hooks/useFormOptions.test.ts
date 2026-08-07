import { describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';

vi.mock('@/hooks/useTags', () => ({
    useTags: () => ({
        tags: [{ name: 'action' }, { name: 'comedy' }],
    }),
}));

vi.mock('@/hooks/useRootFolders', () => ({
    useRootFolders: () => ({
        folders: [],
    }),
}));

import { useFormOptions } from './useFormOptions';

describe('useFormOptions', () => {
    it('returns tag options with name as both id and name', () => {
        const { result } = renderHook(() => useFormOptions());

        expect(result.current.tagOptions).toEqual([
            { id: 'action', name: 'action' },
            { id: 'comedy', name: 'comedy' },
        ]);
    });
});
