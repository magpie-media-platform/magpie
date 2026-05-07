import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExtensionLoader, isValidCustomElementName } from './ExtensionLoader';
import type { UIManifest } from './types';

let injectedElements: HTMLElement[] = [];
let originalAppendChild: typeof document.head.appendChild;

const mockFetch = vi.fn();

function mockJsonResponse(
    body: unknown,
    status = 200,
): {
    ok: boolean;
    status: number;
    headers: Headers;
    json: () => Promise<unknown>;
} {
    return {
        ok: status >= 200 && status < 300,
        status,
        headers: new Headers({ 'content-type': 'application/json' }),
        json: () => Promise.resolve(body),
    };
}

function mockErrorResponse(
    status: number,
    statusText: string,
): {
    ok: false;
    status: number;
    statusText: string;
    headers: Headers;
    text: () => Promise<string>;
} {
    return {
        ok: false,
        status,
        statusText,
        headers: new Headers({ 'content-type': 'text/plain' }),
        text: () => Promise.resolve(statusText),
    };
}

describe('isValidCustomElementName', () => {
    it('returns true for names with a hyphen', () => {
        expect(isValidCustomElementName('my-widget')).toBe(true);
        expect(isValidCustomElementName('a-b')).toBe(true);
        expect(isValidCustomElementName('foo-bar-baz')).toBe(true);
    });

    it('returns false for names without a hyphen', () => {
        expect(isValidCustomElementName('widget')).toBe(false);
        expect(isValidCustomElementName('mywidget')).toBe(false);
        expect(isValidCustomElementName('')).toBe(false);
    });
});

