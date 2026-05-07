import { useQuery, useMutation, useQueryClient, type Query } from '@tanstack/react-query';

import { apiGet, apiPost, apiDelete, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { BadgeVariant } from '@/types/badge';
import type { PagedList } from '@/types/paging';

export type DownloadStatusValue = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7;

export const downloadStatusLabels: Record<DownloadStatusValue, string> = {
    0: 'Grabbed',
    1: 'Queued',
    2: 'Downloading',
    3: 'Paused',
    4: 'Stalled',
    5: 'Downloaded',
    6: 'Imported',
    7: 'Failed',
};

// Mirrors Magpie.Models.Enums.DownloadStatus terminal states (Imported, Failed).
export const terminalDownloadStatuses: ReadonlySet<DownloadStatusValue> = new Set([6, 7]);

export function isTerminalDownloadStatus(status: DownloadStatusValue | undefined): boolean {
    return status !== undefined && terminalDownloadStatuses.has(status);
}

export const downloadStatusVariants: Record<DownloadStatusValue, BadgeVariant> = {
    0: 'grabbed',
    1: 'queued',
    2: 'downloading',
    3: 'paused',
    4: 'stalled',
    5: 'downloaded',
    6: 'imported',
    7: 'failed',
};

export interface ApiDownload {
    id: string;

    releaseId?: string | null;

    downloadClientId: string;

    externalId: string;

    title: string;

    status: DownloadStatusValue;

    outputPath?: string | null;

    statusReason?: string | null;

    dateCompleted?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export type PagedDownloads = PagedList<ApiDownload>;

export interface DownloadQueryParams {
    releaseId?: string;

    downloadClientId?: string;

    status?: DownloadStatusValue;

    pageNumber?: number;

    pageSize?: number;

    sortBy?: string;

    sortDirection?: 0 | 1 | 2;
}

export const downloadQueryKeys = {
    all: ['downloads'] as const,
    list: (params?: DownloadQueryParams) => ['downloads', 'list', params ?? {}] as const,
    detail: (id: string) => ['downloads', 'detail', id] as const,
};

async function fetchDownloads(params?: DownloadQueryParams): Promise<PagedDownloads> {
    const url = buildUrl('/downloads', params);
    return apiGet<PagedDownloads>(url);
}

async function fetchDownloadById(id: string): Promise<ApiDownload> {
    return apiGet<ApiDownload>(`/downloads/${id}`);
}

async function removeDownload(id: string): Promise<void> {
    return apiDelete(`/downloads/${id}`);
}

async function pauseDownload(id: string): Promise<void> {
    return apiPost(`/downloads/${id}/pause`);
}

async function resumeDownload(id: string): Promise<void> {
    return apiPost(`/downloads/${id}/resume`);
}

export function useDownloads(params?: DownloadQueryParams) {
    const queryClient = useQueryClient();

    const downloadsQuery = useQuery({
        queryKey: downloadQueryKeys.list(params),
        queryFn: () => fetchDownloads(params),
        refetchInterval: POLLING_INTERVALS.fast,
        staleTime: POLLING_INTERVALS.fast / 2,
    });

    const removeMutation = useMutation({
        mutationFn: removeDownload,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const pauseMutation = useMutation({
        mutationFn: pauseDownload,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const resumeMutation = useMutation({
        mutationFn: resumeDownload,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const downloads: ApiDownload[] = downloadsQuery.data?.items ?? [];
    const pagination = downloadsQuery.data
        ? {
              pageNumber: downloadsQuery.data.pageNumber,
              pageSize: downloadsQuery.data.pageSize,
              totalItems: downloadsQuery.data.totalItems,
          }
        : null;

    return {
        downloads,
        pagination,
        isLoading: downloadsQuery.isLoading,
        error: queryErrorOrNull(downloadsQuery),

        removeDownload: removeMutation.mutateAsync,
        isRemoving: removeMutation.isPending,

        pauseDownload: pauseMutation.mutateAsync,
        isPausing: pauseMutation.isPending,

        resumeDownload: resumeMutation.mutateAsync,
        isResuming: resumeMutation.isPending,

        refetch: downloadsQuery.refetch,
    };
}

export function useDownload(id: string, enabled = true) {
    const downloadQuery = useQuery({
        queryKey: downloadQueryKeys.detail(id),
        queryFn: () => fetchDownloadById(id),
        enabled: enabled && Boolean(id),
        refetchInterval: (query: Query<ApiDownload, Error>) => {
            if (isTerminalDownloadStatus(query.state.data?.status)) {
                return false;
            }
            return POLLING_INTERVALS.fast;
        },
        staleTime: POLLING_INTERVALS.fast / 2,
    });

    return {
        download: downloadQuery.data ?? null,
        isLoading: downloadQuery.isLoading,
        error: queryErrorOrNull(downloadQuery),
        refetch: downloadQuery.refetch,
    };
}
