import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExtensionLoader } from './ExtensionLoader';
import type { UIManifest } from './types';

const firstExtensionId = '7d9f1fba-9d4e-48f3-a5fe-a288fa144175';
const secondExtensionId = '3fbe3aab-c941-48ed-91a9-f0a542f1c5a2';

const runtimeScript = `(() => {
    if (window.MagpieMetadataSettings) {
        return;
    }
    const context = document.currentScript?.magpieExtensionContext;
    context.waitUntil(
        context.loadAssetText('metadata-settings.html').then(() => {
            window.MagpieMetadataSettings = {
                register: (tag) => {
                    if (!customElements.get(tag)) {
                        customElements.define(tag, class extends HTMLElement {});
                    }
                },
            };
        }),
    );
})();`;

const providers = {
    first: {
        extensionId: firstExtensionId,
        tagName: 'magpie-first-settings',
        runtime: runtimeScript,
        template: '<template id="metadata-settings-template"></template>',
        settings: `(() => {
            const context = document.currentScript?.magpieExtensionContext;
            context.waitUntil(window.MagpieMetadataSettings.register('magpie-first-settings'));
        })();`,
        settingsPath: 'first-settings.js',
    },
    second: {
        extensionId: secondExtensionId,
        tagName: 'magpie-second-settings',
        runtime: runtimeScript,
        template: '<template id="metadata-settings-template"></template>',
        settings: `(() => {
            const context = document.currentScript?.magpieExtensionContext;
            context.waitUntil(window.MagpieMetadataSettings.register('magpie-second-settings'));
        })();`,
        settingsPath: 'second-settings.js',
    },
} as const;

type ProviderName = keyof typeof providers;

const mockFetch = vi.fn();
let injectedElements: HTMLElement[] = [];
let originalAppendChild: typeof document.head.appendChild;

function mockJsonResponse(body: unknown) {
    return {
        ok: true,
        status: 200,
        headers: new Headers({ 'content-type': 'application/json' }),
        json: () => Promise.resolve(body),
    };
}

function mockTextResponse(body: string, contentType: string) {
    return {
        ok: true,
        status: 200,
        headers: new Headers({ 'content-type': contentType }),
        text: () => Promise.resolve(body),
    };
}

function mockErrorResponse(status: number, message: string) {
    return {
        ok: false,
        status,
        statusText: message,
        headers: new Headers({ 'content-type': 'text/plain' }),
        text: () => Promise.resolve(message),
    };
}

function createManifest(firstProvider: ProviderName): UIManifest {
    const secondProvider = firstProvider === 'first' ? 'second' : 'first';
    const orderedProviders = [firstProvider, secondProvider] as const;
    return {
        assets: [
            ...orderedProviders.map((providerName, index) => ({
                id: `${providerName}-runtime`,
                extensionId: providers[providerName].extensionId,
                name: `${providerName} runtime`,
                kind: 'Script' as const,
                url: null,
                relativePath: 'metadata-settings.js',
                linkRel: null,
                integrity: null,
                priority: 80 + index * 10,
            })),
            ...orderedProviders.map((providerName, index) => ({
                id: `${providerName}-settings`,
                extensionId: providers[providerName].extensionId,
                name: `${providerName} settings`,
                kind: 'Script' as const,
                url: null,
                relativePath: providers[providerName].settingsPath,
                linkRel: null,
                integrity: null,
                priority: 100 + index * 10,
            })),
        ],
        extensions: [],
    };
}

function configureAssetResponses(manifest: UIManifest, failingTemplateProvider?: ProviderName) {
    let failedTemplate = false;
    mockFetch.mockImplementation((input: string) => {
        if (input === '/api/ui/manifest') {
            return Promise.resolve(mockJsonResponse(manifest));
        }

        for (const [providerName, provider] of Object.entries(providers) as [
            ProviderName,
            (typeof providers)[ProviderName],
        ][]) {
            const assetBase = `/api/ui/assets/${provider.extensionId}`;
            if (input === `${assetBase}/metadata-settings.js`) {
                return Promise.resolve(mockTextResponse(provider.runtime, 'text/javascript'));
            }
            if (input === `${assetBase}/metadata-settings.html`) {
                if (providerName === failingTemplateProvider && !failedTemplate) {
                    failedTemplate = true;
                    return Promise.resolve(mockErrorResponse(503, 'Template unavailable'));
                }
                return Promise.resolve(mockTextResponse(provider.template, 'text/html'));
            }
            if (input === `${assetBase}/${provider.settingsPath}`) {
                return Promise.resolve(mockTextResponse(provider.settings, 'text/javascript'));
            }
        }

        return Promise.reject(new Error(`Unexpected request: ${input}`));
    });
}

