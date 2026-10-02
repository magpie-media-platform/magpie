import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { EXTENSION_CATALOG_EVENTS } from '@/lib/extensionCatalogEvents';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

export interface ProviderDescriptor {
    kind: string;
    providerType: string;
    displayName: string;
    schema: IntegrationSettingDefinition[];
}

export interface ProviderChoices {
    types: string[];
    labels: Record<string, string>;
}

export function selectProviderChoices(providers: ProviderDescriptor[], kind: string): ProviderChoices {
    const ofKind = providers.filter((provider) => provider.kind === kind);
    return {
        types: ofKind.map((provider) => provider.providerType),
        labels: Object.fromEntries(ofKind.map((provider) => [provider.providerType, provider.displayName])),
    };
}

export const providerCatalogQueryKey = ['providers'] as const;

export function useProviderCatalog<T>(select: (providers: ProviderDescriptor[]) => T, enabled = true) {
    useInvalidateQueriesOnEvents(enabled ? EXTENSION_CATALOG_EVENTS : [], [providerCatalogQueryKey], {
        exact: true,
    });
    return useQuery({
        queryKey: providerCatalogQueryKey,
        queryFn: () => apiGet<ProviderDescriptor[]>('/providers'),
        select,
        enabled,
        staleTime: 60_000,
    });
}
