import type { ScalarPatch } from '@/types/patch';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';
import { tagQueryKeys } from '@/hooks/useTags';

export interface StringPropertyCriterion {
    operator?: CriteriaValueOperator;
    value?: string | null;
    values?: string[];
}

export type CriteriaValueOperator =
    | 'Equals'
    | 'NotEquals'
    | 'IsNull'
    | 'IsNotNull'
    | 'GreaterThan'
    | 'GreaterThanOrEqual'
    | 'LessThan'
    | 'LessThanOrEqual'
    | 'In'
    | 'NotIn'
    | 'ContainsString'
    | 'NotContainsString'
    | 'StartsWithString'
    | 'NotStartsWithString'
    | 'EndsWithString'
    | 'NotEndsWithString'
    | 'ContainsWord'
    | 'Regex';

export interface DictionaryCriteria {
    items?: Record<string, StringPropertyCriterion>;
}

export interface QualityTierCriteria {
    and?: DictionaryCriteria[];
}

type RuleKind = 'Accept' | 'Exclude';

export interface ApiQualityTierRule {
    id: string;

    name?: string | null;

    kind: RuleKind;

    criteria: QualityTierCriteria;
}

interface ApiQualityTier {
    id: string;

    name: string;

    criteria: QualityTierCriteria;

    rules?: ApiQualityTierRule[];

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

    mimeTypes: string[];

    upgradeAllowed: boolean;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    qualityTierRanks: ApiQualityTierListRank[];

    rules?: ApiQualityTierRule[];
}

interface QualityTierListRankCreate {
    qualityTierId: string;

    ordinal: number;

    isCutoff: boolean;
}

export interface QualityTierListRankPatch {
    qualityTierId: ScalarPatch<string>;

    ordinal?: ScalarPatch<number>;

    isCutoff?: ScalarPatch<boolean>;
}

interface QualityTierListCreate {
    name: string;

    mimeTypes?: string[];

    upgradeAllowed?: boolean;

    rules?: ApiQualityTierRule[];

    qualityTierRanks?: QualityTierListRankCreate[];
}

export interface QualityTierListPatch {
    name?: ScalarPatch<string>;
    mimeTypes?: ScalarPatch<string[]>;
    upgradeAllowed?: ScalarPatch<boolean>;
    qualityTierRanks?: {
        add?: QualityTierListRankCreate[];
        remove?: string[];
        update?: QualityTierListRankPatch[];
    };
    rules?: ScalarPatch<ApiQualityTierRule[]>;
    tags?: {
        add?: string[];
        remove?: string[];
    };
}

interface QualityTierCreate {
    name: string;

    criteria: QualityTierCriteria;

    rules?: ApiQualityTierRule[];
}

interface QualityTierPatch {
    name?: ScalarPatch<string>;
    criteria?: ScalarPatch<QualityTierCriteria>;
    rules?: ScalarPatch<ApiQualityTierRule[]>;
}

export const qualityTierListQueryKeys = {
    all: ['quality-tier-lists'] as const,
    list: (options?: QualityTierListQueryOptions) =>
        options?.tag || options?.contentType || options?.level
            ? ([
                  'quality-tier-lists',
                  'list',
                  { tag: options.tag, contentType: options.contentType, level: options.level },
              ] as const)
            : (['quality-tier-lists', 'list'] as const),
    detail: (id: string) => ['quality-tier-lists', 'detail', id] as const,
};

interface QualityTierListQueryOptions {
    tag?: string;
    contentType?: string;
    level?: string;
    enabled?: boolean;
}

export async function fetchQualityTierLists(options?: QualityTierListQueryOptions): Promise<ApiQualityTierList[]> {
    return apiGet<ApiQualityTierList[]>(
        buildUrl('/quality-tier-lists', {
            tag: options?.tag,
            contentType: options?.contentType,
            level: options?.level,
        }),
    );
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

export function useQualityTierLists(options?: QualityTierListQueryOptions) {
    useInvalidateQueriesOnEvents(
        ['qualityTierList.created', 'qualityTierList.updated', 'qualityTierList.deleted'],
        [qualityTierListQueryKeys.list(options)],
        { exact: true, owner: 'quality-tier-lists' },
    );
    const queryClient = useQueryClient();
    const tierListsQuery = useQuery({
        queryKey: qualityTierListQueryKeys.list(options),
        queryFn: () => fetchQualityTierLists(options),
        enabled: options?.enabled ?? true,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const createMutation = useMutation({
        mutationFn: createQualityTierList,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
        },
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: QualityTierListPatch }) => patchQualityTierList(id, patch),
        onSuccess: (_data, { id }) => {
            void queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.byEntity(id) });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteQualityTierList,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
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
    useInvalidateQueriesOnEvents(
        ['qualityTierList.created', 'qualityTierList.updated', 'qualityTierList.deleted'],
        [qualityTierListQueryKeys.detail(id ?? '')],
        { entityIds: id ? [id] : [], exact: true, owner: 'quality-tier-list-detail' },
    );
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
        void queryClient.invalidateQueries({ queryKey: qualityTierListQueryKeys.all });
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
