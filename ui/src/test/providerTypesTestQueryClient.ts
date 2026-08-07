import { QueryClient } from '@tanstack/react-query';

import { providerTypeQueryKeys } from '@/hooks/useProviderTypes';
import { providerSchemaQueryKeys, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

export interface SeededProviderTypes {
    downloadClients?: string[];
    mediaServers?: string[];
    indexers?: string[];
    indexerFeeds?: string[];
    externalUsers?: string[];

    schemas?: {
        downloadClients?: Record<string, IntegrationSettingDefinition[]>;
        mediaServers?: Record<string, IntegrationSettingDefinition[]>;
        indexers?: Record<string, IntegrationSettingDefinition[]>;
        indexerFeeds?: Record<string, IntegrationSettingDefinition[]>;
    };
}

export function createTestQueryClient(seed: SeededProviderTypes = {}): QueryClient {
    const client = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: Infinity,
                staleTime: Infinity,
            },
        },
    });

    if (seed.downloadClients) {
        client.setQueryData(providerTypeQueryKeys.downloadClients, seed.downloadClients);
    }
    if (seed.mediaServers) {
        client.setQueryData(providerTypeQueryKeys.mediaServers, seed.mediaServers);
    }
    if (seed.indexers) {
        client.setQueryData(providerTypeQueryKeys.indexers, seed.indexers);
    }
    if (seed.indexerFeeds) {
        client.setQueryData(providerTypeQueryKeys.indexerFeeds, seed.indexerFeeds);
    }
    if (seed.externalUsers) {
        client.setQueryData(providerTypeQueryKeys.externalUsers, seed.externalUsers);
    }

    if (seed.schemas?.downloadClients) {
        for (const [providerType, schema] of Object.entries(seed.schemas.downloadClients)) {
            client.setQueryData(providerSchemaQueryKeys.downloadClients(providerType), schema);
        }
    }
    if (seed.schemas?.mediaServers) {
        for (const [providerType, schema] of Object.entries(seed.schemas.mediaServers)) {
            client.setQueryData(providerSchemaQueryKeys.mediaServers(providerType), schema);
        }
    }
    if (seed.schemas?.indexers) {
        for (const [providerType, schema] of Object.entries(seed.schemas.indexers)) {
            client.setQueryData(providerSchemaQueryKeys.indexers(providerType), schema);
        }
    }
    if (seed.schemas?.indexerFeeds) {
        for (const [providerType, schema] of Object.entries(seed.schemas.indexerFeeds)) {
            client.setQueryData(providerSchemaQueryKeys.indexerFeeds(providerType), schema);
        }
    }

    return client;
}
