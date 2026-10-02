import { useProviderCatalog } from '@/hooks/useProviderCatalog';
import { EXTENSION_CATALOG_EVENTS } from '@/lib/extensionCatalogEvents';
import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';

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

export function isRenderableSetting(entry: IntegrationSettingDefinition): boolean {
    return typeof entry.key === 'string' && entry.key.length > 0 && isSchemaDataType(entry.type);
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

export function useProviderSchemaQuery(
    kind: string,
    providerType: string | null,
): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderCatalog(
        (providers) =>
            providers.find((provider) => provider.kind === kind && provider.providerType === providerType)?.schema ??
            [],
        !!providerType,
    );
}

export function useDownloadClientSchema(providerType: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderSchemaQuery('downloadClient', providerType);
}

export function useMediaServerSchema(providerType: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderSchemaQuery('mediaServer', providerType);
}

export function useIndexerSchema(providerType: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    return useProviderSchemaQuery('indexer', providerType);
}

export function useIndexerInstanceSchema(indexerId: string | null): UseQueryResult<IntegrationSettingDefinition[]> {
    const queryKey = ['indexer-instance-schema', indexerId ?? ''] as const;
    useInvalidateQueriesOnEvents(indexerId ? EXTENSION_CATALOG_EVENTS : [], [queryKey], {
        exact: true,
    });
    useInvalidateQueriesOnEvents(indexerId ? ['indexer.updated'] : [], [queryKey], {
        entityIds: indexerId ? [indexerId] : [],
        exact: true,
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
    return useProviderSchemaQuery('indexerFeed', providerType);
}
