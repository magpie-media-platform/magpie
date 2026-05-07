/* eslint-disable react-refresh/only-export-components */
import { createContext, useEffect, useMemo, useState, type ReactNode } from 'react';

import { ExtensionLoader, type ExtensionLoaderConfig } from './ExtensionLoader';
import type { ExtensionLoaderState, UIContext, UIExtension } from './types';

export interface ExtensionContextValue {
    state: ExtensionLoaderState;

    context: UIContext;

    getExtensionsForSlot: (slot: string) => UIExtension[];

    retry: () => Promise<void>;
}

const defaultContextValue: ExtensionContextValue = {
    state: {
        state: 'idle',
        error: null,
        manifest: null,
    },
    context: {
        apiBaseUrl: '/api',
        userId: null,
        username: null,
        role: null,
        theme: null,
    },
    getExtensionsForSlot: () => [],
    retry: () => Promise.resolve(),
};

export const ExtensionContext = createContext<ExtensionContextValue>(defaultContextValue);
ExtensionContext.displayName = 'ExtensionContext';

export interface ExtensionProviderProps {
    children: ReactNode;

    apiBaseUrl?: string;

    userId?: string | null;

    username?: string | null;

    role?: string | null;

    theme?: 'light' | 'dark' | null;
}

export function ExtensionProvider({
    children,
    apiBaseUrl = '/api',
    userId = null,
    username = null,
    role = null,
    theme = null,
}: ExtensionProviderProps) {
    const [loader] = useState(() => new ExtensionLoader({ apiBaseUrl } as ExtensionLoaderConfig));

    const [loaderState, setLoaderState] = useState<ExtensionLoaderState>(loader.state);

    useEffect(() => {
        const unsubscribe = loader.subscribe(setLoaderState);
        return unsubscribe;
    }, [loader]);

    useEffect(() => {
        loader.init().catch((err) => console.error('[ExtensionProvider] init failed:', err));
    }, [loader]);

    const contextValue = useMemo<ExtensionContextValue>(
        () => ({
            state: loaderState,
            context: { apiBaseUrl, userId, username, role, theme },
            getExtensionsForSlot: (slot: string) => loader.getExtensionsForSlot(slot),
            retry: async () => {
                loader.reset();
                await loader.init();
            },
        }),
        [loaderState, loader, apiBaseUrl, userId, username, role, theme],
    );

    return <ExtensionContext.Provider value={contextValue}>{children}</ExtensionContext.Provider>;
}
