import type { DictionaryPatch } from '@/types/patch';
import { EXTENSION_CATALOG_EVENTS } from '@/lib/extensionCatalogEvents';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPatch, apiPost } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { IntegrationSettingDefinition } from '@/hooks/useProviderSchema';
import type { ConnectionTestResult, IntegrationSettingsTestRequest } from '@/hooks/useTestConnection';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface ApiSetting {
    id: string;

    settingsGroupId: string;

    key: string;

    value: string;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

interface ApiSecretSettingState {
    isSet: boolean;
}

export interface ApiSettingsGroup {
    id?: string;

    name: string;

    extensionId?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    settings: ApiSetting[];

    secretSettings?: Record<string, ApiSecretSettingState> | null;

    schema?: IntegrationSettingDefinition[] | null;

    canTest?: boolean;
}

interface SettingsGroupPatch {
    settings: DictionaryPatch<string, string>;
}

export const settingsQueryKeys = {
    all: ['settings'] as const,
    groups: ['settings', 'groups'] as const,
};

const SETTINGS_CHANGE_EVENTS = ['settingsGroup.updated', ...EXTENSION_CATALOG_EVENTS] as const;

export function hasConfiguredExtensionSettings(group: ApiSettingsGroup): boolean {
    const hasExtension = group.extensionId !== undefined && group.extensionId !== null;
    const hasVisibleSettings = group.settings.some((setting) => setting.value.trim().length > 0);
    const hasSecretSettings = Object.values(group.secretSettings ?? {}).some((state) => state.isSet);

    return hasExtension && (hasVisibleSettings || hasSecretSettings);
}

async function updateSystemSettingsGroup(name: string, patch: SettingsGroupPatch): Promise<void> {
    return apiPatch(`/settings/groups/system/${encodeURIComponent(name)}`, patch);
}

async function fetchAllSettingsGroups(): Promise<ApiSettingsGroup[]> {
    return apiGet<ApiSettingsGroup[]>('/settings/groups');
}

async function updateExtensionSettingsGroup(
    extensionId: string,
    name: string,
    patch: SettingsGroupPatch,
): Promise<void> {
    return apiPatch(`/settings/groups/${encodeURIComponent(extensionId)}/${encodeURIComponent(name)}`, patch);
}

export function settingsToRecord(settings: ApiSetting[]): Record<string, string> {
    const record: Record<string, string> = {};
    for (const setting of settings) {
        record[setting.key] = setting.value;
    }
    return record;
}

export function useSystemSettingsGroup(name: string) {
    useInvalidateQueriesOnEvents(SETTINGS_CHANGE_EVENTS, [settingsQueryKeys.groups], {
        exact: true,
        owner: 'system-settings-group',
    });
    const query = useQuery({
        queryKey: settingsQueryKeys.groups,
        queryFn: fetchAllSettingsGroups,
        select: (groups) => groups.find((group) => group.extensionId == null && group.name === name) ?? null,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    return {
        group: query.data ?? null,
        refetch: query.refetch,
        isLoading: query.isLoading,
        error: query.error ?? null,
        dataUpdatedAt: query.dataUpdatedAt,
    };
}

interface UpdateSettingsVars {
    values: Record<string, string>;
    existingKeys: Set<string>;
}

function buildSettingsPatch({ values, existingKeys }: UpdateSettingsVars): SettingsGroupPatch {
    const add: Record<string, string> = {};
    const update: Record<string, string> = {};

    for (const [key, value] of Object.entries(values)) {
        if (existingKeys.has(key)) {
            update[key] = value;
        } else {
            add[key] = value;
        }
    }

    const settings: SettingsGroupPatch['settings'] = {};
    if (Object.keys(add).length > 0) {
        settings.add = add;
    }
    if (Object.keys(update).length > 0) {
        settings.update = update;
    }

    return { settings };
}

export function useUpdateSystemSettingsGroup(name: string) {
    const queryClient = useQueryClient();

    const mutation = useMutation({
        mutationFn: (vars: UpdateSettingsVars) => updateSystemSettingsGroup(name, buildSettingsPatch(vars)),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: settingsQueryKeys.groups });
        },
    });

    return {
        updateSettings: mutation.mutateAsync,
        isUpdating: mutation.isPending,
        error: queryErrorOrNull(mutation),
    };
}

export function useExtensionSettingsGroups(extensionId: string | null) {
    useInvalidateQueriesOnEvents(extensionId ? SETTINGS_CHANGE_EVENTS : [], [settingsQueryKeys.groups], {
        exact: true,
        owner: 'extension-settings-groups',
    });
    const query = useQuery({
        queryKey: settingsQueryKeys.groups,
        queryFn: fetchAllSettingsGroups,
        enabled: !!extensionId,
        staleTime: POLLING_INTERVALS.slow / 2,
        select: (groups) => (extensionId ? groups.filter((g) => g.extensionId === extensionId) : []),
    });

    return {
        groups: query.data ?? [],
        isLoading: query.isLoading,
        error: query.error ?? null,
        dataUpdatedAt: query.dataUpdatedAt,
    };
}

export function useUpdateExtensionSettingsGroup(extensionId: string, name: string) {
    const queryClient = useQueryClient();

    const mutation = useMutation({
        mutationFn: (vars: UpdateSettingsVars) =>
            updateExtensionSettingsGroup(extensionId, name, buildSettingsPatch(vars)),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: settingsQueryKeys.all });
        },
    });

    return {
        updateSettings: mutation.mutateAsync,
        isUpdating: mutation.isPending,
        error: queryErrorOrNull(mutation),
    };
}

async function testExtensionSettingsGroup(
    extensionId: string,
    name: string,
    settings: Record<string, string>,
): Promise<ConnectionTestResult> {
    const request: IntegrationSettingsTestRequest = { settings };
    return apiPost<ConnectionTestResult>(
        `/settings/groups/${encodeURIComponent(extensionId)}/${encodeURIComponent(name)}/test`,
        request,
    );
}

export function useTestExtensionSettingsGroup(extensionId: string, name: string) {
    const mutation = useMutation({
        mutationFn: (settings: Record<string, string>) => testExtensionSettingsGroup(extensionId, name, settings),
    });

    return {
        test: mutation.mutateAsync,
        isTesting: mutation.isPending,
        error: queryErrorOrNull(mutation),
    };
}
