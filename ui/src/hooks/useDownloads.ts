import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiDelete, buildUrl } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { EventStreamPayload } from '@/hooks/useEventStream';
import { magpieEventNames } from '@/lib/magpieEventNames';
import { queryErrorOrNull } from '@/lib/utils';
import type { BadgeVariant } from '@/types/badge';
import type { DownloadStatusValue } from '@/types/content';
import type { PagedList, SortDirectionName } from '@/types/paging';

export type { DownloadStatusValue } from '@/types/content';

export const downloadStatusLabels: Record<DownloadStatusValue, string> = {
    Grabbed: 'Grabbed',
    Queued: 'Queued',
    Downloading: 'Downloading',
    Paused: 'Paused',
    Stalled: 'Stalled',
    Downloaded: 'Downloaded',
    Importing: 'Importing',
    Imported: 'Imported',
    Failed: 'Failed',
    Faulted: 'Faulted',
    Cancelled: 'Cancelled',
};

export const downloadStatusVariants: Record<DownloadStatusValue, BadgeVariant> = {
    Grabbed: 'grabbed',
    Queued: 'queued',
    Downloading: 'downloading',
    Paused: 'paused',
    Stalled: 'stalled',
    Downloaded: 'downloaded',
    Importing: 'importing',
    Imported: 'imported',
    Failed: 'failed',
    Faulted: 'stalled',
    Cancelled: 'cancelled',
};

/** Whether the download ended in a failure. */
export function isFailedDownloadStatus(status?: DownloadStatusValue | null): boolean {
    return status === 'Failed' || status === 'Faulted';
}

/** Whether a release with this acquisition status can be grabbed again. */
export function canRegrab(status?: DownloadStatusValue | null): boolean {
    return !status || isFailedDownloadStatus(status) || status === 'Cancelled';
}

export type DownloadFailureReasonValue =
    | 'BlockedExecutable'
    | 'BlockedFileType'
    | 'NoUsableContent'
    | 'DownloadStuck'
    | 'DownloadErrored'
    | 'ImportFailed'
    | 'NoCompatibleClient'
    | 'ClientRejected'
    | 'InsufficientDestinationSpace';

export const downloadFailureReasonLabels: Record<DownloadFailureReasonValue, string> = {
    BlockedExecutable: 'Blocked executable',
    BlockedFileType: 'Blocked file type',
    NoUsableContent: 'No usable content',
    DownloadStuck: 'Stuck download',
    DownloadErrored: 'Download errored',
    ImportFailed: 'Import failed',
    NoCompatibleClient: 'No compatible client',
    ClientRejected: 'Client rejected',
    InsufficientDestinationSpace: 'Insufficient destination space',
};

export interface ApiDownload {
    id: string;

    releaseId?: string | null;

    downloadClientId: string;

    downloadClientName?: string | null;

    externalId: string;

    title: string;

    contentHierarchyName?: string | null;

    contentMetadataTitle?: string | null;

    contentHierarchyItemId?: string | null;

    contentHierarchyParentId?: string | null;

    posterUrl?: string | null;

    releaseInfoUrl?: string | null;

    status: DownloadStatusValue;

    outputPath?: string | null;

    statusReason?: string | null;

    dateCompleted?: string | null;

    progress?: number | null;

    lastProgressAt?: string | null;

    failedAt?: string | null;

    failureReason?: DownloadFailureReasonValue | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    canPause?: boolean | null;

    canResume?: boolean | null;

    canForceStart?: boolean | null;

    clientRemovedAt?: string | null;

    heldByPolicy?: boolean | null;
}

export type PagedDownloads = PagedList<ApiDownload>;

export interface DownloadQueryParams {
    releaseId?: string;

    downloadClientId?: string;

    status?: DownloadStatusValue;

    statuses?: readonly DownloadStatusValue[];

    pageNumber?: number;

    pageSize?: number;

    sortBy?: string;

    sortDirection?: SortDirectionName;
}

export interface DownloadBulkFilter {
    releaseId?: string;

    downloadClientId?: string;

    status?: DownloadStatusValue;

    statuses?: readonly DownloadStatusValue[];
}

interface DownloadBulkArgs {
    filter: DownloadBulkFilter;

    excludeIds: string[];
}

export interface DownloadBulkActionResult {
    count: number;

    error: string | null;
}

