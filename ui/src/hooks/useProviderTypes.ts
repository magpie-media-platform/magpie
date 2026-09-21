import { useProviderCatalog } from '@/hooks/useProviderCatalog';

interface ProviderTypesResult {
    types: string[];
    isLoading: boolean;
    isError: boolean;
}

function useIntegrationProviderTypes(kind: string): ProviderTypesResult {
    const query = useProviderCatalog((providers) =>
        providers.filter((provider) => provider.kind === kind).map((provider) => provider.providerType),
    );
    return { types: query.data ?? [], isLoading: query.isLoading, isError: query.isError };
}

export function useDownloadClientProviderTypes(): ProviderTypesResult {
    return useIntegrationProviderTypes('downloadClient');
}

export function useMediaServerProviderTypes(): ProviderTypesResult {
    return useIntegrationProviderTypes('mediaServer');
}

export function useIndexerProviderTypes(): ProviderTypesResult {
    return useIntegrationProviderTypes('indexer');
}

export function useIndexerFeedProviderTypes(): ProviderTypesResult {
    return useIntegrationProviderTypes('indexerFeed');
}
