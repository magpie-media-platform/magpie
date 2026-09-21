import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPut, apiDelete } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface BlockedFileType {
    id: string;
    pattern: string;
    createdAt: string;
    modifiedAt?: string | null;
    createdByUserId?: string | null;
    modifiedByUserId?: string | null;
}

interface CreateBlockedFileTypeInput {
    pattern: string;
}

export interface MimeMapping {
    extension: string;
    mimeType: string;
    createdAt: string;
    updatedAt?: string | null;
}

interface MimeMappingInput {
    extension: string;
    mimeType: string;
}

const fileTypesQueryKeys = {
    denyList: () => ['file-types', 'deny-list'] as const,
    mimeMappings: () => ['file-types', 'mime-mappings'] as const,
};

const BLOCKED_FILE_TYPE_EVENTS = ['blockedFileType.created', 'blockedFileType.deleted'] as const;
const MIME_MAPPING_EVENTS = ['mimeMapping.created', 'mimeMapping.updated', 'mimeMapping.deleted'] as const;

async function fetchDenyList(): Promise<BlockedFileType[]> {
    return apiGet<BlockedFileType[]>('/system/file-types/exclusions');
}

async function fetchMimeMappings(): Promise<MimeMapping[]> {
    return apiGet<MimeMapping[]>('/system/file-types/mime');
}

async function createDenyListEntry(input: CreateBlockedFileTypeInput): Promise<BlockedFileType> {
    return apiPost<BlockedFileType>('/system/file-types/exclusions', JSON.stringify(input.pattern));
}

async function deleteDenyListEntry(id: string): Promise<void> {
    return apiDelete(`/system/file-types/exclusions/${id}`);
}

async function saveMimeMapping(input: MimeMappingInput): Promise<MimeMapping> {
    return apiPut<MimeMapping>(`/system/file-types/mime/${encodeURIComponent(input.extension)}`, {
        mimeType: input.mimeType,
    });
}

async function deleteMimeMapping(extension: string): Promise<void> {
    return apiDelete(`/system/file-types/mime/${encodeURIComponent(extension)}`);
}

export function useDenyList() {
    useInvalidateQueriesOnEvents(BLOCKED_FILE_TYPE_EVENTS, [fileTypesQueryKeys.denyList()], {
        exact: true,
        owner: 'file-types:deny-list',
    });
    const queryClient = useQueryClient();

    const listQuery = useQuery({
        queryKey: fileTypesQueryKeys.denyList(),
        queryFn: fetchDenyList,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createDenyListEntry,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: fileTypesQueryKeys.denyList() });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteDenyListEntry,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: fileTypesQueryKeys.denyList() });
        },
    });

    return {
        entries: listQuery.data ?? [],
        isLoading: listQuery.isLoading,
        error: queryErrorOrNull(listQuery),
        refetch: listQuery.refetch,

        createEntry: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        deleteEntry: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,
    };
}

export function useMimeMappings() {
    useInvalidateQueriesOnEvents(MIME_MAPPING_EVENTS, [fileTypesQueryKeys.mimeMappings()], {
        exact: true,
        owner: 'file-types:mime-mappings',
    });
    const queryClient = useQueryClient();

    const listQuery = useQuery({
        queryKey: fileTypesQueryKeys.mimeMappings(),
        queryFn: fetchMimeMappings,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const saveMutation = useMutation({
        mutationFn: saveMimeMapping,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: fileTypesQueryKeys.mimeMappings() });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteMimeMapping,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: fileTypesQueryKeys.mimeMappings() });
        },
    });

    return {
        mappings: listQuery.data ?? [],
        isLoading: listQuery.isLoading,
        error: queryErrorOrNull(listQuery),
        refetch: listQuery.refetch,

        saveMapping: saveMutation.mutateAsync,
        isSaving: saveMutation.isPending,

        deleteMapping: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,
    };
}
