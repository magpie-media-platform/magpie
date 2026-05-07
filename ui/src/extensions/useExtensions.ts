import { useContext, useMemo } from 'react';

import { ExtensionContext } from './ExtensionProvider';
import type { LoaderState, UIExtension } from './types';

export interface UseExtensionsReturn {
    state: LoaderState;

    isLoading: boolean;

    isReady: boolean;

    isError: boolean;

    error: string | null;

    getExtensionsForSlot: (slot: string) => UIExtension[];

    retry: () => Promise<void>;

    apiBaseUrl: string;

    userId: string | null;

    username: string | null;

    role: string | null;
}

export function useExtensions(): UseExtensionsReturn {
    const contextValue = useContext(ExtensionContext);

    return useMemo(
        () => ({
            state: contextValue.state.state,
            isLoading: contextValue.state.state === 'loading',
            isReady: contextValue.state.state === 'ready',
            isError: contextValue.state.state === 'error',
            error: contextValue.state.error?.message ?? null,
            getExtensionsForSlot: contextValue.getExtensionsForSlot,
            retry: contextValue.retry,
            apiBaseUrl: contextValue.context.apiBaseUrl,
            userId: contextValue.context.userId,
            username: contextValue.context.username,
            role: contextValue.context.role,
        }),
        [contextValue],
    );
}

export interface UseSlotExtensionsReturn {
    extensions: UIExtension[];

    isReady: boolean;

    isLoading: boolean;
}

export function useSlotExtensions(slot: string): UseSlotExtensionsReturn {
    const { isReady, isLoading, getExtensionsForSlot } = useExtensions();

    const extensions = useMemo(
        () => (isReady ? getExtensionsForSlot(slot) : []),
        [isReady, getExtensionsForSlot, slot],
    );

    return {
        extensions,
        isReady,
        isLoading,
    };
}
