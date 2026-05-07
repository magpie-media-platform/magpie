import { buildQueryKeys, useCrudResource } from './useCrudResource';
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

    settings: Record<string, string>;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface DownloadClientPatch {
    name?: { value: string };
    enabled?: { value: boolean };
    settings?: {
        add?: Record<string, string>;
        remove?: string[];
        update?: Record<string, string>;
    };
    tags?: {
        add?: string[];
        remove?: string[];
    };
}

export interface DownloadClientCreate {
    providerType: string;

    name: string;

    enabled?: boolean;

    settings?: Record<string, string>;
}

export const downloadClientQueryKeys = buildQueryKeys('download-clients');

export function useDownloadClients() {
    const resource = useCrudResource<ApiDownloadClient, DownloadClientCreate, DownloadClientPatch>(
        { basePath: '/download-clients' },
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
