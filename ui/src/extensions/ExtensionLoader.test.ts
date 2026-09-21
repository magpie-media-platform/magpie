import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExtensionLoader, isValidCustomElementName } from './ExtensionLoader';
import type { ExtensionAssetContext, UIManifest } from './types';

let injectedElements: HTMLElement[] = [];
let originalAppendChild: typeof document.head.appendChild;

const mockFetch = vi.fn();
const mockCreateObjectURL = vi.fn();
const mockRevokeObjectURL = vi.fn();

interface ContextualScriptElement extends HTMLScriptElement {
    magpieExtensionContext?: ExtensionAssetContext;
}

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

function mockTextResponse(
    body: string,
    contentType: string,
    status = 200,
): {
    ok: boolean;
    status: number;
    headers: Headers;
    text: () => Promise<string>;
} {
    return {
        ok: status >= 200 && status < 300,
        status,
        headers: new Headers({ 'content-type': contentType }),
        text: () => Promise.resolve(body),
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
        localStorage.clear();
        Reflect.deleteProperty(window, 'MagpieMetadataSettings');

        vi.stubGlobal('fetch', mockFetch);
        URL.createObjectURL = mockCreateObjectURL;
        URL.revokeObjectURL = mockRevokeObjectURL;

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
        Reflect.deleteProperty(URL, 'createObjectURL');
        Reflect.deleteProperty(URL, 'revokeObjectURL');
        Reflect.deleteProperty(window, 'MagpieMetadataSettings');
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

            new ExtensionLoader();
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

            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse('window.__magpieAssetLoaded = 1;', 'text/javascript'));

            localStorage.setItem('magpie_auth_token', 'test-token');

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            expect(mockFetch).toHaveBeenNthCalledWith(
                2,
                '/api/ui/assets/ext-1/test.js',
                expect.objectContaining({
                    credentials: 'include',
                    headers: expect.any(Headers),
                    method: 'GET',
                }),
            );

            const assetRequestHeaders = mockFetch.mock.calls[1][1]?.headers as Headers;
            expect(assetRequestHeaders.get('Authorization')).toBe('Bearer test-token');

            const script = injectedElements.find((el) => el.tagName === 'SCRIPT') as HTMLScriptElement | undefined;
            expect(script).toBeDefined();
            expect(script?.src).toBe('');
            expect(script?.textContent).toContain('__magpieAssetLoaded');
        });

        it.each([
            ['runtime', 'throw new Error("Protected runtime failed");', 'Protected runtime failed'],
            ['syntax', 'const broken = ;', null],
        ])('recovers after a protected inline %s error', async (_kind, failingSource, expectedMessage) => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'failing-asset',
                        extensionId: 'ext-1',
                        name: 'failing-script',
                        kind: 'Script',
                        url: null,
                        relativePath: 'failing.js',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };
            const correctedSource = 'document.currentScript.magpieExtensionContext.waitUntil(Promise.resolve());';
            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse(failingSource, 'text/javascript'))
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse(correctedSource, 'text/javascript'));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            const failure = await loader.init().then(
                () => null,
                (error: unknown) => error,
            );

            expect(failure).toBeInstanceOf(Error);
            if (expectedMessage) {
                expect(failure).toHaveProperty('message', expect.stringContaining(expectedMessage));
            }
            expect(loader.state.state).toBe('error');
            const failedScript = injectedElements.find(
                (element): element is ContextualScriptElement => element instanceof HTMLScriptElement,
            );
            expect(failedScript).toBeDefined();
            expect(failedScript?.isConnected).toBe(false);
            expect(failedScript?.magpieExtensionContext).toBeUndefined();

            const executionWindow = document.defaultView ?? window;
            const laterErrorObserver = vi.fn((event: ErrorEvent) => event.stopImmediatePropagation());
            executionWindow.addEventListener('error', laterErrorObserver, { capture: true, once: true });
            const laterError = new ErrorEvent('error', {
                cancelable: true,
                message: 'Later unrelated error',
            });
            expect(executionWindow.dispatchEvent(laterError)).toBe(true);
            expect(laterError.defaultPrevented).toBe(false);
            expect(laterErrorObserver).toHaveBeenCalledOnce();

            loader.reset();
            await loader.init();

            expect(loader.state.state).toBe('ready');
            const scripts = injectedElements.filter(
                (element): element is ContextualScriptElement => element instanceof HTMLScriptElement,
            );
            expect(scripts).toHaveLength(2);
            expect(scripts[1]?.isConnected).toBe(true);
            expect(scripts[1]?.magpieExtensionContext).toBeUndefined();
        });

        it.each([
            '',
            '.',
            './asset.js',
            '../asset.js',
            'nested/../asset.js',
            '%2e%2e/asset.js',
            '%252e%252e/asset.js',
            'nested%2fasset.js',
            'nested%252fasset.js',
            'nested\\asset.js',
            'nested%5casset.js',
            '/absolute.js',
            'https://evil.test/asset.js',
            'asset.js?download=1',
            'asset.js#fragment',
            'nested//asset.js',
            'asset%zz.js',
        ])('rejects unsafe protected asset path %j before fetching it', async (relativePath) => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'unsafe-asset',
                        extensionId: 'ext-1',
                        name: 'unsafe-script',
                        kind: 'Script',
                        url: null,
                        relativePath,
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };
            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });

            await expect(loader.init()).rejects.toThrow('Invalid protected extension asset path.');
            expect(mockFetch).toHaveBeenCalledTimes(1);
        });

        it('rejects unsafe secondary paths without fetching them', async () => {
            const extensionId = 'ext-1';
            const unsafePaths = [
                '../asset.html',
                '%2e%2e/asset.html',
                'nested\\asset.html',
                '/absolute.html',
                'https://evil.test/asset.html',
                'asset.html?download=1',
                'asset.html#fragment',
            ];
            const source = `
                (() => {
                    const context = document.currentScript.magpieExtensionContext;
                    const paths = ${JSON.stringify(unsafePaths)};
                    context.waitUntil(Promise.all(paths.map(
                        (path) => context.loadAssetText(path).then(
                            () => Promise.reject(new Error("Unsafe path was fetched")),
                            () => undefined,
                        ),
                    )));
                })();
            `;
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'runtime-asset',
                        extensionId,
                        name: 'runtime',
                        kind: 'Script',
                        url: null,
                        relativePath: 'runtime.js',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };
            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse(source, 'text/javascript'));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            expect(mockFetch).toHaveBeenCalledTimes(2);
            expect(mockFetch).toHaveBeenLastCalledWith(`/api/ui/assets/${extensionId}/runtime.js`, expect.any(Object));
        });

        it('encodes valid nested protected asset segments within the bound extension', async () => {
            const extensionId = 'ext-1';
            const source = `
                (() => {
                    const context = document.currentScript.magpieExtensionContext;
                    context.waitUntil(context.loadAssetText("templates/provider template.html"));
                })();
            `;
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'runtime-asset',
                        extensionId,
                        name: 'runtime',
                        kind: 'Script',
                        url: null,
                        relativePath: 'nested/provider script.js',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };
            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse(source, 'text/javascript'))
                .mockResolvedValueOnce(mockTextResponse('<template></template>', 'text/html'));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            expect(mockFetch.mock.calls.map(([url]) => url)).toEqual([
                '/api/ui/manifest',
                `/api/ui/assets/${extensionId}/nested/provider%20script.js`,
                `/api/ui/assets/${extensionId}/templates/provider%20template.html`,
            ]);
        });

        it('injects script assets when API serializes kind as a numeric enum', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'test-script',
                        kind: 0,
                        url: null,
                        relativePath: 'test.js',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse('window.__magpieNumericAssetLoaded = 1;', 'text/javascript'));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            expect(mockFetch).toHaveBeenNthCalledWith(
                2,
                '/api/ui/assets/ext-1/test.js',
                expect.objectContaining({
                    credentials: 'include',
                    method: 'GET',
                }),
            );

            const script = injectedElements.find((el) => el.tagName === 'SCRIPT') as HTMLScriptElement | undefined;
            expect(script).toBeDefined();
            expect(script?.textContent).toContain('__magpieNumericAssetLoaded');
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

            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse('.magpie-test { color: red; }', 'text/css'));

            localStorage.setItem('magpie_auth_token', 'test-token');

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            expect(mockFetch).toHaveBeenNthCalledWith(
                2,
                '/api/ui/assets/ext-1/test.css',
                expect.objectContaining({
                    credentials: 'include',
                    headers: expect.any(Headers),
                    method: 'GET',
                }),
            );

            const assetRequestHeaders = mockFetch.mock.calls[1][1]?.headers as Headers;
            expect(assetRequestHeaders.get('Authorization')).toBe('Bearer test-token');

            const style = injectedElements.find((el) => el.tagName === 'STYLE') as HTMLStyleElement | undefined;
            expect(style).toBeDefined();
            expect(style?.textContent).toContain('.magpie-test');
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

        it('injects relative link assets through an authenticated blob URL', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'extension-icon',
                        kind: 'Link',
                        url: null,
                        relativePath: 'icon.svg',
                        linkRel: 'icon',
                        integrity: null,
                        priority: 50,
                    },
                ],
                extensions: [],
            };

            mockCreateObjectURL.mockReturnValue('blob:magpie-icon');
            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));
            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 200,
                headers: new Headers({ 'content-type': 'image/svg+xml' }),
                blob: () => Promise.resolve(new Blob(['<svg />'], { type: 'image/svg+xml' })),
            });

            localStorage.setItem('magpie_auth_token', 'test-token');

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            expect(mockFetch).toHaveBeenNthCalledWith(
                2,
                '/api/ui/assets/ext-1/icon.svg',
                expect.objectContaining({
                    credentials: 'include',
                    headers: expect.any(Headers),
                    method: 'GET',
                }),
            );

            const assetRequestHeaders = mockFetch.mock.calls[1][1]?.headers as Headers;
            expect(assetRequestHeaders.get('Authorization')).toBe('Bearer test-token');

            const link = injectedElements.find((el) => el.tagName === 'LINK') as HTMLLinkElement | undefined;
            expect(link?.rel).toBe('icon');
            expect(link?.href).toBe('blob:magpie-icon');

            loader.reset();

            expect(mockRevokeObjectURL).toHaveBeenCalledWith('blob:magpie-icon');
            expect(document.head.contains(link!)).toBe(false);
        });

        it('warns and skips link assets without a rel attribute', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'relless-link',
                        kind: 'Link',
                        url: 'https://example.com/font',
                        relativePath: null,
                        linkRel: null,
                        integrity: null,
                        priority: 50,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
            const loader = new ExtensionLoader();
            await loader.init();

            expect(loader.state.state).toBe('ready');
            expect(injectedElements.filter((el) => el.tagName === 'LINK')).toEqual([]);
            expect(warnSpy).toHaveBeenCalledWith('[ExtensionLoader] Link asset "relless-link" has no rel attribute.');

            warnSpy.mockRestore();
        });

        it('injects style and link assets when API serializes kind as a numeric enum', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'numeric-link',
                        kind: 2,
                        url: 'https://example.com/font',
                        relativePath: null,
                        linkRel: 'preconnect',
                        integrity: null,
                        priority: 50,
                    },
                    {
                        id: 'asset-2',
                        extensionId: 'ext-1',
                        name: 'numeric-style',
                        kind: 1,
                        url: null,
                        relativePath: 'test.css',
                        linkRel: null,
                        integrity: null,
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse('.magpie-numeric { color: blue; }', 'text/css'));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            expect(mockFetch).toHaveBeenNthCalledWith(
                2,
                '/api/ui/assets/ext-1/test.css',
                expect.objectContaining({
                    credentials: 'include',
                    method: 'GET',
                }),
            );

            const style = injectedElements.find((el) => el.tagName === 'STYLE') as HTMLStyleElement | undefined;
            expect(style?.textContent).toContain('.magpie-numeric');

            const link = injectedElements.find((el) => el.tagName === 'LINK') as HTMLLinkElement | undefined;
            expect(link?.rel).toBe('preconnect');
            expect(link?.href).toBe('https://example.com/font');
        });

        it('uses remote URL for style assets when provided', async () => {
            const manifest: UIManifest = {
                assets: [
                    {
                        id: 'asset-1',
                        extensionId: 'ext-1',
                        name: 'cdn-style',
                        kind: 'Style',
                        url: 'https://cdn.example.com/theme.css',
                        relativePath: null,
                        linkRel: null,
                        integrity: 'sha384-def456',
                        priority: 100,
                    },
                ],
                extensions: [],
            };

            mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

            const loader = new ExtensionLoader();
            const initPromise = loader.init();

            await vi.waitFor(() => {
                const link = injectedElements.find((el) => el.tagName === 'LINK') as HTMLLinkElement | undefined;
                expect(link).toBeDefined();
                expect(link?.rel).toBe('stylesheet');
                expect(link?.href).toBe('https://cdn.example.com/theme.css');
                expect(link?.integrity).toBe('sha384-def456');
                expect(link?.crossOrigin).toBe('anonymous');
            });

            const link = injectedElements.find((el) => el.tagName === 'LINK');
            link?.dispatchEvent(new Event('load'));

            await initPromise;
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

            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse('window.__magpieAssetLoaded = 1;', 'text/javascript'));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            const script = injectedElements.find((el) => el.tagName === 'SCRIPT')!;

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

            mockFetch
                .mockResolvedValueOnce(mockJsonResponse(manifest))
                .mockResolvedValueOnce(mockTextResponse('.magpie-test { color: red; }', 'text/css'));

            const loader = new ExtensionLoader({ apiBaseUrl: '/api' });
            await loader.init();

            const link = injectedElements.find((el) => el.tagName === 'STYLE')!;

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
});
