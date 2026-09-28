import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { connectLiveUpdates, getLiveUpdateStatus, retryLiveUpdates } from './liveUpdates';
import { clearAuthStorage, storeAuthTokens } from './tokenStorage';
import { installSharedWorkerHarness } from '@/test/sharedWorkerHarness';

class Source {
    static CLOSED = 2;
    static sources: Source[] = [];
    closed = false;
    readyState = 0;
    onopen: (() => void) | null = null;
    onerror: (() => void) | null = null;
    onmessage: ((event: { data: string }) => void) | null = null;
    constructor(
        readonly url: string,
        readonly options: { withCredentials: boolean },
    ) {
        Source.sources.push(this);
    }
    close() {
        this.closed = true;
    }
}

describe('shared live updates', () => {
    const cleanup: (() => void)[] = [];
    beforeEach(() => {
        vi.useFakeTimers();
        localStorage.clear();
        Source.sources = [];
    });
    afterEach(() => {
        cleanup.splice(0).forEach((close) => close());
        vi.unstubAllGlobals();
        vi.unstubAllEnvs();
        vi.useRealTimers();
        localStorage.clear();
    });

    it('shares one credentialed stream across ten ports and releases the last page', () => {
        installSharedWorkerHarness(Source);
        const handlers = Array.from({ length: 10 }, () => vi.fn());
        handlers.forEach((handler) => cleanup.push(connectLiveUpdates('', handler)));
        expect(Source.sources).toHaveLength(1);
        expect(Source.sources[0].options).toEqual({ withCredentials: true });
        Source.sources[0].onmessage?.({
            data: JSON.stringify({ name: 'content.updated', context: { entityId: 'one' } }),
        });
        handlers.forEach((handler) => expect(handler).toHaveBeenCalledOnce());
        cleanup.shift()!();
        expect(Source.sources[0].closed).toBe(false);
        cleanup.splice(0).forEach((close) => close());
        expect(Source.sources[0].closed).toBe(true);
    });

    it('has no per-page fallback when unsupported and reports startup failures separately', () => {
        vi.stubGlobal('SharedWorker', undefined);
        cleanup.push(connectLiveUpdates('', vi.fn()));
        expect(getLiveUpdateStatus().state).toBe('unsupported');
        vi.stubGlobal(
            'SharedWorker',
            class {
                constructor() {
                    throw new Error('Worker blocked');
                }
            },
        );
        localStorage.setItem('magpie_auth_token', 'fixture');
        cleanup.push(connectLiveUpdates('', vi.fn()));
        expect(getLiveUpdateStatus()).toEqual({ state: 'failed', error: 'Worker blocked' });
        expect(Source.sources).toHaveLength(0);
    });

    it('drops queued events from a retired session and renews without a duplicate stream', () => {
        const harness = installSharedWorkerHarness(Source);
        const handler = vi.fn();
        cleanup.push(connectLiveUpdates('', handler));
        const oldSource = Source.sources[0];
        oldSource.onopen?.();
        storeAuthTokens('renewed', new Date(Date.now() + 600000).toISOString());
        expect(Source.sources).toHaveLength(1);
        expect(handler).not.toHaveBeenCalled();
        const deliver = harness.workers[0].port.onmessage;
        let queued: { data: unknown } | undefined;
        harness.workers[0].port.onmessage = (event) => {
            queued = event;
        };
        oldSource.onmessage?.({ data: JSON.stringify({ name: 'secret', context: {} }) });
        harness.workers[0].port.onmessage = deliver;
        clearAuthStorage();
        expect(oldSource.closed).toBe(true);
        expect(queued).toBeDefined();
        deliver?.(queued!);
        oldSource.onmessage?.({ data: JSON.stringify({ name: 'secret', context: {} }) });
        expect(handler).not.toHaveBeenCalled();
        storeAuthTokens('next-session', new Date(Date.now() + 600000).toISOString());
        expect(Source.sources).toHaveLength(2);
        expect(Source.sources.filter((source) => !source.closed)).toHaveLength(1);
    });

    it('keeps temporary failures yellow and reconciles once after the connection gap', () => {
        installSharedWorkerHarness(Source);
        const handler = vi.fn();
        cleanup.push(connectLiveUpdates('', handler));
        Source.sources[0].onopen?.();
        Source.sources[0].onerror?.();
        expect(getLiveUpdateStatus().state).toBe('reconnecting');
        Source.sources[0].onopen?.();
        expect(getLiveUpdateStatus().state).toBe('connected');
        expect(handler).toHaveBeenCalledExactlyOnceWith({ name: 'stream.reconnected', context: {} });
    });

    it('retries a terminal stream failure through the same worker', () => {
        installSharedWorkerHarness(Source);
        cleanup.push(connectLiveUpdates('', vi.fn()));
        Source.sources[0].readyState = Source.CLOSED;
        Source.sources[0].onerror?.();
        expect(getLiveUpdateStatus()).toEqual({ state: 'reconnecting' });
        vi.advanceTimersByTime(1000);
        Source.sources[1].readyState = Source.CLOSED;
        Source.sources[1].onerror?.();
        vi.advanceTimersByTime(2000);
        Source.sources[2].readyState = Source.CLOSED;
        Source.sources[2].onerror?.();
        vi.advanceTimersByTime(4000);
        Source.sources[3].readyState = Source.CLOSED;
        Source.sources[3].onerror?.();
        expect(getLiveUpdateStatus()).toEqual({ state: 'failed' });
        retryLiveUpdates();
        expect(Source.sources).toHaveLength(5);
        expect(Source.sources.filter((source) => !source.closed)).toHaveLength(1);
    });

    it('recovers a worker error without keeping its previous connection', () => {
        const harness = installSharedWorkerHarness(Source);
        const handler = vi.fn();
        cleanup.push(connectLiveUpdates('', handler));
        Source.sources[0].onopen?.();
        harness.workers[0].onerror?.({});
        expect(getLiveUpdateStatus().state).toBe('reconnecting');
        expect(Source.sources[0].closed).toBe(true);
        vi.advanceTimersByTime(1000);
        Source.sources[1].onopen?.();
        expect(getLiveUpdateStatus().state).toBe('connected');
        expect(Source.sources.filter((source) => !source.closed)).toHaveLength(1);
        expect(handler).toHaveBeenCalledWith({ name: 'stream.reconnected', context: {} });
    });

    it('closes expired authentication and resumes the same session after renewal', () => {
        const harness = installSharedWorkerHarness(Source);
        cleanup.push(connectLiveUpdates('', vi.fn()));
        Source.sources[0].onopen?.();
        vi.setSystemTime(Date.now() + 700000);
        harness.tick();
        expect(Source.sources[0].closed).toBe(true);
        storeAuthTokens('renewed', new Date(Date.now() + 600000).toISOString());
        expect(Source.sources.filter((source) => !source.closed)).toHaveLength(1);
    });

    it('isolates API instances on the same origin', () => {
        installSharedWorkerHarness(Source);
        cleanup.push(connectLiveUpdates('', vi.fn()));
        vi.stubEnv('VITE_API_URL', '/other/api');
        localStorage.setItem('magpie_auth_token:http://localhost:3000/other/api', 'fixture');
        localStorage.setItem(
            'magpie_token_expires:http://localhost:3000/other/api',
            new Date(Date.now() + 600000).toISOString(),
        );
        cleanup.push(connectLiveUpdates('', vi.fn()));
        expect(Source.sources).toHaveLength(2);
        expect(Source.sources[1].url).toContain('/other/api/events/stream');
    });
});
