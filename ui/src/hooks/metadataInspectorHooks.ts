import { useMutation, useQuery, useQueryClient, type QueryKey } from '@tanstack/react-query';

import { apiGet, apiPatch, buildUrl } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { ApiSubject } from '@/hooks/useSubjects';
import { extractImageUrl, extractTitle } from '@/lib/metadata';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import type {
    MaterializedMetadata,
    MetadataContainerPatch,
    MetadataContribution,
    MetadataInspectorResponse,
    SubjectRef,
} from '@/types';
import type { PagedList } from '@/types/paging';

type SubjectRefResolver = (containerIds: Set<string>, id: string) => Promise<Record<string, SubjectRef>>;

interface MetadataInspectorHooksConfig {
    basePath: string;
    inspectorKey: (id: string) => QueryKey;
    detailKey: (id: string) => QueryKey;
    resolveSubjectRefs: SubjectRefResolver;
}

const SUBJECT_PAGE_SIZE = 500;
const RELATED_CONTENT_PAGE_SIZE = 100;

function relatedContainerIds(materialized: MaterializedMetadata, contributions: MetadataContribution[]): Set<string> {
    const ids = new Set<string>();
    for (const relationship of materialized.relationships ?? []) {
        if (relationship.relatedMetadataContainerId) {
            ids.add(relationship.relatedMetadataContainerId);
        }
    }
    for (const contribution of contributions) {
        for (const relationship of contribution.relationships ?? []) {
            if (relationship.relatedMetadataContainerId) {
                ids.add(relationship.relatedMetadataContainerId);
            }
        }
    }
    return ids;
}

async function collectSubjectRefs(
    containerIds: Set<string>,
    pageParams: (pageNumber: number) => Record<string, unknown>,
): Promise<Record<string, SubjectRef>> {
    const subjects: Record<string, SubjectRef> = {};
    if (containerIds.size === 0) {
        return subjects;
    }

    const unresolved = new Set(containerIds);
    let pageNumber = 1;
    let loaded = 0;

    while (unresolved.size > 0) {
        const page = await apiGet<PagedList<ApiSubject>>(buildUrl('/subjects', pageParams(pageNumber)));
        for (const subject of page.items) {
            if (!unresolved.has(subject.metadataContainerId)) {
                continue;
            }
            subjects[subject.metadataContainerId] = {
                subjectId: subject.id,
                name: extractTitle(subject.metadataContainer) ?? 'Unknown',
                imageUrl: extractImageUrl(subject.metadataContainer) ?? null,
            };
            unresolved.delete(subject.metadataContainerId);
        }

        loaded += page.items.length;
        if (page.items.length === 0 || loaded >= page.totalItems) {
            break;
        }
        pageNumber += 1;
    }

    return subjects;
}

export const scanSubjectRefs: SubjectRefResolver = (containerIds) =>
    collectSubjectRefs(containerIds, (pageNumber) => ({ pageNumber, pageSize: SUBJECT_PAGE_SIZE }));

export const relatedContentSubjectRefs: SubjectRefResolver = (containerIds, id) =>
    collectSubjectRefs(containerIds, (pageNumber) => ({
        relatedContentId: id,
        pageNumber,
        pageSize: RELATED_CONTENT_PAGE_SIZE,
    }));

async function fetchMetadataInspector(
    basePath: string,
    resolveSubjectRefs: SubjectRefResolver,
    id: string,
): Promise<MetadataInspectorResponse> {
    const [materialized, contributions] = await Promise.all([
        apiGet<MaterializedMetadata>(`${basePath}/${id}/metadata`),
        apiGet<MetadataContribution[]>(`${basePath}/${id}/metadata/contributions`),
    ]);
    const subjects = await resolveSubjectRefs(relatedContainerIds(materialized, contributions), id);

    return { materialized, contributions, subjects };
}

export function createMetadataInspectorHooks(config: MetadataInspectorHooksConfig) {
    const { basePath, inspectorKey, detailKey, resolveSubjectRefs } = config;

    const patchInspector = (id: string, patch: MetadataContainerPatch): Promise<void> =>
        apiPatch(`${basePath}/${id}/metadata`, patch);

    const reorderContributions = (id: string, contributionIds: string[]): Promise<void> =>
        apiPatch(`${basePath}/${id}/metadata`, {
            contributionIds,
        } satisfies MetadataContainerPatch);

    function useMetadataInspector(id: string | undefined, options?: { enabled?: boolean }) {
        const queryKey = [...inspectorKey(id ?? ''), basePath, resolveSubjectRefs] as const;
        const entityEvents =
            basePath === '/subjects'
                ? ['subject.updated', 'subject.deleted']
                : ['content.hierarchyItem.updated', 'content.hierarchyItem.deleted', 'metadata.refreshed'];
        useInvalidateQueriesOnEvents((options?.enabled ?? true) && id ? entityEvents : [], [queryKey], {
            entityIds: id ? [id] : [],
            exact: true,
            owner: 'metadata-inspector',
        });
        useInvalidateQueriesOnEvents(
            (options?.enabled ?? true) && id && basePath === '/subjects' ? ['job.execution.completed'] : [],
            [queryKey],
            {
                entityIds: id ? [id] : [],
                exact: true,
                owner: 'metadata-inspector',
                predicate: (event) => event.context['execution.jobType'] === 'SubjectMetadataRefreshItem',
            },
        );

        return useQuery({
            queryKey,
            queryFn: () => fetchMetadataInspector(basePath, resolveSubjectRefs, id!),
            enabled: (options?.enabled ?? true) && !!id,
            staleTime: POLLING_INTERVALS.slow,
        });
    }

    function usePatchMetadataInspector(id: string | undefined) {
        const queryClient = useQueryClient();
        return useMutation({
            mutationFn: (patch: MetadataContainerPatch) => patchInspector(id!, patch),
            onSuccess: () => {
                void queryClient.invalidateQueries({ queryKey: inspectorKey(id ?? '') });
                void queryClient.invalidateQueries({ queryKey: detailKey(id ?? '') });
            },
        });
    }

    function useReorderMetadataContributions(id: string | undefined) {
        const queryClient = useQueryClient();
        return useMutation({
            mutationFn: (contributionIds: string[]) => reorderContributions(id!, contributionIds),
            onSuccess: async () => {
                await Promise.all([
                    queryClient.invalidateQueries({ queryKey: inspectorKey(id ?? '') }),
                    queryClient.invalidateQueries({ queryKey: detailKey(id ?? '') }),
                ]);
            },
        });
    }

    return {
        useMetadataInspector,
        usePatchMetadataInspector,
        useReorderMetadataContributions,
    };
}
