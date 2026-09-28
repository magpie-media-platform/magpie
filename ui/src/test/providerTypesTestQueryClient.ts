import { QueryClient } from '@tanstack/react-query';

import { providerCatalogQueryKey, type ProviderDescriptor } from '@/hooks/useProviderCatalog';
import { type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

export interface SeededProviderTypes {
    downloadClients?: string[];
    mediaServers?: string[];
    indexers?: string[];
    indexerFeeds?: string[];

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

    const providers: ProviderDescriptor[] = [];
    const kinds = {
        downloadClients: 'downloadClient',
        mediaServers: 'mediaServer',
        indexers: 'indexer',
        indexerFeeds: 'indexerFeed',
    } as const;
    for (const key of Object.keys(kinds) as (keyof typeof kinds)[]) {
        for (const providerType of new Set([...(seed[key] ?? []), ...Object.keys(seed.schemas?.[key] ?? {})])) {
            providers.push({
                kind: kinds[key],
                providerType,
                displayName: providerType,
                schema: seed.schemas?.[key]?.[providerType] ?? [],
            });
        }
    }
    if (providers.length > 0 || Object.keys(kinds).some((key) => key in seed)) {
        client.setQueryData(providerCatalogQueryKey, providers);
    }
    return client;
}
