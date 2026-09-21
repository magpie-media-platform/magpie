import type { ScalarPatch, DictionaryPatch } from '@/types/patch';
import { buildQueryKeys, useCrudResource } from './useCrudResource';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { healthStatusLabels, healthStatusVariants } from '@/types/healthStatus';
import type { HealthStatusValue } from '@/types/healthStatus';

export type DownloadClientStatusValue = HealthStatusValue;

export const downloadClientStatusLabels = healthStatusLabels;
export const downloadClientStatusVariants = healthStatusVariants;

export interface ApiDownloadClient {
    id: string;

    extensionId?: string | null;

    providerType: string;

    name: string;

    enabled: boolean;

    status: DownloadClientStatusValue;

    lastChecked?: string | null;

    cleanupDeleteFiles: boolean;

    keepFailedOnClient: boolean;

    settings?: Record<string, string>;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

interface DownloadClientPatch {
    name?: ScalarPatch<string>;
    enabled?: ScalarPatch<boolean>;
    cleanupDeleteFiles?: ScalarPatch<boolean>;
    keepFailedOnClient?: ScalarPatch<boolean>;
    settings?: DictionaryPatch<string, string>;
    tags?: {
        add?: string[];
        remove?: string[];
    };
}

interface DownloadClientCreate {
    providerType: string;

    name: string;

    enabled?: boolean;

    cleanupDeleteFiles?: boolean;

    keepFailedOnClient?: boolean;

    settings?: Record<string, string>;
}

export const downloadClientQueryKeys = buildQueryKeys('download-clients');

export function useDownloadClients() {
    useInvalidateQueriesOnEvents(
        ['integration.health.changed'],
        [[...downloadClientQueryKeys.list(), '/download-clients']],
        {
            context: { 'integration.type': 'downloadClient' },
            debounceMs: 150,
            exact: true,
            maxWaitMs: 600,
            owner: 'download-client-settings',
        },
    );
    const resource = useCrudResource<ApiDownloadClient, DownloadClientCreate, DownloadClientPatch>(
        {
            basePath: '/download-clients',
            eventNames: ['downloadClient.created', 'downloadClient.updated', 'downloadClient.deleted'],
        },
        downloadClientQueryKeys,
    );

    return {
        clients: resource.items,
        isLoading: resource.isLoading,
        error: resource.error,

        createClient: resource.create,
        isCreating: resource.isCreating,

        updateClient: resource.update,
        toggleClientEnabled: resource.toggleEnabled,
        isToggling: resource.isToggling,
        isUpdating: resource.isUpdating,

        deleteClient: resource.deleteItem,
        isDeleting: resource.isDeleting,

        refetch: resource.refetch,
    };
}
