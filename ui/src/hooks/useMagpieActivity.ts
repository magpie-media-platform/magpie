import { useQuery } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';
import type { PagedList } from '@/types/paging';

export const MagpieActivityEntryKind = {
    JobExecution: 'JobExecution',
    AuditRecord: 'AuditRecord',
} as const;

type MagpieActivityEntryKindValue = (typeof MagpieActivityEntryKind)[keyof typeof MagpieActivityEntryKind];

export interface MagpieActivityEntry {
    id: string;
    kind: MagpieActivityEntryKindValue;
    timestamp: string;
    correlationId?: string | null;
    label: string;
    category: string;
    status?: string | null;
    detail?: string | null;
    entityId?: string | null;
    entityType?: string | null;
    userId?: string | null;
}

interface MagpieActivityResponse {
    entries: MagpieActivityEntry[];
    correlationIds: string[];
    totalCount: number;
    capped: boolean;
    page: number;
    pageSize: number;
}

interface MagpieActivityQueryParams {
    kind?: MagpieActivityEntryKindValue;
    status?: string;
    jobType?: string;
    operation?: string;
    entityId?: string;
    from?: string;
    to?: string;
    pageNumber?: number;
    pageSize?: number;
}

const magpieActivityQueryKeys = {
    forContent: (contentId: string, params?: MagpieActivityQueryParams) =>
        ['magpie-activity', 'content', contentId, params ?? {}] as const,
    forExecutionTree: (executionId: string) => ['magpie-activity', 'execution-tree', executionId] as const,
};

const ACTIVITY_APPEND_EVENTS = [
    'audit.record.created',
    'job.execution.created',
    'job.execution.queued',
    'job.execution.started',
    'job.execution.progress.changed',
    'job.execution.completed',
    'job.execution.failed',
    'job.execution.cancellationRequested',
    'job.execution.cancelled',
] as const;

type JobExecutionTreeNode = {
    id: string;
    correlationId?: string | null;
    jobType: string;
    status: string;
    createdAt: string;
    startedAt?: string | null;
    completedAt?: string | null;
    children: JobExecutionTreeNode[];
};

type JobExecutionTree = {
    root: JobExecutionTreeNode | null;
    totalCount: number;
};

function toMagpieActivityResponse(page: PagedList<MagpieActivityEntry>): MagpieActivityResponse {
    return {
        entries: page.items,
        correlationIds: page.items.flatMap((entry) => (entry.correlationId ? [entry.correlationId] : [])),
        totalCount: page.totalItems,
        capped: false,
        page: page.pageNumber ?? 1,
        pageSize: page.pageSize ?? page.items.length,
    };
}

async function fetchContentMagpieActivity(
    contentId: string,
    params: MagpieActivityQueryParams,
): Promise<MagpieActivityResponse> {
    const url = buildUrl('/audit-history', {
        contentId,
        kind: params.kind,
        status: params.status,
        from: params.from,
        to: params.to,
        pageNumber: params.pageNumber,
        pageSize: params.pageSize,
    });
    return apiGet<PagedList<MagpieActivityEntry>>(url).then(toMagpieActivityResponse);
}

function toMagpieActivityEntry(node: JobExecutionTreeNode): MagpieActivityEntry {
    return {
        id: node.id,
        kind: MagpieActivityEntryKind.JobExecution,
        timestamp: node.completedAt ?? node.startedAt ?? node.createdAt,
        correlationId: node.correlationId,
        label: node.jobType,
        category: 'Job',
        status: node.status,
        detail: null,
    };
}

function toExecutionTreeMagpieActivityResponse(tree: JobExecutionTree): MagpieActivityResponse {
    const entries: MagpieActivityEntry[] = [];
    const nodes = tree.root ? [tree.root] : [];

    while (nodes.length > 0) {
        const node = nodes.pop();
        if (!node) {
            continue;
        }

        entries.push(toMagpieActivityEntry(node));
        nodes.push(...node.children);
    }

    return {
        entries,
        correlationIds: entries.flatMap((entry) => (entry.correlationId ? [entry.correlationId] : [])),
        totalCount: tree.totalCount,
        capped: false,
        page: 1,
        pageSize: entries.length,
    };
}

async function fetchJobExecutionTreeActivity(executionId: string): Promise<MagpieActivityResponse> {
    const url = buildUrl(`/jobs/${executionId}/execution-tree`, { 'include-siblings': true });
    return apiGet<JobExecutionTree>(url).then(toExecutionTreeMagpieActivityResponse);
}

export function useContentMagpieActivity(contentId: string | null | undefined, params: MagpieActivityQueryParams = {}) {
    const query = useQuery({
        queryKey: magpieActivityQueryKeys.forContent(contentId ?? '', params),
        queryFn: () => fetchContentMagpieActivity(contentId!, params),
        enabled: !!contentId,
    });
    const relatedIds = [
        contentId,
        ...(query.data?.entries.flatMap((entry) => [entry.id, entry.entityId]) ?? []),
    ].filter((id): id is string => !!id);

    useInvalidateQueriesOnEvents(
        contentId ? ACTIVITY_APPEND_EVENTS : [],
        [magpieActivityQueryKeys.forContent(contentId ?? '', params)],
        {
            debounceMs: 200,
            entityIds: relatedIds,
            exact: true,
            maxWaitMs: 800,
            owner: 'activity:content',
        },
    );

    return {
        activity: query.data ?? null,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}

export function useJobExecutionTreeActivity(executionId: string | null | undefined) {
    const query = useQuery({
        queryKey: magpieActivityQueryKeys.forExecutionTree(executionId ?? ''),
        queryFn: () => fetchJobExecutionTreeActivity(executionId!),
        enabled: !!executionId,
    });
    const executionIds = [executionId, ...(query.data?.entries.map((entry) => entry.id) ?? [])].filter(
        (id): id is string => !!id,
    );

    useInvalidateQueriesOnEvents(
        executionId ? ACTIVITY_APPEND_EVENTS : [],
        [magpieActivityQueryKeys.forExecutionTree(executionId ?? '')],
        {
            debounceMs: 150,
            entityIds: executionIds,
            exact: true,
            maxWaitMs: 600,
            owner: 'activity:execution-tree',
        },
    );

    return {
        activity: query.data ?? null,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}
