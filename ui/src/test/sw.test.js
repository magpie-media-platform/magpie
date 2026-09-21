import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';

describe('sw.js', () => {
    let installHandler;
    let fetchHandler;

    beforeAll(async () => {
        const addEventListenerSpy = vi.spyOn(self, 'addEventListener');
        await import('../../public/sw.js');
        installHandler = addEventListenerSpy.mock.calls.find(([type]) => type === 'install')?.[1];
        fetchHandler = addEventListenerSpy.mock.calls.find(([type]) => type === 'fetch')?.[1];
        addEventListenerSpy.mockRestore();
    });

    afterEach(() => vi.unstubAllGlobals());

    it('caches the self-contained offline page during installation', async () => {
        const add = vi.fn().mockResolvedValue(undefined);
        vi.stubGlobal('caches', { open: vi.fn().mockResolvedValue({ add }) });
        vi.stubGlobal('skipWaiting', vi.fn());
        let installPromise;

        installHandler({
            waitUntil: (promise) => {
                installPromise = promise;
            },
        });
        await installPromise;

        expect(add).toHaveBeenCalledWith('/offline.html');
    });

    it('returns the network response for an online navigation', async () => {
        const response = { ok: true };
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response));
        const match = vi.fn().mockResolvedValue(undefined);
        vi.stubGlobal('caches', { match });
        let responded;

        fetchHandler({
            request: { method: 'GET', mode: 'navigate', url: `${self.location.origin}/library` },
            respondWith: (promise) => {
                responded = promise;
            },
        });

        await expect(responded).resolves.toBe(response);
        expect(match).not.toHaveBeenCalled();
    });

    it('serves the self-contained offline page when navigation fails', async () => {
        vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')));
        const match = vi.fn().mockResolvedValue('offline response');
        vi.stubGlobal('caches', { match });
        let responded;

        fetchHandler({
            request: { method: 'GET', mode: 'navigate', url: `${self.location.origin}/library` },
            respondWith: (promise) => {
                responded = promise;
            },
        });

        await expect(responded).resolves.toBe('offline response');
        expect(match).toHaveBeenCalledWith('/offline.html');
    });
});
