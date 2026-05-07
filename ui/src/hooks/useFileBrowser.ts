import { useCallback, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { queryErrorOrNull } from '@/lib/utils';

export interface FileSystemEntry {
    name: string;

    path: string;

    type: 'file' | 'folder';

    size?: number;
}

export interface FileBrowserRequest {
    path?: string;
}

async function fetchContents(path: string): Promise<FileSystemEntry[]> {
    const request: FileBrowserRequest = { path };
    return apiPost<FileSystemEntry[]>('/file-browser/contents', request);
}

export const fileBrowserQueryKey = (path: string | undefined) => ['file-browser', path] as const;

export function useFileBrowser() {
    const queryClient = useQueryClient();
    const [path, setPath] = useState<string | undefined>(undefined);

    // Default staleTime/gcTime intentional: always-fresh on open, cached briefly for fast reopens.
    const query = useQuery({
        queryKey: fileBrowserQueryKey(path),
        queryFn: () => fetchContents(path!),
        enabled: path !== undefined,
    });

    const navigate = useCallback((next: string) => {
        setPath(next);
    }, []);

    const navigateAsync = useCallback(
        (next: string) => {
            setPath(next);
            return queryClient.fetchQuery({
                queryKey: fileBrowserQueryKey(next),
                queryFn: () => fetchContents(next),
                staleTime: 0,
            });
        },
        [queryClient],
    );

    const reset = useCallback(() => {
        setPath(undefined);
    }, []);

    return {
        path,

        navigate,

        navigateAsync,

        // Backwards-compatible aliases for existing consumers/tests.
        browse: navigate,

        browseAsync: navigateAsync,

        entries: path !== undefined && query.data ? query.data : [],

        isLoading: query.isFetching,

        error: queryErrorOrNull(query),

        reset,
    };
}
