import { act, renderHook, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';

import { useBulkRun, type BulkRunResult } from './useBulkRun';

const items = [{ id: 'a' }, { id: 'b' }, { id: 'c' }];

function wrapper({ children }: { children: ReactNode }) {
    return (
        <ToastProvider>
            <ToastStateProvider>
                {children}
                <ToastList />
                <ToastViewport />
            </ToastStateProvider>
        </ToastProvider>
    );
}

describe('useBulkRun', () => {
    it('reports a partial failure in one toast and keeps only the failed items selected', async () => {
        const selectOnly = vi.fn();
        const action = vi.fn((item: { id: string }) =>
            item.id === 'a' ? Promise.resolve() : Promise.reject(new Error('nope')),
        );
        const { result } = renderHook(() => useBulkRun(), { wrapper });

        await act(async () => {
            await result.current(items, { selectOnly }, { action, verb: 'delete', itemNounPlural: 'rules' });
        });

        expect(screen.getAllByRole('alert')).toHaveLength(1);
        expect(screen.getByText('Failed to delete some rules')).toBeInTheDocument();
        expect(screen.getByText('1 succeeded and 2 failed. Failed rules remain selected.')).toBeInTheDocument();
        expect(selectOnly).toHaveBeenCalledWith(['b', 'c']);
    });

    it('reports a total failure without claiming any success', async () => {
        const selectOnly = vi.fn();
        const action = vi.fn(() => Promise.reject(new Error('nope')));
        const { result } = renderHook(() => useBulkRun(), { wrapper });

        await act(async () => {
            await result.current(items, { selectOnly }, { action, verb: 'delete', itemNounPlural: 'rules' });
        });

        expect(screen.getAllByRole('alert')).toHaveLength(1);
        expect(screen.getByText('Failed to delete rules')).toBeInTheDocument();
        expect(screen.getByText('3 failed. Failed rules remain selected.')).toBeInTheDocument();
        expect(selectOnly).toHaveBeenCalledWith(['a', 'b', 'c']);
    });

    it('says nothing and clears the selection when every item succeeds', async () => {
        const selectOnly = vi.fn();
        const action = vi.fn(() => Promise.resolve());
        const { result } = renderHook(() => useBulkRun(), { wrapper });

        let outcome: BulkRunResult<{ id: string }> | null = null;
        await act(async () => {
            outcome = await result.current(items, { selectOnly }, { action, verb: 'delete', itemNounPlural: 'rules' });
        });

        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        expect(selectOnly).toHaveBeenCalledWith([]);
        expect(outcome).toEqual({ succeeded: items, failed: [] });
    });

    it('applies the action to every item even after one throws', async () => {
        const selectOnly = vi.fn();
        const action = vi.fn((item: { id: string }) =>
            item.id === 'a' ? Promise.reject(new Error('nope')) : Promise.resolve(),
        );
        const { result } = renderHook(() => useBulkRun(), { wrapper });

        let outcome: BulkRunResult<{ id: string }> | null = null;
        await act(async () => {
            outcome = await result.current(items, { selectOnly }, { action, verb: 'delete', itemNounPlural: 'rules' });
        });

        expect(action).toHaveBeenCalledTimes(3);
        expect(outcome).toEqual({ succeeded: [{ id: 'b' }, { id: 'c' }], failed: [{ id: 'a' }] });
    });
});