describe('ExtensionLoader', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        injectedElements = [];

        vi.stubGlobal('fetch', mockFetch);

        originalAppendChild = document.head.appendChild.bind(document.head);
        document.head.appendChild = <T extends Node>(node: T): T => {
            if (node instanceof HTMLElement) {
                injectedElements.push(node);
            }
            return originalAppendChild(node);
        };
    });

    afterEach(() => {
        for (const el of injectedElements) {
            el.remove();
        }
        injectedElements = [];
        document.head.appendChild = originalAppendChild;
        vi.unstubAllGlobals();
    });

    describe('constructor', () => {
        it('initializes with idle state', () => {
            const loader = new ExtensionLoader();
            expect(loader.state).toEqual({
                state: 'idle',
                error: null,
                manifest: null,
            });
        });

        it('accepts custom apiBaseUrl', () => {
            const loader = new ExtensionLoader({ apiBaseUrl: '/custom-api' });
            expect(loader.state.state).toBe('idle');
        });
    });

    describe('subscribe', () => {
        it('immediately notifies with current state', () => {
            const loader = new ExtensionLoader();
            const callback = vi.fn();

            loader.subscribe(callback);

            expect(callback).toHaveBeenCalledWith({
                state: 'idle',
                error: null,
                manifest: null,
            });
        });

        it('returns unsubscribe function', () => {
            const loader = new ExtensionLoader();
            const callback = vi.fn();

            const unsubscribe = loader.subscribe(callback);
            expect(typeof unsubscribe).toBe('function');

            callback.mockClear();
            unsubscribe();

            mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));
        });
    });

    describe('init', () => {
        it('fetches manifest and transitions to ready', async () => {
            const manifest: UIManifest = {
                assets: [],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            const states: string[] = [];
            loader.subscribe((s) => states.push(s.state));

            await loader.init();

            expect(states).toEqual(['idle', 'loading', 'ready']);
            expect(loader.state.manifest).toEqual(manifest);
        });

        it('transitions to error on fetch failure', async () => {
            mockFetch.mockResolvedValueOnce(mockErrorResponse(500, 'Internal Server Error'));

            const loader = new ExtensionLoader();
            const states: string[] = [];
            loader.subscribe((s) => states.push(s.state));

            await expect(loader.init()).rejects.toThrow();

            expect(states).toEqual(['idle', 'loading', 'error']);
            expect(loader.state.error?.message).toBeTruthy();
        });

        it('transitions to error on network error', async () => {
            mockFetch.mockRejectedValueOnce(new Error('Network error'));

            const loader = new ExtensionLoader();

            await expect(loader.init()).rejects.toThrow('Network error');

            expect(loader.state.state).toBe('error');
            expect(loader.state.error?.message).toBe('Network error');
        });

        it('returns same promise when called multiple times', async () => {
            mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));

            const loader = new ExtensionLoader();

            const promise1 = loader.init();
            const promise2 = loader.init();

            expect(promise1).toBe(promise2);
            await promise1;
            expect(mockFetch).toHaveBeenCalledTimes(1);
        });

        it('does nothing if already ready', async () => {
            mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));

            const loader = new ExtensionLoader();
            await loader.init();

            mockFetch.mockClear();

            const _loader2 = new ExtensionLoader();
            mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));

            expect(loader.state.state).toBe('ready');
        });

        it('injects script assets', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'test-script',
                        kind: 'Script',
                        url: null,
                        relativePath: 'test.js',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });

            const initPromise = loader.init();

            await vi.waitFor(() => {
                const script = injectedElements.find((el) => el.tagName === 'SCRIPT') as HTMLScriptElement | undefined;
                expect(script).toBeDefined();
                expect(script?.src).toContain('/api/ui/assets/ext-1/test.js');
            });

            const script = injectedElements.find((el) => el.tagName === 'SCRIPT');
            script?.dispatchEvent(new Event('load'));

            await initPromise;
        });

        it('injects style assets', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'test-style',
                        kind: 'Style',
                        url: null,
                        relativePath: 'test.css',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            const initPromise = loader.init();

            await vi.waitFor(() => {
                const link = injectedElements.find(
                    (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'stylesheet',
                ) as HTMLLinkElement | undefined;
                expect(link).toBeDefined();
                expect(link?.href).toContain('/api/ui/assets/ext-1/test.css');
            });

            const link = injectedElements.find(
                (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'stylesheet',
            );
            link?.dispatchEvent(new Event('load'));

            await initPromise;
        });

        it('injects link assets', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'preconnect',
                        kind: 'Link',
                        url: 'https://fonts.googleapis.com',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 50,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            const link = injectedElements.find(
                (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'preconnect',
            ) as HTMLLinkElement | undefined;

            expect(link).toBeDefined();
            expect(link?.href).toBe('https://fonts.googleapis.com/');
        });

        it('uses remote URL when provided', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'cdn-script',
                        kind: 'Script',
                        url: 'https://cdn.example.com/widget.js',
                        relativePath: null,
                        linkRel: null,
                        integrity: 'sha384-abc123',
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            const initPromise = loader.init();

            await vi.waitFor(() => {
                const script = injectedElements.find((el) => el.tagName === 'SCRIPT') as HTMLScriptElement | undefined;
                expect(script).toBeDefined();
                expect(script?.src).toBe('https://cdn.example.com/widget.js');
                expect(script?.integrity).toBe('sha384-abc123');
                expect(script?.crossOrigin).toBe('anonymous');
            });

            const script = injectedElements.find((el) => el.tagName === 'SCRIPT');
            script?.dispatchEvent(new Event('load'));

            await initPromise;
        });

        it('skips duplicate asset URLs', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'preconnect-a',
                        kind: 'Link',
                        url: 'https://example.com/font',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 50,
                    },
                    {
                        id: 'asset-2',
                        extensionId: 'ext-2',
                        name: 'preconnect-b',
                        kind: 'Link',
                        url: 'https://example.com/font',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 60,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            const links = injectedElements.filter(
                (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'preconnect',
            );
            expect(links.length).toBe(1);
            expect((links[0] as HTMLLinkElement).href).toBe('https://example.com/font');
        });

        it('sorts assets by priority before injecting', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-3',
                        extensionId: 'ext-1',
                        name: 'last',
                        kind: 'Link',
                        url: 'https://example.com/last',
                        relativePath: null,
                        linkRel: 'prefetch',
                        integrity: null,
                        priority: 200,
                    },
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'first',
                        kind: 'Link',
                        url: 'https://example.com/first',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 50,
                    },
                    {
                        id: 'asset-2',
                        extensionId: 'ext-1',
                        name: 'middle',
                        kind: 'Link',
                        url: 'https://example.com/middle',
                        relativePath: null,
                        linkRel: 'dns-prefetch',
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            const links = injectedElements.filter((el) => el.tagName === 'LINK') as HTMLLinkElement[];
            expect(links.length).toBe(3);
            expect(links[0].href).toContain('first');
            expect(links[1].href).toContain('middle');
            expect(links[2].href).toContain('last');
        });
    });

    describe('reset', () => {
        it('clears cached promise so init can be retried', async () => {
            mockFetch.mockResolvedValueOnce(mockErrorResponse(503, 'Service Unavailable'));

            const loader = new ExtensionLoader();
            await expect(loader.init()).rejects.toThrow();
            expect(loader.state.state).toBe('error');

            loader.reset();
            expect(loader.state.state).toBe('idle');
            expect(loader.state.error).toBeNull();

            mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));

            await loader.init();
            expect(loader.state.state).toBe('ready');
            expect(mockFetch).toHaveBeenCalledTimes(2);
        });

        it('transitions back to idle state', () => {
            const loader = new ExtensionLoader();
            const states: string[] = [];
            loader.subscribe((s) => states.push(s.state));

            loader.reset();

            expect(states).toEqual(['idle', 'idle']);
        });

        it('removes injected script elements from head', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'test-script',
                        kind: 'Script',
                        url: null,
                        relativePath: 'test.js',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            const initPromise = loader.init();

            await vi.waitFor(() => {
                const script = injectedElements.find((el) => el.tagName === 'SCRIPT');
                expect(script).toBeDefined();
            });

            const script = injectedElements.find((el) => el.tagName === 'SCRIPT')!;
            script.dispatchEvent(new Event('load'));
            await initPromise;

            expect(document.head.contains(script)).toBe(true);

            loader.reset();

            expect(document.head.contains(script)).toBe(false);
        });

        it('removes injected style elements from head', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'test-style',
                        kind: 'Style',
                        url: null,
                        relativePath: 'test.css',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            const initPromise = loader.init();

            await vi.waitFor(() => {
                const link = injectedElements.find(
                    (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'stylesheet',
                );
                expect(link).toBeDefined();
            });

            const link = injectedElements.find(
                (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'stylesheet',
            )!;
            link.dispatchEvent(new Event('load'));
            await initPromise;

            expect(document.head.contains(link)).toBe(true);

            loader.reset();

            expect(document.head.contains(link)).toBe(false);
        });

        it('removes injected link elements from head', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'preconnect',
                        kind: 'Link',
                        url: 'https://fonts.googleapis.com',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 50,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            const link = injectedElements.find(
                (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'preconnect',
            )!;

            expect(document.head.contains(link)).toBe(true);

            loader.reset();

            expect(document.head.contains(link)).toBe(false);
        });

        it('does not duplicate elements after reset and re-init', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'preconnect',
                        kind: 'Link',
                        url: 'https://fonts.googleapis.com',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 50,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));
            const loader = new ExtensionLoader();
            await loader.init();

            const linksBefore = injectedElements.filter(
                (el) => el.tagName === 'LINK' && (el as HTMLLinkElement).rel === 'preconnect',
            );
            expect(linksBefore.length).toBe(1);

            loader.reset();
            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));
            await loader.init();

            const linksInHead = document.head.querySelectorAll('link[rel="preconnect"]');
            expect(linksInHead.length).toBe(1);
        });

        it('clears manifest on reset', async () => {
            mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));

            const loader = new ExtensionLoader();
            await loader.init();
            expect(loader.state.manifest).not.toBeNull();

            loader.reset();
            expect(loader.state.manifest).toBeNull();
        });
    });

    describe('getExtensionsForSlot', () => {
        it('returns empty array before init', () => {
            const loader = new ExtensionLoader();
            expect(loader.getExtensionsForSlot('dashboard.main')).toEqual([]);
        });

        it('returns extensions for slot sorted by priority', async () => {
            const manifest: UIManifest = {
                assets: [],
                extensions: [
                    {
                        id: 'ext-2',
                        extensionId: 'my-ext',
                        name: 'Widget B',
                        slot: 'dashboard.main',
                        modifier: 'After',
                        tagName: 'widget-b',
                        priority: 200,
                        attributes: null,
                    },
                    {
                        id: 'ext-1',
                        extensionId: 'my-ext',
                        name: 'Widget A',
                        slot: 'dashboard.main',
                        modifier: 'Before',
                        tagName: 'widget-a',
                        priority: 50,
                        attributes: null,
                    },
                    {
                        id: 'ext-3',
                        extensionId: 'other-ext',
                        name: 'Other Widget',
                        slot: 'content.list',
                        modifier: 'After',
                        tagName: 'other-widget',
                        priority: 100,
                        attributes: null,
                    },
                ],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            const extensions = loader.getExtensionsForSlot('dashboard.main');
            expect(extensions.length).toBe(2);
            expect(extensions[0].name).toBe('Widget A');
            expect(extensions[1].name).toBe('Widget B');
        });

        it('returns empty array for unknown slot', async () => {
            const manifest: UIManifest = {
                assets: [],
                extensions: [
                    {
                        id: 'ext-1',
                        extensionId: 'my-ext',
                        name: 'Widget',
                        slot: 'dashboard.main',
                        modifier: 'After',
                        tagName: 'my-widget',
                        priority: 100,
                        attributes: null,
                    },
                ],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            expect(loader.getExtensionsForSlot('unknown.slot')).toEqual([]);
        });
    });

    describe('getExtensions', () => {
        it('returns empty array before init', () => {
            const loader = new ExtensionLoader();
            expect(loader.getExtensions()).toEqual([]);
        });

        it('returns all extensions after init', async () => {
            const manifest: UIManifest = {
                assets: [],
                extensions: [
                    {
                        id: 'ext-1',
                        extensionId: 'my-ext',
                        name: 'Widget 1',
                        slot: 'dashboard.main',
                        modifier: 'After',
                        tagName: 'widget-1',
                        priority: 100,
                        attributes: null,
                    },
                    {
                        id: 'ext-2',
                        extensionId: 'my-ext',
                        name: 'Widget 2',
                        slot: 'content.list',
                        modifier: 'Before',
                        tagName: 'widget-2',
                        priority: 50,
                        attributes: { 'data-feature': 'enabled' },
                    },
                ],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            expect(loader.getExtensions()).toEqual(manifest.extensions);
        });
    });

    describe('getExtensionsForSlot -- tagName validation', () => {
        it('filters out extensions with invalid tagName and logs a warning', async () => {
            const manifest: UIManifest = {
                assets: [],
                extensions: [
                    {
                        id: 'valid',
                        extensionId: 'my-ext',
                        name: 'Valid Widget',
                        slot: 'dashboard.main',
                        modifier: 'After',
                        tagName: 'valid-widget',
                        priority: 100,
                        attributes: null,
                    },
                    {
                        id: 'invalid',
                        extensionId: 'my-ext',
                        name: 'Invalid Widget',
                        slot: 'dashboard.main',
                        modifier: 'After',
                        tagName: 'invalidwidget',
                        priority: 200,
                        attributes: null,
                    },
                ],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
            const loader = new ExtensionLoader();
            await loader.init();

            const extensions = loader.getExtensionsForSlot('dashboard.main');
            expect(extensions).toHaveLength(1);
            expect(extensions[0].id).toBe('valid');
            expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('"invalidwidget"'));

            warnSpy.mockRestore();
        });
    });

    describe('getAssets', () => {
        it('returns empty array before init', () => {
            const loader = new ExtensionLoader();
            expect(loader.getAssets()).toEqual([]);
        });

        it('returns all assets after init', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'script',
                        kind: 'Link',
                        url: 'https://example.com',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            await loader.init();

            expect(loader.getAssets()).toEqual(manifest.assets);
        });
    });
});
