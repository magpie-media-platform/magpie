import { createRef } from 'react';
import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { useMergedRef } from './useMergedRef';

describe('useMergedRef', () => {
    it('useMergedRef_GivesTheNodeToBothRefs_WhenOneIsAnObjectAndOneIsACallback', () => {
        // Arrange
        const objectRef = createRef<HTMLDivElement>();
        const callbackRef = vi.fn();
        const node = document.createElement('div');
        const { result } = renderHook(() => useMergedRef(objectRef, callbackRef));

        // Act
        result.current(node);

        // Assert
        expect(objectRef.current).toBe(node);
        expect(callbackRef).toHaveBeenCalledWith(node);
    });
});
