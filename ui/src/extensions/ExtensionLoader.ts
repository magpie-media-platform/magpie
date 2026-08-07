import { apiFetch, apiFetchResponse } from '../lib/api';

import type {
    Asset,
    AssetKindName,
    ExtensionAssetContext,
    ExtensionLoaderState,
    LoaderState,
    UIManifest,
} from './types';

export type StateChangeCallback = (state: ExtensionLoaderState) => void;

export interface ExtensionLoaderConfig {
    apiBaseUrl?: string;
}

interface ProtectedExtensionScriptElement extends HTMLScriptElement {
    magpieExtensionContext?: ExtensionAssetContext;
}

const protectedAssetSegmentPattern = /^[\p{L}\p{N}][\p{L}\p{N}._~@+() -]*$/u;

export function isValidCustomElementName(tagName: string): boolean {
    return tagName.includes('-');
}

function getAssetUrl(apiBaseUrl: string, asset: Asset): string {
    if (asset.url) {
        return asset.url;
    }
    return `${apiBaseUrl}${getProtectedRelativeAssetPath(asset.extensionId, asset.relativePath)}`;
}

function getProtectedRelativeAssetPath(extensionId: string, relativePath: string | null): string {
    if (!relativePath) {
        throw new Error('Invalid protected extension asset path.');
    }

    let canonicalPath = relativePath;
    for (let decodeCount = 0; canonicalPath.includes('%'); decodeCount += 1) {
        if (decodeCount === 8) {
            throw new Error('Invalid protected extension asset path.');
        }

        let decodedPath: string;
        try {
            decodedPath = decodeURIComponent(canonicalPath);
        } catch {
            throw new Error('Invalid protected extension asset path.');
        }

        if (decodedPath.split('/').length !== canonicalPath.split('/').length) {
            throw new Error('Invalid protected extension asset path.');
        }
        canonicalPath = decodedPath;
    }

    if (
        canonicalPath.startsWith('/') ||
        canonicalPath.includes('\\') ||
        canonicalPath.includes('?') ||
        canonicalPath.includes('#') ||
        canonicalPath.includes(':')
    ) {
        throw new Error('Invalid protected extension asset path.');
    }

    const segments = canonicalPath.split('/');
    if (
        segments.some(
            (segment) =>
                segment === '' ||
                segment === '.' ||
                segment === '..' ||
                segment.trim() !== segment ||
                !protectedAssetSegmentPattern.test(segment),
        )
    ) {
        throw new Error('Invalid protected extension asset path.');
    }

    return `/ui/assets/${encodeURIComponent(extensionId)}/${segments.map(encodeURIComponent).join('/')}`;
}

function normalizeAssetKind(asset: Asset): AssetKindName | null {
    if (asset.kind === 'Script' || asset.kind === 'Style' || asset.kind === 'Link') {
        return asset.kind;
    }

    switch (asset.kind) {
        case 0:
            return 'Script';
        case 1:
            return 'Style';
        case 2:
            return 'Link';
        default:
            return null;
    }
}

function isProtectedRelativeAsset(asset: Asset): boolean {
    const kind = normalizeAssetKind(asset);
    return !asset.url && (kind === 'Script' || kind === 'Style');
}

function injectScript(url: string, integrity: string | null): Promise<HTMLScriptElement> {
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = url;
        script.async = false;
        if (integrity) {
            script.integrity = integrity;
            script.crossOrigin = 'anonymous';
        }
        script.onload = () => resolve(script);
        script.onerror = () => reject(new Error(`Failed to load script: ${url}`));
        document.head.appendChild(script);
    });
}

function injectStyle(url: string, integrity: string | null): Promise<HTMLLinkElement> {
    return new Promise((resolve, reject) => {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = url;
        if (integrity) {
            link.integrity = integrity;
            link.crossOrigin = 'anonymous';
        }
        link.onload = () => resolve(link);
        link.onerror = () => reject(new Error(`Failed to load stylesheet: ${url}`));
        document.head.appendChild(link);
    });
}

