import { apiFetch } from '../lib/api';

import type { Asset, ExtensionLoaderState, LoaderState, UIManifest } from './types';

export type StateChangeCallback = (state: ExtensionLoaderState) => void;

export interface ExtensionLoaderConfig {
    apiBaseUrl?: string;
}

export function isValidCustomElementName(tagName: string): boolean {
    return tagName.includes('-');
}

function getAssetUrl(apiBaseUrl: string, asset: Asset): string {
    if (asset.url) {
        return asset.url;
    }
    return `${apiBaseUrl}/ui/assets/${asset.extensionId}/${asset.relativePath}`;
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

async function injectAsset(apiBaseUrl: string, asset: Asset): Promise<HTMLElement | null> {
    const url = getAssetUrl(apiBaseUrl, asset);

    switch (asset.kind) {
        case 'Script':
            return injectScript(url, asset.integrity);
        case 'Style':
            return injectStyle(url, asset.integrity);
        case 'Link':
            if (asset.linkRel) {
                return injectLink(url, asset.linkRel);
            }
            return null;
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
            const message = err instanceof Error ? err.message : 'Unknown error loading extensions';
            this._setError(message, err);
            throw err;
        }
    }

    reset(): void {
        for (const element of this._injectedElements) {
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

    getExtensions() {
        return this._manifest?.extensions ?? [];
    }

    getAssets() {
        return this._manifest?.assets ?? [];
    }
}
