import { useCallback, useRef } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { queryErrorOrNull } from '@/lib/utils';

export interface ConnectionTestResult {
    success: boolean;

    message: string;
}

interface UseTestConnectionResult<TTarget = string> {
    test: (target: TTarget) => Promise<ConnectionTestResult>;

    isPending: boolean;

    error: Error | null;
}

export interface IntegrationSettingsTestRequest {
    providerType?: string;

    id?: string;

    settings?: Record<string, string>;
}

interface UseTestSettingsResult {
    test: (request: IntegrationSettingsTestRequest) => Promise<ConnectionTestResult>;

    isPending: boolean;

    error: Error | null;

    cancel: () => void;
}

function useTestSettings(path: string): UseTestSettingsResult {
    const abortRef = useRef<AbortController | null>(null);
    const mutation = useMutation({
        mutationFn: (request: IntegrationSettingsTestRequest) => {
            abortRef.current?.abort();
            const controller = new AbortController();
            abortRef.current = controller;
            return apiPost<ConnectionTestResult>(path, request, { signal: controller.signal });
        },
    });

    const mutationRef = useRef(mutation);
    mutationRef.current = mutation;

    const cancel = useCallback(() => {
        abortRef.current?.abort();
        abortRef.current = null;
        mutationRef.current.reset();
    }, []);

    return {
        test: mutation.mutateAsync,
        isPending: mutation.isPending,
        error: queryErrorOrNull(mutation),
        cancel,
    };
}

function useTestConnection<TTarget>(
    queryKeyPrefix: string,
    buildPath: (target: TTarget) => string,
): UseTestConnectionResult<TTarget> {
    const queryClient = useQueryClient();

    const mutation = useMutation({
        mutationFn: (target: TTarget) => apiPost<ConnectionTestResult>(buildPath(target)),
        onSettled: () => {
            void queryClient.invalidateQueries({ queryKey: [queryKeyPrefix] });
        },
    });

    return {
        test: mutation.mutateAsync,
        isPending: mutation.isPending,
        error: queryErrorOrNull(mutation),
    };
}

export function useTestDownloadClient(): UseTestConnectionResult {
    return useTestConnection('download-clients', (id: string) => `/download-clients/${id}/test`);
}

export function useTestMediaServer(): UseTestConnectionResult {
    return useTestConnection('media-servers', (id: string) => `/media-servers/${id}/test`);
}

export function useTestMediaServerSettings(): UseTestSettingsResult {
    return useTestSettings('/media-servers/test');
}

export function useTestDownloadClientSettings(): UseTestSettingsResult {
    return useTestSettings('/download-clients/test');
}
