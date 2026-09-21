import { createContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { ExtensionLoader } from './ExtensionLoader';
import type { ExtensionLoaderState, UIContext, UIExtension } from './types';

export interface ExtensionContextValue {
    state: ExtensionLoaderState;

    retriesExhausted: boolean;

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
    retriesExhausted: false,
    context: {
        apiBaseUrl: '/api',
        userId: null,
        username: null,
        role: null,
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
}

export function ExtensionProvider({
    children,
    apiBaseUrl = '/api',
    userId = null,
    username = null,
    role = null,
}: ExtensionProviderProps) {
    const [loader] = useState(() => new ExtensionLoader({ apiBaseUrl }));

    const [loaderState, setLoaderState] = useState<ExtensionLoaderState>(loader.state);
    const automaticRetryCountRef = useRef(0);
    const automaticRetryKeyRef = useRef<string | null>(null);
    const [retriesExhausted, setRetriesExhausted] = useState(false);

    useEffect(() => {
        const unsubscribe = loader.subscribe(setLoaderState);
        return unsubscribe;
    }, [loader]);

    useEffect(() => {
        if (!userId) {
            return;
        }
        void loader.init().catch(() => undefined);
    }, [loader, userId]);

    useEffect(() => {
        if (loaderState.state === 'ready') {
            automaticRetryCountRef.current = 0;
            automaticRetryKeyRef.current = null;
            setRetriesExhausted(false);
            return;
        }

        if (loaderState.state !== 'error' || !userId) {
            return;
        }

        const retryKey = `${userId}:${role ?? ''}`;
        if (automaticRetryKeyRef.current !== retryKey) {
            automaticRetryKeyRef.current = retryKey;
            automaticRetryCountRef.current = 0;
        }

        if (automaticRetryCountRef.current >= 10) {
            setRetriesExhausted(true);
            return;
        }

        setRetriesExhausted(false);

        const timeoutId = setTimeout(() => {
            automaticRetryCountRef.current += 1;
            loader.reset();
            void loader.init().catch(() => undefined);
        }, 3000);

        return () => clearTimeout(timeoutId);
    }, [loader, loaderState.state, role, userId]);

    const contextValue = useMemo<ExtensionContextValue>(
        () => ({
            state: loaderState,
            retriesExhausted,
            context: { apiBaseUrl, userId, username, role },
            getExtensionsForSlot: (slot: string) => loader.getExtensionsForSlot(slot),
            retry: async () => {
                loader.reset();
                await loader.init();
            },
        }),
        [loaderState, retriesExhausted, loader, apiBaseUrl, userId, username, role],
    );

    return <ExtensionContext.Provider value={contextValue}>{children}</ExtensionContext.Provider>;
}
