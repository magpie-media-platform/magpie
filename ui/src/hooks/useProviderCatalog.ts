import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { EXTENSION_CATALOG_EVENTS } from '@/lib/extensionCatalogEvents';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

export interface ProviderDescriptor {
    kind: string;
    providerType: string;
    schema: IntegrationSettingDefinition[];
}

export const providerCatalogQueryKey = ['providers'] as const;

export function useProviderCatalog<T>(select: (providers: ProviderDescriptor[]) => T, enabled = true) {
    useInvalidateQueriesOnEvents(enabled ? EXTENSION_CATALOG_EVENTS : [], [providerCatalogQueryKey], {
        exact: true,
        owner: 'provider-catalog',
    });
    return useQuery({
        queryKey: providerCatalogQueryKey,
        queryFn: () => apiGet<ProviderDescriptor[]>('/providers'),
        select,
        enabled,
        staleTime: 60_000,
    });
}
