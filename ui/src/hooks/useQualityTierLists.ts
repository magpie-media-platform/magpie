import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface StringPropertyCriterion {
    operator?: CriteriaValueOperator;
    value?: string | null;
    values?: string[];
}

export type CriteriaValueOperator = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15;

export interface DictionaryCriteria {
    items?: Record<string, StringPropertyCriterion>;
}

export interface QualityTierCriteria {
    and?: DictionaryCriteria[];
}

export interface ApiQualityTier {
    id: string;

    name: string;

    criteria: QualityTierCriteria;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface ApiQualityTierListRank {
    qualityTierListId: string;

    qualityTierId: string;

    ordinal: number;

    isCutoff: boolean;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    qualityTier?: ApiQualityTier | null;
}

export interface ApiQualityTierList {
    id: string;

    name: string;

    upgradeAllowed: boolean;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    qualityTierRanks: ApiQualityTierListRank[];
}

export interface QualityTierListRankCreate {
    qualityTierId: string;

    ordinal: number;

    isCutoff: boolean;
}

export interface QualityTierListRankPatch {
    qualityTierId: { value: string };

    ordinal?: { value: number };

    isCutoff?: { value: boolean };
}

export interface QualityTierListCreate {
    name: string;

    upgradeAllowed?: boolean;

    qualityTierRanks?: QualityTierListRankCreate[];
}

export interface QualityTierListPatch {
    name?: { value: string };
    upgradeAllowed?: { value: boolean };
    qualityTierRanks?: {
        add?: QualityTierListRankCreate[];
        remove?: string[];
        update?: QualityTierListRankPatch[];
    };
    tags?: {
        add?: string[];
        remove?: string[];
    };
}

export interface QualityTierCreate {
    name: string;

    criteria: QualityTierCriteria;
}

export interface QualityTierPatch {
    name?: { value: string };
    criteria?: { value: QualityTierCriteria };
}

export const qualityTierListQueryKeys = {
    all: ['quality-tier-lists'] as const,
    list: (tag?: string) =>
        tag ? (['quality-tier-lists', 'list', { tag }] as const) : (['quality-tier-lists', 'list'] as const),
    detail: (id: string) => ['quality-tier-lists', 'detail', id] as const,
};

async function fetchQualityTierLists(tag?: string): Promise<ApiQualityTierList[]> {
    const path = tag ? `/quality-tier-lists?tag=${encodeURIComponent(tag)}` : '/quality-tier-lists';
    return apiGet<ApiQualityTierList[]>(path);
}

async function fetchQualityTierListById(id: string): Promise<ApiQualityTierList> {
    return apiGet<ApiQualityTierList>(`/quality-tier-lists/${id}`);
}

async function createQualityTierList(data: QualityTierListCreate): Promise<ApiQualityTierList> {
    return apiPost<ApiQualityTierList>('/quality-tier-lists', data);
}

async function patchQualityTierList(id: string, patch: QualityTierListPatch): Promise<void> {
    return apiPatch(`/quality-tier-lists/${id}`, patch);
}

async function deleteQualityTierList(id: string): Promise<void> {
    return apiDelete(`/quality-tier-lists/${id}`);
}

async function createQualityTier(data: QualityTierCreate): Promise<ApiQualityTier> {
    return apiPost<ApiQualityTier>('/quality-tiers', data);
}

async function patchQualityTier(id: string, patch: QualityTierPatch): Promise<void> {
    return apiPatch(`/quality-tiers/${id}`, patch);
}

async function deleteQualityTier(id: string): Promise<void> {
    return apiDelete(`/quality-tiers/${id}`);
}

export function useQualityTierLists(options?: { tag?: string }) {
    const queryClient = useQueryClient();
    const tag = options?.tag;

    const tierListsQuery = useQuery({
        queryKey: qualityTierListQueryKeys.list(tag),
        queryFn: () => fetchQualityTierLists(tag),
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const createMutation = useMutation({
        mutationFn: createQualityTierList,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
        },
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: QualityTierListPatch }) => patchQualityTierList(id, patch),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteQualityTierList,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
        },
    });

    const tierLists: ApiQualityTierList[] = tierListsQuery.data ?? [];

    const updateTierList = async (id: string, patch: QualityTierListPatch): Promise<void> => {
        await patchMutation.mutateAsync({ id, patch });
    };

    return {
        tierLists,
        isLoading: tierListsQuery.isLoading,
        error: queryErrorOrNull(tierListsQuery),

        createTierList: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        updateTierList,
        isUpdating: patchMutation.isPending,

        deleteTierList: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: tierListsQuery.refetch,
    };
}

export function useQualityTierList(id: string | undefined) {
    return useQuery({
        queryKey: qualityTierListQueryKeys.detail(id ?? ''),
        queryFn: () => fetchQualityTierListById(id!),
        enabled: !!id,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

export function useQualityTiers() {
    const queryClient = useQueryClient();

    const invalidateAll = () => {
        queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
    };

    const createMutation = useMutation({
        mutationFn: createQualityTier,
        onSuccess: invalidateAll,
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: QualityTierPatch }) => patchQualityTier(id, patch),
        onSuccess: invalidateAll,
    });

    const deleteMutation = useMutation({
        mutationFn: deleteQualityTier,
        onSuccess: invalidateAll,
    });

    const updateTier = async (id: string, patch: QualityTierPatch): Promise<void> => {
        await patchMutation.mutateAsync({ id, patch });
    };

    return {
        createTier: createMutation.mutateAsync,
        isCreating: createMutation.isPending,
        updateTier,
        isUpdating: patchMutation.isPending,
        deleteTier: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,
    };
}