function injectLink(url: string, linkRel: string): HTMLLinkElement {
    const link = document.createElement('link');
    link.rel = linkRel;
    link.href = url;
    document.head.appendChild(link);
    return link;
}

async function injectProtectedRelativeScript(apiBaseUrl: string, asset: Asset): Promise<HTMLScriptElement> {
    const source = await loadProtectedRelativeAssetText(apiBaseUrl, asset.extensionId, asset.relativePath);
    const trackedWork: Promise<unknown>[] = [];
    let acceptingTrackedWork = true;

    const script: ProtectedExtensionScriptElement = document.createElement('script');
    script.async = false;
    script.textContent = source;
    script.magpieExtensionContext = Object.freeze({
        apiBaseUrl,
        extensionId: asset.extensionId,
        loadAssetText: (relativePath: string) =>
            loadProtectedRelativeAssetText(apiBaseUrl, asset.extensionId, relativePath),
        waitUntil: (work: PromiseLike<unknown>) => {
            if (!acceptingTrackedWork) {
                throw new Error('Extension scripts must register asynchronous work during execution.');
            }
            trackedWork.push(Promise.resolve(work));
        },
    });

    let executionError: unknown;
    let executionFailed = false;
    const executionWindow = document.defaultView ?? window;
    const captureExecutionError = (event: ErrorEvent) => {
        if (!executionFailed) {
            executionFailed = true;
            executionError =
                event.error instanceof Error
                    ? event.error
                    : new Error(event.message || 'Extension script execution failed.');
        }
        event.preventDefault();
    };
    executionWindow.addEventListener('error', captureExecutionError, true);
    try {
        document.head.appendChild(script);
    } catch (error) {
        if (!executionFailed) {
            executionError = error;
            executionFailed = true;
        }
    } finally {
        executionWindow.removeEventListener('error', captureExecutionError, true);
        acceptingTrackedWork = false;
        delete script.magpieExtensionContext;
    }

    if (executionFailed) {
        await Promise.allSettled(trackedWork);
        script.remove();
        throw executionError instanceof Error
            ? executionError
            : Object.assign(new Error('Extension script execution failed.'), { cause: executionError });
    }

    try {
        await Promise.all(trackedWork);
    } catch (error) {
        script.remove();
        throw error;
    }
    return script;
}

async function loadProtectedRelativeAssetText(
    apiBaseUrl: string,
    extensionId: string,
    relativePath: string | null,
): Promise<string> {
    const response = await apiFetchResponse(getProtectedRelativeAssetPath(extensionId, relativePath), {
        baseUrl: apiBaseUrl,
        method: 'GET',
    });
    return response.text();
}

async function injectProtectedRelativeLink(
    apiBaseUrl: string,
    asset: Asset,
    linkRel: string,
): Promise<HTMLLinkElement> {
    const response = await apiFetchResponse(getProtectedRelativeAssetPath(asset.extensionId, asset.relativePath), {
        baseUrl: apiBaseUrl,
        method: 'GET',
    });
    return injectLink(URL.createObjectURL(await response.blob()), linkRel);
}

async function injectProtectedRelativeStyle(apiBaseUrl: string, asset: Asset): Promise<HTMLStyleElement> {
    const css = await loadProtectedRelativeAssetText(apiBaseUrl, asset.extensionId, asset.relativePath);

    const style = document.createElement('style');
    style.textContent = css;
    document.head.appendChild(style);
    return style;
}