function bulkDownloads(route: string, { filter, excludeIds }: DownloadBulkArgs): Promise<DownloadBulkActionResult> {
    return apiPost<DownloadBulkActionResult>(route, { filter: { ...filter, excludeIds } });
}

function bulkForgetDownloads(args: DownloadBulkArgs): Promise<DownloadBulkActionResult> {
    return bulkDownloads('/downloads/bulk/delete', args);
}

function bulkRemoveDownloads(args: DownloadBulkArgs): Promise<DownloadBulkActionResult> {
    return bulkDownloads('/downloads/bulk/remove', args);
}

function bulkPauseDownloads(args: DownloadBulkArgs): Promise<DownloadBulkActionResult> {
    return bulkDownloads('/downloads/bulk/pause', args);
}

function bulkResumeDownloads(args: DownloadBulkArgs): Promise<DownloadBulkActionResult> {
    return bulkDownloads('/downloads/bulk/resume', args);
}

function bulkStopDownloads(args: DownloadBulkArgs): Promise<DownloadBulkActionResult> {
    return bulkDownloads('/downloads/bulk/stop', args);
}

export const downloadQueryKeys = {
    all: ['downloads'] as const,
    list: (params?: DownloadQueryParams) => ['downloads', 'list', params ?? {}] as const,
    detail: (id: string) => ['downloads', 'detail', id] as const,
    count: (status: DownloadStatusValue, downloadClientId?: string) =>
        ['downloads', 'count', status, downloadClientId ?? null] as const,
};

export const downloadEvents = [
    'download.created',
    'download.updated',
    'download.deleted',
    magpieEventNames.downloadCompleted,
    magpieEventNames.downloadFailed,
    magpieEventNames.downloadImported,
    'download.stalled',
] as const;

const CONTENT_LINEAGE_EVENTS = [
    magpieEventNames.contentHierarchyItemUpdated,
    magpieEventNames.contentHierarchyItemDeleted,
    magpieEventNames.metadataRefreshed,
] as const;

function changesDownloadStatus(event: EventStreamPayload): boolean {
    return (
        event.context['download.changedFields']?.split(',').some((field) => field.trim().toLowerCase() === 'status') ===
        true
    );
}

interface DownloadEventInvalidationOptions {
    entityIds?: readonly string[];
    failOpenEventNames?: readonly string[];
    prePredicate?: (event: EventStreamPayload) => boolean;
    predicate?: (event: EventStreamPayload) => boolean;
    unprovableMembership?: boolean;
}

function useDownloadEventInvalidation(
    queryKeys: readonly (readonly unknown[])[],
    enabled = true,
    options: DownloadEventInvalidationOptions = {},
) {
    useInvalidateQueriesOnEvents(enabled ? downloadEvents : [], queryKeys, {
        debounceMs: 200,
        entityIds: options.entityIds,
        exact: true,
        failOpenEventNames: options.failOpenEventNames,
        maxWaitMs: 800,
        prePredicate: options.prePredicate,
        predicate: options.predicate,
        unprovableMembership: options.unprovableMembership ?? false,
    });
}

async function fetchDownloads(params?: DownloadQueryParams): Promise<PagedDownloads> {
    const url = buildUrl('/downloads', params);
    return apiGet<PagedDownloads>(url);
}

async function fetchDownload(id: string): Promise<ApiDownload> {
    return apiGet<ApiDownload>(`/downloads/${id}`);
}

async function removeDownload(id: string): Promise<void> {
    return apiDelete(`/downloads/${id}`);
}

async function deleteDownloadWithFiles(id: string): Promise<void> {
    return apiDelete(`/downloads/${id}/files`);
}

async function pauseDownload(id: string): Promise<void> {
    return apiPost(`/downloads/${id}/pause`);
}

async function resumeDownload(id: string): Promise<void> {
    return apiPost(`/downloads/${id}/resume`);
}

async function forceStartDownload(id: string): Promise<void> {
    return apiPost(`/downloads/${id}/force-start`);
}

async function retryDownload(id: string): Promise<void> {
    return apiPost(`/downloads/${id}/retry`);
}

async function cancelDownload(id: string): Promise<void> {
    return apiPost(`/downloads/${id}/cancel`);
}

