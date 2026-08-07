import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';

const PROVIDER_CATALOG_EVENTS = [
    'extension.installed',
    'extension.enabled',
    'extension.disabled',
    'extension.uninstalled',
    'extension.upgraded',
] as const;

export const SchemaDataType = {
    String: 'String',
    Number: 'Number',
    Boolean: 'Boolean',
    Date: 'Date',
    MappingsList: 'MappingsList',
} as const;

type SchemaDataTypeValue = (typeof SchemaDataType)[keyof typeof SchemaDataType];

const KNOWN_DATA_TYPES = new Set<string>(Object.values(SchemaDataType));

export function isSchemaDataType(value: unknown): value is SchemaDataTypeValue {
    return typeof value === 'string' && KNOWN_DATA_TYPES.has(value);
}

export interface SettingSchemaOptionsSource {
    kind: 'RootFolders' | 'ContentTypes' | 'JSFunction';

    functionName?: string | null;
}

export interface SettingSchemaOption {
    value: string;
    label: string;
    description?: string;
}

export interface IntegrationSettingDefinition {
    key: string;

    type: SchemaDataTypeValue;

    displayName?: string;

    description?: string;

    required?: boolean;

    secret?: boolean;

    defaultValue?: string;

    options?: string[];

    placeholder?: string;

    rowSchema?: IntegrationSettingDefinition[];

    optionsSource?: SettingSchemaOptionsSource;
}

interface ProviderDescriptor {
    kind: string;

    providerType: string;

    schema: IntegrationSettingDefinition[];
}

export function omitUnchangedSecretSettings(
    settings: Record<string, string>,
    schema: IntegrationSettingDefinition[],
): Record<string, string> {
    const result: Record<string, string> = {};
    for (const [key, value] of Object.entries(settings)) {
        const definition = schema.find(
            (entry) => typeof entry.key === 'string' && entry.key.toLowerCase() === key.toLowerCase(),
        );
        if (definition?.secret && !value) {
            continue;
        }
        result[key] = value;
    }
    return result;
}

export function secretsConfiguredForEdit(
    schema: IntegrationSettingDefinition[],
    isEditing: boolean,
): Record<string, boolean> {
    const result: Record<string, boolean> = {};
    for (const entry of schema) {
        if (entry.secret) {
            result[entry.key] = isEditing;
        }
    }
    return result;
}

export const providerSchemaQueryKeys = {
    downloadClients: (providerType: string) => ['provider-schema', 'download-clients', providerType] as const,
    mediaServers: (providerType: string) => ['provider-schema', 'media-servers', providerType] as const,
    indexers: (providerType: string) => ['provider-schema', 'indexers', providerType] as const,
    indexerFeeds: (providerType: string) => ['provider-schema', 'indexer-feeds', providerType] as const,
};

function providerKindForQueryKey(queryKey: readonly unknown[]): string {
    switch (queryKey[1]) {
        case 'download-clients':
            return 'downloadClient';
        case 'media-servers':
            return 'mediaServer';
        case 'indexers':
            return 'indexer';
        case 'indexer-feeds':
            return 'indexerFeed';
        default:
            return '';
    }
}

async function fetchProviderSchema(queryKey: readonly unknown[]): Promise<IntegrationSettingDefinition[]> {
    const kind = providerKindForQueryKey(queryKey);
    const providerType = typeof queryKey[2] === 'string' ? queryKey[2] : '';
    const providers = await apiGet<ProviderDescriptor[]>('/providers');
    return providers.find((provider) => provider.kind === kind && provider.providerType === providerType)?.schema ?? [];
}

function useProviderSchemaQuery(queryKey: readonly unknown[]): UseQueryResult<IntegrationSettingDefinition[]> {
    const providerType = typeof queryKey[2] === 'string' ? queryKey[2] : '';
    useInvalidateQueriesOnEvents(providerType ? PROVIDER_CATALOG_EVENTS : [], [queryKey], {
        exact: true,
        owner: 'provider-schema',
    });
    return useQuery({
        queryKey,
        queryFn: () => fetchProviderSchema(queryKey),
        enabled: providerType.length > 0,
        staleTime: 60_000,
    });
}

export function useDownloadClientSchema(providerType: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderSchemaQuery(providerSchemaQueryKeys.downloadClients(providerType ?? ''));
}

export function useMediaServerSchema(providerType: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderSchemaQuery(providerSchemaQueryKeys.mediaServers(providerType ?? ''));
}

export function useIndexerSchema(providerType: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderSchemaQuery(providerSchemaQueryKeys.indexers(providerType ?? ''));
}

export function useIndexerInstanceSchema(indexerId: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    const queryKey = ['indexer-instance-schema', indexerId ?? ''] as const;
    useInvalidateQueriesOnEvents(indexerId ? PROVIDER_CATALOG_EVENTS : [], [queryKey], {
        exact: true,
        owner: 'indexer-instance-schema',
    });
    useInvalidateQueriesOnEvents(indexerId ? ['indexer.updated'] : [], [queryKey], {
        entityIds: indexerId ? [indexerId] : [],
        exact: true,
        owner: 'indexer-instance-schema',
    });
    return useQuery({
        queryKey,
        queryFn: () =>
            apiGet<IntegrationSettingDefinition[]>(`/indexers/${encodeURIComponent(indexerId ?? '')}/schema`),
        enabled: !!indexerId,
        staleTime: 60_000,
    });
}

export function useIndexerFeedSchema(providerType: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderSchemaQuery(providerSchemaQueryKeys.indexerFeeds(providerType ?? ''));
}
