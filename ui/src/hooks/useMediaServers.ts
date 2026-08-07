import { buildQueryKeys, useCrudResource } from './useCrudResource';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
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

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    settings?: Record<string, string>;
}

interface MediaServerPatch {
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

interface MediaServerCreate {
    providerType: string;

    name: string;

    enabled?: boolean;

    settings?: Record<string, string>;
}

const mediaServerQueryKeys = buildQueryKeys('media-servers');

export function useMediaServers() {
    useInvalidateQueriesOnEvents(['integration.health.changed'], [[...mediaServerQueryKeys.list(), '/media-servers']], {
        context: { 'integration.type': 'mediaServer' },
        debounceMs: 150,
        exact: true,
        maxWaitMs: 600,
        owner: 'media-server-settings',
    });
    const resource = useCrudResource<ApiMediaServer, MediaServerCreate, MediaServerPatch>(
        {
            basePath: '/media-servers',
            eventNames: ['mediaServer.created', 'mediaServer.updated', 'mediaServer.deleted'],
        },
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