async function injectAsset(apiBaseUrl: string, asset: Asset): Promise<HTMLElement | null> {
    const kind = normalizeAssetKind(asset);
    if (!kind) {
        console.warn(`[ExtensionLoader] Unknown asset kind "${asset.kind}" for asset "${asset.name}".`);
        return null;
    }

    if (isProtectedRelativeAsset(asset)) {
        if (kind === 'Script') {
            return injectProtectedRelativeScript(apiBaseUrl, asset);
        }

        return injectProtectedRelativeStyle(apiBaseUrl, asset);
    }

    const url = getAssetUrl(apiBaseUrl, asset);

    switch (kind) {
        case 'Script':
            return injectScript(url, asset.integrity);
        case 'Style':
            return injectStyle(url, asset.integrity);
        case 'Link':
            if (!asset.linkRel) {
                console.warn(`[ExtensionLoader] Link asset "${asset.name}" has no rel attribute.`);
                return null;
            }
            if (asset.url) {
                return injectLink(url, asset.linkRel);
            }
            return injectProtectedRelativeLink(apiBaseUrl, asset, asset.linkRel);
        default:
            return null;
    }
}

export class ExtensionLoader {
    private _state: LoaderState = 'idle';
    private _error: { message: string; cause?: unknown } | null = null;
    private _manifest: UIManifest | null = null;
    private _apiBaseUrl: string;
    private _subscribers: Set<StateChangeCallback> = new Set();
    private _initPromise: Promise<void> | null = null;
    private _injectedElements: HTMLElement[] = [];

    constructor(config: ExtensionLoaderConfig = {}) {
        this._apiBaseUrl = config.apiBaseUrl ?? '/api';
    }

    get state(): ExtensionLoaderState {
        return {
            state: this._state,
            error: this._error,
            manifest: this._manifest,
        };
    }

    subscribe(callback: StateChangeCallback): () => void {
        this._subscribers.add(callback);
        callback(this.state);
        return () => {
            this._subscribers.delete(callback);
        };
    }

    private _notify(): void {
        const currentState = this.state;
        for (const callback of this._subscribers) {
            callback(currentState);
        }
    }

    private _setState(state: LoaderState): void {
        this._state = state;
        this._notify();
    }

    private _setError(message: string, cause?: unknown): void {
        this._error = { message, cause };
        this._state = 'error';
        this._notify();
    }

    private async _fetchManifest(): Promise<UIManifest> {
        return apiFetch<UIManifest>('/ui/manifest', { baseUrl: this._apiBaseUrl });
    }

    init(): Promise<void> {
        if (this._initPromise) {
            return this._initPromise;
        }

        if (this._state === 'ready') {
            return Promise.resolve();
        }

        this._initPromise = this._doInit();
        return this._initPromise;
    }

    private async _doInit(): Promise<void> {
        this._setState('loading');

        try {
            const manifest = await this._fetchManifest();
            this._manifest = manifest;

            const sortedAssets = [...manifest.assets].sort((a, b) => a.priority - b.priority);

            const seenUrls = new Set<string>();
            for (const asset of sortedAssets) {
                const url = getAssetUrl(this._apiBaseUrl, asset);
                if (seenUrls.has(url)) {
                    continue;
                }
                seenUrls.add(url);
                const element = await injectAsset(this._apiBaseUrl, asset);
                if (element) {
                    this._injectedElements.push(element);
                }
            }

            this._setState('ready');
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to load extensions.';
            this._setError(message, err);
            throw err;
        }
    }

    reset(): void {
        for (const element of this._injectedElements) {
            if (element instanceof HTMLLinkElement && element.href.startsWith('blob:')) {
                URL.revokeObjectURL(element.href);
            }
            element.remove();
        }
        this._injectedElements = [];

        this._initPromise = null;
        this._manifest = null;
        this._error = null;
        this._setState('idle');
    }

    getExtensionsForSlot(slot: string) {
        if (!this._manifest) {
            return [];
        }

        return this._manifest.extensions
            .filter((ext) => {
                if (ext.slot !== slot) {
                    return false;
                }
                if (!isValidCustomElementName(ext.tagName)) {
                    console.warn(
                        `[ExtensionLoader] Extension "${ext.name}" has invalid tagName "${ext.tagName}": ` +
                            `custom element names must contain a hyphen.`,
                    );
                    return false;
                }
                return true;
            })
            .sort((a, b) => a.priority - b.priority);
    }
}