function clearMetadataSettingsRuntime() {
    const script = document.createElement('script');
    script.textContent = 'Reflect.deleteProperty(window, "MagpieMetadataSettings");';
    document.head.appendChild(script);
    script.remove();
}

describe('ExtensionLoader provider asset recovery', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        injectedElements = [];
        localStorage.clear();
        Reflect.deleteProperty(window, 'MagpieMetadataSettings');
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
        for (const element of injectedElements) {
            element.remove();
        }
        injectedElements = [];
        document.head.appendChild = originalAppendChild;
        Reflect.deleteProperty(window, 'MagpieMetadataSettings');
        vi.unstubAllGlobals();
    });

    it('recovers both providers and supports either provider runtime loading first', async () => {
        const manifest = createManifest('first');
        configureAssetResponses(manifest, 'first');
        localStorage.setItem('magpie_auth_token', 'test-token');
        const loader = new ExtensionLoader({ apiBaseUrl: '/api' });

        await expect(loader.init()).rejects.toThrow('Template unavailable');

        expect(loader.state.state).toBe('error');
        expect(customElements.get(providers.first.tagName)).toBeUndefined();
        expect(customElements.get(providers.second.tagName)).toBeUndefined();

        loader.reset();
        const define = vi.spyOn(customElements, 'define').mockImplementationOnce(() => {
            throw new Error('Provider registration failed');
        });
        await expect(loader.init()).rejects.toThrow('Provider registration failed');

        expect(loader.state.state).toBe('error');
        expect(customElements.get(providers.first.tagName)).toBeUndefined();
        expect(customElements.get(providers.second.tagName)).toBeUndefined();

        define.mockRestore();
        loader.reset();
        await loader.init();

        expect(loader.state.state).toBe('ready');
        expect(customElements.get(providers.first.tagName)).toBeDefined();
        expect(customElements.get(providers.second.tagName)).toBeDefined();
        expect(
            mockFetch.mock.calls.map(([url]) => url as string).filter((url) => url.endsWith('/metadata-settings.html')),
        ).toEqual([
            `/api/ui/assets/${firstExtensionId}/metadata-settings.html`,
            `/api/ui/assets/${firstExtensionId}/metadata-settings.html`,
        ]);

        loader.reset();
        clearMetadataSettingsRuntime();
        mockFetch.mockClear();
        const secondFirstManifest = createManifest('second');
        configureAssetResponses(secondFirstManifest);
        const secondFirstLoader = new ExtensionLoader({ apiBaseUrl: '/api' });
        await secondFirstLoader.init();

        expect(secondFirstLoader.state.state).toBe('ready');
        expect(
            mockFetch.mock.calls.map(([url]) => url as string).filter((url) => url.endsWith('/metadata-settings.html')),
        ).toEqual([`/api/ui/assets/${secondExtensionId}/metadata-settings.html`]);

        secondFirstLoader.reset();
        clearMetadataSettingsRuntime();
        mockFetch.mockClear();
        const firstFirstManifest = createManifest('first');
        configureAssetResponses(firstFirstManifest);
        const firstFirstLoader = new ExtensionLoader({ apiBaseUrl: '/api' });
        await firstFirstLoader.init();

        expect(firstFirstLoader.state.state).toBe('ready');
        expect(
            mockFetch.mock.calls.map(([url]) => url as string).filter((url) => url.endsWith('/metadata-settings.html')),
        ).toEqual([`/api/ui/assets/${firstExtensionId}/metadata-settings.html`]);
    });
});
