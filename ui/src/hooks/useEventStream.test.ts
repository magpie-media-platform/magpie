import { createElement, StrictMode, type ComponentProps, type ReactNode } from 'react';

import { render, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { installSharedWorkerHarness } from '@/test/sharedWorkerHarness';

import { useEventStream, useGlobalEventStreamConnection, type EventStreamPayload } from './useEventStream';

type Listener = (evt: { data: string }) => void;

interface MockEventSource {
    url: string;
    closed: boolean;
    onmessage: Listener | null;
    onopen: (() => void) | null;
    onerror: (() => void) | null;
    emit: (data: string) => void;
    close: () => void;
}

let sources: MockEventSource[] = [];
let testQueryClient: QueryClient;

function installFakeEventSource() {
    class FakeEventSource implements MockEventSource {
        static CLOSED = 2;
        url: string;
        closed = false;
        onmessage: Listener | null = null;
        onopen: (() => void) | null = null;
        onerror: (() => void) | null = null;
        constructor(url: string) {
            this.url = url;
            sources.push(this);
        }
        emit(data: string) {
            this.onmessage?.({ data });
        }
        close() {
            this.closed = true;
        }
    }
    (globalThis as unknown as { EventSource: typeof FakeEventSource }).EventSource = FakeEventSource;
    installSharedWorkerHarness(FakeEventSource);
}

function ActiveSession({ children }: { children: ReactNode }) {
    useGlobalEventStreamConnection(true);
    return createElement('div', null, children);
}

function ActiveSessionWrapper({ children }: { children: ReactNode }) {
    return createElement(
        QueryClientProvider,
        { client: testQueryClient },
        createElement(ActiveSession, null, children),
    );
}

function payload(name = 'download.imported', context: Record<string, string> = {}): EventStreamPayload {
    return {
        name,
        context,
    };
}

function ListenerComponent({ handler }: { handler: (event: EventStreamPayload) => void }) {
    useEventStream(handler);
    return null;
}

function SessionComponent({
    showListener,
    handler,
}: {
    showListener: boolean;
    handler: (event: EventStreamPayload) => void;
}) {
    useGlobalEventStreamConnection(true);
    return showListener ? createElement(ListenerComponent, { handler }) : null;
}

function SessionWrapper(props: ComponentProps<typeof SessionComponent>) {
    return createElement(QueryClientProvider, { client: testQueryClient }, createElement(SessionComponent, props));
}

describe('useEventStream', () => {
    beforeEach(() => {
        sources = [];
        testQueryClient = new QueryClient();
        installFakeEventSource();
    });

    afterEach(() => {
        vi.restoreAllMocks();
        vi.unstubAllGlobals();
        localStorage.clear();
    });

    it('opens one unfiltered EventSource for the active application session', () => {
        renderHook(
            () => {
                useEventStream(() => {}, { eventName: 'download.imported', entityId: 'abc-123' });
                useEventStream(() => {}, { eventName: 'metadata.refreshed' });
            },
            { wrapper: ActiveSessionWrapper },
        );

        expect(sources).toHaveLength(1);
        expect(sources[0].url).toContain('/api/events/stream');
        expect(sources[0].url).not.toContain('eventName=');
        expect(sources[0].url).not.toContain('entityId=');
    });

    it('never puts an auth token in the stream URL', () => {
        renderHook(() => useEventStream(() => {}), { wrapper: ActiveSessionWrapper });

        expect(sources[0].url).not.toContain('access_token');
        expect(sources[0].url).not.toContain('token=');
    });

    it('does not open a stream for an inactive session', () => {
        renderHook(() => useGlobalEventStreamConnection(false), {
            wrapper: ({ children }) => createElement(QueryClientProvider, { client: testQueryClient }, children),
        });

        expect(sources).toHaveLength(0);
    });

    it('dispatches only matching event names to each local listener', () => {
        const downloadHandler = vi.fn();
        const metadataHandler = vi.fn();
        renderHook(
            () => {
                useEventStream(downloadHandler, {
                    eventName: ['download.completed', 'download.failed', 'download.imported'],
                });
                useEventStream(metadataHandler, { eventName: 'metadata.refreshed' });
            },
            { wrapper: ActiveSessionWrapper },
        );

        sources[0].emit(JSON.stringify(payload('download.failed')));

        expect(downloadHandler).toHaveBeenCalledWith(payload('download.failed'));
        expect(metadataHandler).not.toHaveBeenCalled();
    });

    it('filters entity ids locally', () => {
        const handler = vi.fn();
        renderHook(() => useEventStream(handler, { eventName: 'download.imported', entityId: 'dl-2' }), {
            wrapper: ActiveSessionWrapper,
        });

        sources[0].emit(JSON.stringify(payload('download.imported', { 'download.id': 'dl-1' })));
        sources[0].emit(JSON.stringify(payload('download.imported', { 'download.id': 'dl-2' })));

        expect(handler).toHaveBeenCalledTimes(1);
        expect(handler).toHaveBeenCalledWith(payload('download.imported', { 'download.id': 'dl-2' }));
    });

    it('matches any entity in a detail query scope', () => {
        const handler = vi.fn();
        renderHook(
            () =>
                useEventStream(handler, {
                    eventName: 'content.hierarchyItem.updated',
                    entityIds: ['content-1', 'content-2'],
                }),
            { wrapper: ActiveSessionWrapper },
        );

        sources[0].emit(
            JSON.stringify(payload('content.hierarchyItem.updated', { 'content.hierarchyItemId': 'content-3' })),
        );
        sources[0].emit(
            JSON.stringify(payload('content.hierarchyItem.updated', { 'content.hierarchyItemId': 'content-2' })),
        );

        expect(handler).toHaveBeenCalledTimes(1);
        expect(handler).toHaveBeenCalledWith(
            payload('content.hierarchyItem.updated', { 'content.hierarchyItemId': 'content-2' }),
        );
    });

    it('matches entity ids from configuration event context', () => {
        const handler = vi.fn();
        renderHook(() => useEventStream(handler, { eventName: 'notificationRule.updated', entityId: 'rule-2' }), {
            wrapper: ActiveSessionWrapper,
        });

        sources[0].emit(JSON.stringify(payload('notificationRule.updated', { 'notificationRule.id': 'rule-1' })));
        sources[0].emit(JSON.stringify(payload('notificationRule.updated', { 'notificationRule.id': 'rule-2' })));

        expect(handler).toHaveBeenCalledTimes(1);
        expect(handler).toHaveBeenCalledWith(payload('notificationRule.updated', { 'notificationRule.id': 'rule-2' }));
    });

    it('does not treat related entity ids as the event identity', () => {
        const handler = vi.fn();
        renderHook(() => useEventStream(handler, { eventName: 'download.updated', entityId: 'content-2' }), {
            wrapper: ActiveSessionWrapper,
        });

        sources[0].emit(
            JSON.stringify(payload('download.updated', { 'content.hierarchyItemIds': 'content-1,content-2' })),
        );

        expect(handler).not.toHaveBeenCalled();
    });

    it('matches an exact non-id context value', () => {
        const handler = vi.fn();
        renderHook(
            () =>
                useEventStream(handler, {
                    eventName: 'settingsGroup.updated',
                    context: { 'settingsGroup.name': 'search' },
                }),
            { wrapper: ActiveSessionWrapper },
        );

        sources[0].emit(JSON.stringify(payload('settingsGroup.updated', { 'settingsGroup.name': 'database' })));
        sources[0].emit(JSON.stringify(payload('settingsGroup.updated', { 'settingsGroup.name': 'search' })));

        expect(handler).toHaveBeenCalledTimes(1);
        expect(handler).toHaveBeenCalledWith(payload('settingsGroup.updated', { 'settingsGroup.name': 'search' }));
    });

    it('continues dispatching when one local listener fails', () => {
        const nextHandler = vi.fn();
        vi.spyOn(console, 'error').mockImplementation(() => {});
        renderHook(
            () => {
                useEventStream(() => {
                    throw new Error('listener failed');
                });
                useEventStream(nextHandler);
            },
            { wrapper: ActiveSessionWrapper },
        );

        sources[0].emit(JSON.stringify(payload()));

        expect(nextHandler).toHaveBeenCalledWith(payload());
    });

    it('unregisters a local listener without closing the shared source', () => {
        const handler = vi.fn();
        const { rerender } = render(createElement(SessionWrapper, { showListener: true, handler }));
        const activeSource = sources[0];

        rerender(createElement(SessionWrapper, { showListener: false, handler }));
        activeSource.emit(JSON.stringify(payload()));

        expect(activeSource.closed).toBe(false);
        expect(handler).not.toHaveBeenCalled();
    });

    it('dispatches to one listener across repeated strict mode navigation', () => {
        for (let index = 0; index < 10; index += 1) {
            const handler = vi.fn();
            const view = render(
                createElement(
                    StrictMode,
                    null,
                    createElement(SessionWrapper, {
                        handler,
                        showListener: true,
                    }),
                ),
            );
            const activeSource = sources.at(-1)!;

            activeSource.emit(JSON.stringify(payload()));
            view.unmount();
            activeSource.emit(JSON.stringify(payload()));

            expect(handler).toHaveBeenCalledTimes(1);
        }
    });

    it('replaces a subscription when its scope changes', () => {
        const handler = vi.fn();
        const { rerender } = renderHook(({ eventName }) => useEventStream(handler, { eventName }), {
            initialProps: { eventName: 'subject.updated' },
            wrapper: ActiveSessionWrapper,
        });

        rerender({ eventName: 'download.updated' });
        sources[0].emit(JSON.stringify(payload('subject.updated')));
        sources[0].emit(JSON.stringify(payload('download.updated')));

        expect(handler).toHaveBeenCalledTimes(1);
        expect(handler).toHaveBeenCalledWith(payload('download.updated'));
    });

    it('closes the source when the application session ends', () => {
        const { rerender } = renderHook(({ active }) => useGlobalEventStreamConnection(active), {
            initialProps: { active: true },
            wrapper: ({ children }) => createElement(QueryClientProvider, { client: testQueryClient }, children),
        });
        const activeSource = sources[0];

        rerender({ active: false });

        expect(activeSource.closed).toBe(true);
    });

    it('signals a reconnect without invalidating unrelated active queries', () => {
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const invalidateSpy = vi.spyOn(testQueryClient, 'invalidateQueries');
        const handler = vi.fn();
        renderHook(() => useEventStream(handler, { eventName: 'stream.reconnected' }), {
            wrapper: ActiveSessionWrapper,
        });

        sources[0].onopen?.();
        sources[0].onerror?.();
        sources[0].onopen?.();

        expect(handler).toHaveBeenCalledWith({ name: 'stream.reconnected', context: {} });
        expect(invalidateSpy).not.toHaveBeenCalled();
        expect(warnSpy).not.toHaveBeenCalled();
    });

    it('delivers reconnect reconciliation through entity-scoped subscriptions', () => {
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const handler = vi.fn();
        renderHook(() => useEventStream(handler, { eventName: 'stream.reconnected', entityId: 'content-1' }), {
            wrapper: ActiveSessionWrapper,
        });

        sources[0].onopen?.();
        sources[0].onerror?.();
        sources[0].onopen?.();

        expect(handler).toHaveBeenCalledWith({ name: 'stream.reconnected', context: {} });
        expect(warnSpy).not.toHaveBeenCalled();
    });

    it('swallows malformed payloads without notifying listeners', () => {
        const handler = vi.fn();
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
        renderHook(() => useEventStream(handler), { wrapper: ActiveSessionWrapper });

        expect(() => sources[0].emit('not-json')).not.toThrow();
        expect(handler).not.toHaveBeenCalled();
        expect(warnSpy).not.toHaveBeenCalled();
    });
});
