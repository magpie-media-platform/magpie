import { useCallback } from 'react';

import { selectProviderChoices, useProviderCatalog, type ProviderDescriptor } from '@/hooks/useProviderCatalog';

interface ProviderTypesResult {
    types: string[];
    labels: Record<string, string>;
    isLoading: boolean;
    isError: boolean;
}

function useIntegrationProviderTypes(kind: string): ProviderTypesResult {
    const select = useCallback((providers: ProviderDescriptor[]) => selectProviderChoices(providers, kind), [kind]);
    const query = useProviderCatalog(select);
    return {
        types: query.data?.types ?? [],
        labels: query.data?.labels ?? {},
        isLoading: query.isLoading,
        isError: query.isError,
    };
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
