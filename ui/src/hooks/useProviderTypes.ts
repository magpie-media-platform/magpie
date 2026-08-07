import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';

const PROVIDER_CATALOG_EVENTS = [
    'extension.installed',
    'extension.enabled',
    'extension.disabled',
    'extension.uninstalled',
    'extension.upgraded',
] as const;

export const providerTypeQueryKeys = {
    downloadClients: ['provider-types', 'download-clients'] as const,
    mediaServers: ['provider-types', 'media-servers'] as const,
    indexers: ['provider-types', 'indexers'] as const,
    indexerFeeds: ['provider-types', 'indexer-feeds'] as const,
    externalUsers: ['provider-types', 'external-users'] as const,
};

interface ProviderTypesResult {
    types: string[];
    isLoading: boolean;
    isError: boolean;
}

async function getIntegrationProviderTypes(kind: string): Promise<string[]> {
    const providers = await apiGet<Array<{ kind: string; providerType: string }>>('/providers');

    return providers.filter((provider) => provider.kind === kind).map((provider) => provider.providerType);
}

function useProviderTypesQuery(queryKey: readonly string[], queryFn: () => Promise<string[]>): ProviderTypesResult {
    useInvalidateQueriesOnEvents(PROVIDER_CATALOG_EVENTS, [queryKey], {
        exact: true,
        owner: 'provider-types',
    });
    const query = useQuery({
        queryKey,
        queryFn,
        staleTime: 60_000,
    });

    return {
        types: query.data ?? [],
        isLoading: query.isLoading,
        isError: query.isError,
    };
}

export function useDownloadClientProviderTypes(): ProviderTypesResult {
    return useProviderTypesQuery(providerTypeQueryKeys.downloadClients, () =>
        getIntegrationProviderTypes('downloadClient'),
    );
}

export function useMediaServerProviderTypes(): ProviderTypesResult {
    return useProviderTypesQuery(providerTypeQueryKeys.mediaServers, () => getIntegrationProviderTypes('mediaServer'));
}

export function useIndexerProviderTypes(): ProviderTypesResult {
    return useProviderTypesQuery(providerTypeQueryKeys.indexers, () => getIntegrationProviderTypes('indexer'));
}

export function useIndexerFeedProviderTypes(): ProviderTypesResult {
    return useProviderTypesQuery(providerTypeQueryKeys.indexerFeeds, () => getIntegrationProviderTypes('indexerFeed'));
}

export function useExternalUserProviderTypes(): ProviderTypesResult {
    return useProviderTypesQuery(providerTypeQueryKeys.externalUsers, () => apiGet<string[]>('/auth-providers'));
}
