import { useMutation } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { queryErrorOrNull } from '@/lib/utils';

export interface ConnectionTestResult {
    success: boolean;

    message: string;
}

interface UseTestConnectionResult {
    test: (id: string) => Promise<ConnectionTestResult>;

    isPending: boolean;

    error: Error | null;
}

function useTestConnection(basePath: string): UseTestConnectionResult {
    const mutation = useMutation({
        mutationFn: (id: string) => apiPost<ConnectionTestResult>(`${basePath}/${id}/test`),
    });

    return {
        test: mutation.mutateAsync,
        isPending: mutation.isPending,
        error: queryErrorOrNull(mutation),
    };
}

export function useTestDownloadClient(): UseTestConnectionResult {
    return useTestConnection('/download-clients');
}

export function useTestIndexer(): UseTestConnectionResult {
    return useTestConnection('/indexers');
}

export function useTestMediaServer(): UseTestConnectionResult {
    return useTestConnection('/media-servers');
}