export function useDownloads(params?: DownloadQueryParams, enabled = true) {
    const queryClient = useQueryClient();
    const hasFilters = !!(params?.downloadClientId || params?.releaseId || params?.status);
    const describeDownloadEvent = (event: EventStreamPayload) => {
        const current = queryClient.getQueryData<PagedDownloads>(downloadQueryKeys.list(params));
        const changedFields = event.context['download.changedFields']?.split(',') ?? [];
        const normalizedSortBy = params?.sortBy?.replace(/[^a-z]/gi, '').toLowerCase();
        return {
            isCurrent: !!current?.items.some((download) => download.id === event.context['download.id']),
            affectsSort: changedFields.some(
                (field) => field.replace(/[^a-z]/gi, '').toLowerCase() === normalizedSortBy,
            ),
            changesStatus: changesDownloadStatus(event),
        };
    };
    useDownloadEventInvalidation([downloadQueryKeys.list(params)], enabled, {
        failOpenEventNames: hasFilters ? ['download.created'] : undefined,
        prePredicate: (event) => {
            if (event.name !== 'download.updated') {
                return true;
            }
            const { isCurrent, affectsSort, changesStatus } = describeDownloadEvent(event);
            return isCurrent || affectsSort || changesStatus;
        },
        predicate: (event) => {
            const { isCurrent, affectsSort, changesStatus } = describeDownloadEvent(event);
            if (event.name === 'download.updated' && !changesStatus) {
                return isCurrent || affectsSort;
            }
            if (event.name === 'download.deleted') {
                return true;
            }
            if (!hasFilters || isCurrent) {
                return true;
            }
            return (
                (!params?.downloadClientId || event.context['downloadClient.id'] === params.downloadClientId) &&
                (!params?.releaseId || event.context['download.releaseId'] === params.releaseId) &&
                (!params?.status || event.context['download.status'] === params.status)
            );
        },
        unprovableMembership: hasFilters,
    });
    useInvalidateQueriesOnEvents(enabled ? CONTENT_LINEAGE_EVENTS : [], [downloadQueryKeys.list(params)], {
        debounceMs: 200,
        exact: true,
        maxWaitMs: 800,
        predicate: (event) => {
            const current = queryClient.getQueryData<PagedDownloads>(downloadQueryKeys.list(params));
            return !!current?.items.some(
                (download) => download.contentHierarchyItemId === event.context['content.hierarchyItemId'],
            );
        },
    });

    const downloadsQuery = useQuery({
        queryKey: downloadQueryKeys.list(params),
        queryFn: () => fetchDownloads(params),
        enabled,
    });

    const removeMutation = useMutation({
        mutationFn: removeDownload,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const deleteWithFilesMutation = useMutation({
        mutationFn: deleteDownloadWithFiles,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const pauseMutation = useMutation({
        mutationFn: pauseDownload,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const resumeMutation = useMutation({
        mutationFn: resumeDownload,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const forceStartMutation = useMutation({
        mutationFn: forceStartDownload,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const retryMutation = useMutation({
        mutationFn: retryDownload,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const cancelMutation = useMutation({
        mutationFn: cancelDownload,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });
        },
    });

    const invalidateAll = () => queryClient.invalidateQueries({ queryKey: downloadQueryKeys.all });

    const bulkForgetMutation = useMutation({ mutationFn: bulkForgetDownloads, onSuccess: invalidateAll });
    const bulkRemoveMutation = useMutation({ mutationFn: bulkRemoveDownloads, onSuccess: invalidateAll });
    const bulkPauseMutation = useMutation({ mutationFn: bulkPauseDownloads, onSuccess: invalidateAll });
    const bulkResumeMutation = useMutation({ mutationFn: bulkResumeDownloads, onSuccess: invalidateAll });
    const bulkStopMutation = useMutation({ mutationFn: bulkStopDownloads, onSuccess: invalidateAll });

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

        deleteDownloadWithFiles: deleteWithFilesMutation.mutateAsync,
        isDeletingWithFiles: deleteWithFilesMutation.isPending,

        pauseDownload: pauseMutation.mutateAsync,
        isPausing: pauseMutation.isPending,

        resumeDownload: resumeMutation.mutateAsync,
        isResuming: resumeMutation.isPending,

        forceStartDownload: forceStartMutation.mutateAsync,
        isForceStarting: forceStartMutation.isPending,

        retryDownload: retryMutation.mutateAsync,
        isRetrying: retryMutation.isPending,

        cancelDownload: cancelMutation.mutateAsync,
        isCancelling: cancelMutation.isPending,

        bulkForgetDownloads: bulkForgetMutation.mutateAsync,
        bulkRemoveDownloads: bulkRemoveMutation.mutateAsync,
        bulkPauseDownloads: bulkPauseMutation.mutateAsync,
        bulkResumeDownloads: bulkResumeMutation.mutateAsync,
        bulkStopDownloads: bulkStopMutation.mutateAsync,

        refetch: downloadsQuery.refetch,
    };
}

export function useDownload(id: string | null | undefined) {
    const enabled = !!id;
    const queryKey = downloadQueryKeys.detail(id ?? '');
    useDownloadEventInvalidation([queryKey], enabled, { entityIds: id ? [id] : [] });
    const downloadQuery = useQuery({
        queryKey,
        queryFn: () => fetchDownload(id!),
        enabled,
    });

    return {
        download: downloadQuery.data ?? null,
        isLoading: downloadQuery.isLoading,
        error: queryErrorOrNull(downloadQuery),
        refetch: downloadQuery.refetch,
    };
}

export function useDownloadStatusCounts(statuses: readonly DownloadStatusValue[], downloadClientId?: string) {
    useInvalidateQueriesOnEvents(
        statuses.length > 0 ? downloadEvents : [],
        statuses.map((status) => downloadQueryKeys.count(status, downloadClientId || undefined)),
        {
            debounceMs: 200,
            exact: true,
            failOpenEventNames: downloadClientId ? ['download.created'] : undefined,
            maxWaitMs: 800,
            prePredicate: (event) => event.name !== 'download.updated' || changesDownloadStatus(event),
            predicate: (event) =>
                (event.name !== 'download.updated' || changesDownloadStatus(event)) &&
                (!downloadClientId ||
                    event.name === 'download.deleted' ||
                    event.context['downloadClient.id'] === downloadClientId),
            unprovableMembership: !!downloadClientId,
        },
    );
    const queries = useQueries({
        queries: statuses.map((status) => ({
            queryKey: downloadQueryKeys.count(status, downloadClientId || undefined),
            queryFn: () =>
                fetchDownloads({
                    status,
                    downloadClientId: downloadClientId || undefined,
                    pageNumber: 1,
                    pageSize: 1,
                }),
        })),
    });

    const counts = {} as Record<DownloadStatusValue, number>;
    statuses.forEach((status, i) => {
        counts[status] = queries[i]?.data?.totalItems ?? 0;
    });

    const total = Object.values(counts).reduce((sum, n) => sum + n, 0);
    const error = queries.find((query) => query.error)?.error ?? null;

    return {
        counts,
        total,
        isLoading: queries.some((query) => query.isLoading),
        error,
        refetch: () => Promise.all(queries.map((query) => query.refetch())),
    };
}

export function useDownloadStatusTotal(
    statuses: readonly DownloadStatusValue[],
    downloadClientId?: string,
    enabled = true,
) {
    const params: DownloadQueryParams = {
        statuses,
        downloadClientId: downloadClientId || undefined,
        pageNumber: 1,
        pageSize: 1,
    };
    useDownloadEventInvalidation([downloadQueryKeys.list(params)], enabled, {
        predicate: (event) => event.name !== 'download.updated' || changesDownloadStatus(event),
    });
    const query = useQuery({
        queryKey: downloadQueryKeys.list(params),
        queryFn: () => fetchDownloads(params),
        enabled,
    });

    return {
        total: query.data?.totalItems ?? 0,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
    };
}

export function useLatestDownloadForRelease(releaseId: string | null | undefined) {
    const params: DownloadQueryParams = {
        releaseId: releaseId ?? undefined,
        pageNumber: 1,
        pageSize: 1,
        sortBy: 'CreatedAt',
        sortDirection: 'Descending',
    };

    const query = useQuery({
        queryKey: downloadQueryKeys.list(params),
        queryFn: () => fetchDownloads(params),
        enabled: !!releaseId,
    });
    const relatedIds = [releaseId, query.data?.items?.[0]?.id].filter((id): id is string => !!id);
    useDownloadEventInvalidation([downloadQueryKeys.list(params)], !!releaseId, { entityIds: relatedIds });

    return {
        download: query.data?.items?.[0] ?? null,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
    };
}
