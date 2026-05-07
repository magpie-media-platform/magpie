import { buildQueryKeys, useCrudResource } from './useCrudResource';
import { healthStatusLabels, healthStatusVariants } from '@/types/healthStatus';
import type { HealthStatusValue } from '@/types/healthStatus';

export type MediaServerStatusValue = HealthStatusValue;

export const mediaServerStatusLabels = healthStatusLabels;
export const mediaServerStatusVariants = healthStatusVariants;

export interface ApiMediaServer {
    id: string;

    extensionId?: string | null;

    providerType: string;

    name: string;

    enabled: boolean;

    status: MediaServerStatusValue;

    lastChecked?: string | null;

    settings: Record<string, string>;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface MediaServerPatch {
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

export interface MediaServerCreate {
    providerType: string;

    name: string;

    enabled?: boolean;

    settings?: Record<string, string>;
}

export const mediaServerQueryKeys = buildQueryKeys('media-servers');

export function useMediaServers() {
    const resource = useCrudResource<ApiMediaServer, MediaServerCreate, MediaServerPatch>(
        { basePath: '/media-servers' },
        mediaServerQueryKeys,
    );

    return {
        servers: resource.items,
        isLoading: resource.isLoading,
        error: resource.error,

        createServer: resource.create,
        isCreating: resource.isCreating,

        updateServer: resource.update,
        toggleServerEnabled: resource.toggleEnabled,
        isToggling: resource.isToggling,
        isUpdating: resource.isUpdating,

        deleteServer: resource.deleteItem,
        isDeleting: resource.isDeleting,

        refetch: resource.refetch,
    };
}
