import { useCallback } from 'react';

import {
    getErrorMessage,
    getErrorDisplayInfo,
    isNetworkError,
    isAuthError,
    type ErrorDisplayInfo,
} from '../lib/errors';

export { isRetryableError } from '../lib/errors';

export { isApiError, AUTH_FAILURE_EVENT } from '../lib/api';

export interface UseApiErrorOptions {
    onToast?: (options: { title: string; description?: string; variant: 'error' | 'warning' }) => void;

    showToast?: boolean;

    logErrors?: boolean;
}

export interface UseApiErrorReturn {
    handleError: (error: unknown, context?: string) => ErrorDisplayInfo;

    getError: (error: unknown, fallback?: string) => string;

    onMutationError: (context?: string) => (error: unknown) => void;
}

export function useApiError(options: UseApiErrorOptions = {}): UseApiErrorReturn {
    const { onToast, showToast = true, logErrors = import.meta.env.DEV } = options;

    const handleError = useCallback(
        (error: unknown, context?: string): ErrorDisplayInfo => {
            const info = getErrorDisplayInfo(error);

            if (logErrors) {
                console.error(context ? `Error ${context}:` : 'API Error:', error);
            }

            if (isAuthError(error)) {
                return info;
            }

            if (showToast && onToast) {
                const title = context ? `Error ${context}` : isNetworkError(error) ? 'Network Error' : 'Error';

                onToast({
                    title,
                    description: info.message,
                    variant: isNetworkError(error) ? 'warning' : 'error',
                });
            }

            return info;
        },
        [onToast, showToast, logErrors],
    );

    const getError = useCallback((error: unknown, fallback?: string): string => {
        return getErrorMessage(error, fallback);
    }, []);

    const onMutationError = useCallback(
        (context?: string) => {
            return (error: unknown) => {
                handleError(error, context);
            };
        },
        [handleError],
    );

    return {
        handleError,
        getError,
        onMutationError,
    };
}

export default useApiError;
