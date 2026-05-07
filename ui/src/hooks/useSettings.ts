import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPatch } from '@/lib/api';
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

export interface ApiSettingsGroup {
    id: string;

    name: string;

    extensionId?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    settings: ApiSetting[];
}

export interface SettingsGroupPatch {
    settings: {
        add?: Record<string, string>;
        remove?: string[];
        update?: Record<string, string>;
    };
}

export const settingsQueryKeys = {
    all: ['settings'] as const,
    systemGroup: (name: string) => ['settings', 'system', name] as const,
    extensionGroups: (extensionId: string) => ['settings', 'extension', extensionId] as const,
};

async function fetchSystemSettingsGroup(name: string): Promise<ApiSettingsGroup> {
    return apiGet<ApiSettingsGroup>(`/settings/groups/system/${encodeURIComponent(name)}`);
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
    const query = useQuery({
        queryKey: settingsQueryKeys.systemGroup(name),
        queryFn: () => fetchSystemSettingsGroup(name),
    });

    return {
        group: query.data ?? null,
        isLoading: query.isLoading,
        error: query.error ?? null,
    };
}

export interface UpdateSettingsVars {
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
            queryClient.invalidateQueries({ queryKey: settingsQueryKeys.systemGroup(name) });
        },
    });

    return {
        updateSettings: mutation.mutateAsync,
        isUpdating: mutation.isPending,
        error: queryErrorOrNull(mutation),
    };
}

export function useExtensionSettingsGroups(extensionId: string | null) {
    const query = useQuery({
        queryKey: settingsQueryKeys.extensionGroups(extensionId ?? ''),
        queryFn: fetchAllSettingsGroups,
        enabled: !!extensionId,
        select: (groups) => groups.filter((g) => g.extensionId === extensionId),
    });

    return {
        groups: query.data ?? [],
        isLoading: query.isLoading,
        error: query.error ?? null,
    };
}

export function useUpdateExtensionSettingsGroup(extensionId: string, name: string) {
    const queryClient = useQueryClient();

    const mutation = useMutation({
        mutationFn: (vars: UpdateSettingsVars) =>
            updateExtensionSettingsGroup(extensionId, name, buildSettingsPatch(vars)),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: settingsQueryKeys.extensionGroups(extensionId) });
        },
    });

    return {
        updateSettings: mutation.mutateAsync,
        isUpdating: mutation.isPending,
        error: queryErrorOrNull(mutation),
    };
}
