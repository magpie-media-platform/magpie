import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { EventStreamPayload } from './useEventStream';
import { useExternalChangeConflict } from './useExternalChangeConflict';

let streamHandler: ((event: EventStreamPayload) => void) | null = null;

vi.mock('./useEventStream', () => ({
    useEventStream: (handler: (event: EventStreamPayload) => void) => {
        streamHandler = handler;
    },
}));

function event(name: string, context: Record<string, string>): EventStreamPayload {
    return {
        name,
        context,
    };
}

function renderConflict(dirty = true) {
    return renderHook(() =>
        useExternalChangeConflict({
            dirty,
            entityId: 'wishlist-1',
            eventNames: ['wishlist.updated', 'wishlist.deleted'],
            deletedEventNames: ['wishlist.deleted'],
            contextKeys: ['wishlist.id'],
        }),
    );
}

describe('useExternalChangeConflict', () => {
    beforeEach(() => {
        streamHandler = null;
        vi.useRealTimers();
    });

    it('marks a dirty editor stale only for its own entity', () => {
        const { result } = renderConflict();

        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-2' })));
        expect(result.current.state).toBeNull();

        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));
        expect(result.current.state).toBe('stale');
    });

    it('ignores a matching update while clean', () => {
        const { result } = renderConflict(false);

        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));

        expect(result.current.state).toBeNull();
    });

    it('marks a matching deletion even while clean', () => {
        const { result } = renderConflict(false);

        act(() => streamHandler?.(event('wishlist.deleted', { 'wishlist.id': 'wishlist-1' })));

        expect(result.current.state).toBe('deleted');
    });

    it('marks a dirty editor deleted', () => {
        const dirty = renderConflict();

        act(() => streamHandler?.(event('wishlist.deleted', { 'wishlist.id': 'wishlist-1' })));
        expect(dirty.result.current.state).toBe('deleted');
    });

    it('clears stale state when a draft is reverted but retains deletion state', () => {
        const { result, rerender } = renderHook(
            ({ dirty }: { dirty: boolean }) =>
                useExternalChangeConflict({
                    dirty,
                    entityId: 'wishlist-1',
                    eventNames: ['wishlist.updated', 'wishlist.deleted'],
                    deletedEventNames: ['wishlist.deleted'],
                    contextKeys: ['wishlist.id'],
                }),
            { initialProps: { dirty: true } },
        );

        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));
        rerender({ dirty: false });
        expect(result.current.state).toBeNull();

        rerender({ dirty: true });
        act(() => streamHandler?.(event('wishlist.deleted', { 'wishlist.id': 'wishlist-1' })));
        rerender({ dirty: false });
        expect(result.current.state).toBe('deleted');
    });

    it('marks a matching update stale while a save is in flight', () => {
        const { result } = renderConflict();

        act(() => result.current.beginOwnSave());
        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));

        expect(result.current.state).toBe('stale');
    });

    it('retains a conflict observed while a successful save was in flight', () => {
        const { result } = renderConflict();

        act(() => result.current.beginOwnSave());
        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));
        act(() => result.current.endOwnSave(true));

        expect(result.current.state).toBe('stale');
    });

    it('clears a pre-existing conflict after a confirmed overwrite succeeds', () => {
        const { result } = renderConflict();

        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));
        act(() => result.current.beginOwnSave());
        act(() => result.current.endOwnSave(true));

        expect(result.current.state).toBeNull();
    });

    it('retains a conflict when the save fails', () => {
        const { result } = renderConflict();

        act(() => result.current.beginOwnSave());
        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));
        act(() => result.current.endOwnSave(false));

        expect(result.current.state).toBe('stale');
    });

    it('marks an event that arrives after a successful save stale', () => {
        const { result } = renderConflict();

        act(() => result.current.beginOwnSave());
        act(() => result.current.endOwnSave(true));
        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));

        expect(result.current.state).toBe('stale');
    });

    it('respects event ordering when completion and notification share a render batch', () => {
        const { result } = renderConflict();

        act(() => result.current.beginOwnSave());
        act(() => {
            result.current.endOwnSave(true);
            streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' }));
        });

        expect(result.current.state).toBe('stale');
    });

    it('retains an earlier notification when completion shares a render batch', () => {
        const { result } = renderConflict();

        act(() => result.current.beginOwnSave());
        act(() => {
            streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' }));
            result.current.endOwnSave(true);
        });

        expect(result.current.state).toBe('stale');
    });

    it('does not treat the next event after a save as an echo', () => {
        vi.useFakeTimers();
        const { result } = renderConflict();

        act(() => result.current.beginOwnSave());
        act(() => result.current.endOwnSave(true));
        act(() => {
            vi.advanceTimersByTime(1);
        });
        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-1' })));

        expect(result.current.state).toBe('stale');
    });

    it('does not let an old save completion clear a conflict after the entity changes', () => {
        const { result, rerender } = renderHook(
            ({ entityId }: { entityId: string }) =>
                useExternalChangeConflict({
                    dirty: true,
                    entityId,
                    eventNames: ['wishlist.updated', 'wishlist.deleted'],
                    deletedEventNames: ['wishlist.deleted'],
                    contextKeys: ['wishlist.id'],
                }),
            { initialProps: { entityId: 'wishlist-1' } },
        );

        act(() => result.current.beginOwnSave());
        rerender({ entityId: 'wishlist-2' });
        act(() => streamHandler?.(event('wishlist.updated', { 'wishlist.id': 'wishlist-2' })));
        act(() => result.current.endOwnSave(true));

        expect(result.current.state).toBe('stale');
    });
});
