import { describe, expect, it, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';

import { useAddAnotherStep } from './useAddAnotherStep';

describe('useAddAnotherStep', () => {
    it('markAdded shows the added state and closes the form without advancing', () => {
        const onAdvance = vi.fn();
        const { result } = renderHook(() => useAddAnotherStep(onAdvance));

        act(() => {
            result.current.setFormOpen(true);
        });
        act(() => {
            result.current.markAdded();
        });

        expect(result.current.added).toBe(true);
        expect(result.current.formOpen).toBe(false);
        expect(onAdvance).not.toHaveBeenCalled();
    });

    it('addAnother clears the added state and reopens a fresh form', () => {
        const { result } = renderHook(() => useAddAnotherStep(vi.fn()));
        act(() => {
            result.current.markAdded();
        });

        act(() => {
            result.current.addAnother();
        });

        expect(result.current.added).toBe(false);
        expect(result.current.formOpen).toBe(true);
    });

    it('continueToNext advances to the next step', () => {
        const onAdvance = vi.fn();
        const { result } = renderHook(() => useAddAnotherStep(onAdvance));

        act(() => {
            result.current.continueToNext();
        });

        expect(onAdvance).toHaveBeenCalledTimes(1);
    });
});
