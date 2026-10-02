import type { ScalarPatch } from '@/types/patch';
import { useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';

export type PackageSourceSearchFilterKind = 'Tag' | 'Prefix' | 'None';

export type PackageSourceCredentialKind = 'None' | 'Basic' | 'SecretReference';

export interface PackageSource {
    id: string;
    name: string;
    sourceUrl: string;
    enabled: boolean;
    searchFilterKind: PackageSourceSearchFilterKind;
    searchFilterValue: string | null;
    credentialKind: PackageSourceCredentialKind;
    username: string | null;
    secretReference: string | null;
    hasSecret: boolean;
    createdAt: string;
    modifiedAt: string | null;
}

interface CreatePackageSourceInput {
    name: string;
    sourceUrl: string;
    enabled?: boolean;
    searchFilterKind?: PackageSourceSearchFilterKind;
    searchFilterValue?: string | null;
    credentialKind?: PackageSourceCredentialKind;
    username?: string | null;
    secret?: string | null;
    secretReference?: string | null;
}

interface PatchPackageSourceInput {
    name?: ScalarPatch<string>;
    sourceUrl?: ScalarPatch<string>;
    enabled?: ScalarPatch<boolean>;
    searchFilterKind?: ScalarPatch<PackageSourceSearchFilterKind>;
    searchFilterValue?: ScalarPatch<string | null>;
    credentialKind?: ScalarPatch<PackageSourceCredentialKind>;
    username?: ScalarPatch<string | null>;
    encryptedSecret?: ScalarPatch<string | null>;
    secretReference?: ScalarPatch<string | null>;
}

const packageSourceQueryKeys = {
    all: ['extension-package-sources'] as const,
    list: () => ['extension-package-sources', 'list'] as const,
};

async function fetchPackageSources(): Promise<PackageSource[]> {
    return apiGet<PackageSource[]>('/extensions/package-sources');
}

async function createPackageSource(input: CreatePackageSourceInput): Promise<PackageSource> {
    return apiPost<PackageSource>('/extensions/package-sources', input);
}

async function patchPackageSource(id: string, patch: PatchPackageSourceInput): Promise<void> {
    return apiPatch(`/extensions/package-sources/${id}`, patch);
}

async function setPackageSourceEnabled(id: string, enabled: boolean): Promise<void> {
    return patchPackageSource(id, { enabled: { value: enabled } });
}

async function deletePackageSource(id: string): Promise<void> {
    return apiDelete(`/extensions/package-sources/${id}`);
}

export function usePackageSources() {
    useInvalidateQueriesOnEvents(
        ['extensionPackageSource.created', 'extensionPackageSource.updated', 'extensionPackageSource.deleted'],
        [packageSourceQueryKeys.list()],
        { exact: true },
    );
    const queryClient = useQueryClient();

    const sourcesQuery = useQuery({
        queryKey: packageSourceQueryKeys.list(),
        queryFn: fetchPackageSources,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const invalidate = () => {
        void queryClient.invalidateQueries({ queryKey: packageSourceQueryKeys.all });
    };

    const createMutation = useMutation({ mutationFn: createPackageSource, onSuccess: invalidate });
    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: PatchPackageSourceInput }) => patchPackageSource(id, patch),
        onSuccess: invalidate,
    });
    const enabledMutation = useMutation({
        mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => setPackageSourceEnabled(id, enabled),
        onSuccess: invalidate,
    });
    const deleteMutation = useMutation({ mutationFn: deletePackageSource, onSuccess: invalidate });

    const sources: PackageSource[] = useMemo(() => sourcesQuery.data ?? [], [sourcesQuery.data]);

    return {
        sources,
        isLoading: sourcesQuery.isLoading,
        error: queryErrorOrNull(sourcesQuery),
        refetch: sourcesQuery.refetch,

        createSource: (input: CreatePackageSourceInput) => createMutation.mutateAsync(input),
        isCreating: createMutation.isPending,

        patchSource: (id: string, patch: PatchPackageSourceInput) => patchMutation.mutateAsync({ id, patch }),
        isPatching: patchMutation.isPending,

        setEnabled: (id: string, enabled: boolean) => enabledMutation.mutateAsync({ id, enabled }),
        isSettingEnabled: enabledMutation.isPending,

        deleteSource: (id: string) => deleteMutation.mutateAsync(id),
        isDeleting: deleteMutation.isPending,
    };
}
