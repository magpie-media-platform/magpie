import { useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPatch, apiPost } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { useRestartRequired } from '@/components/ui/RestartRequiredBanner';

export interface ApiExtension {
    id: string;

    version: string;

    name: string;

    author?: string | null;

    description?: string | null;

    targetABI?: string | null;

    filePath: string;

    assemblyName: string;

    installed: boolean;

    enabled: boolean;

    disabledReason?: string | null;

    loadOrder: number;

    managed: boolean;

    nuGetId?: string | null;

    packageRepository?: string | null;

    autoUpgrade: boolean;

    lastChecked?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface ExtensionItem {
    id: string;

    name: string;

    version: string;

    enabled: boolean;

    managed: boolean;

    nuGetId?: string | null;

    disabledReason?: string | null;

    targetABI?: string | null;

    autoUpgrade: boolean;

    filePath: string;

    loadOrder: number;

    packageRepository?: string | null;

    createdAt: string;

    lastUpdateCheck?: string | null;

    builtIn: boolean;

    author?: string | null;

    description?: string | null;
}

export const extensionQueryKeys = {
    all: ['extensions'] as const,
    list: () => ['extensions', 'list'] as const,
    detail: (id: string) => ['extensions', 'detail', id] as const,
};

function isBuiltIn(ext: ApiExtension): boolean {
    return !ext.managed && !ext.nuGetId && ext.loadOrder < 100;
}

export function transformExtension(ext: ApiExtension): ExtensionItem {
    return {
        id: ext.id,
        name: ext.name,
        version: ext.version,
        enabled: ext.enabled,
        managed: ext.managed,
        nuGetId: ext.nuGetId,
        autoUpgrade: ext.autoUpgrade,
        filePath: ext.filePath,
        loadOrder: ext.loadOrder,
        packageRepository: ext.packageRepository,
        disabledReason: ext.disabledReason,
        targetABI: ext.targetABI,
        createdAt: ext.createdAt,
        lastUpdateCheck: ext.lastChecked,

        builtIn: isBuiltIn(ext),
        author: ext.author ?? null,
        description: ext.description ?? null,
    };
}

async function fetchExtensions(): Promise<ApiExtension[]> {
    return apiGet<ApiExtension[]>('/extensions');
}

async function enableExtension(id: string): Promise<ApiExtension> {
    return apiPost<ApiExtension>(`/extensions/${id}/enable`);
}

async function disableExtension(id: string): Promise<ApiExtension> {
    return apiPost<ApiExtension>(`/extensions/${id}/disable`);
}

interface ExtensionPatch {
    autoUpgrade?: { value: boolean };
    nuGetId?: { value: string | null };
    packageRepository?: { value: string | null };
}

async function patchExtension(id: string, patch: ExtensionPatch): Promise<void> {
    return apiPatch(`/extensions/${id}`, patch);
}

export function useExtensionManagement() {
    const queryClient = useQueryClient();

    const extensionsQuery = useQuery({
        queryKey: extensionQueryKeys.list(),
        queryFn: fetchExtensions,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const enableMutation = useMutation({
        mutationFn: enableExtension,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const disableMutation = useMutation({
        mutationFn: disableExtension,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: ExtensionPatch }) => patchExtension(id, patch),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const extensions: ExtensionItem[] = useMemo(() => {
        if (!extensionsQuery.data) return [];
        const byName = new Map<string, ApiExtension>();
        for (const ext of extensionsQuery.data) {
            const existing = byName.get(ext.name);
            if (!existing || new Date(ext.createdAt) > new Date(existing.createdAt)) {
                byName.set(ext.name, ext);
            }
        }
        return [...byName.values()].map(transformExtension);
    }, [extensionsQuery.data]);

    const toggleEnabled = async (id: string, enabled: boolean): Promise<ApiExtension> => {
        if (enabled) {
            return enableMutation.mutateAsync(id);
        } else {
            return disableMutation.mutateAsync(id);
        }
    };

    const setAutoUpgrade = async (id: string, autoUpgrade: boolean): Promise<void> => {
        return patchMutation.mutateAsync({ id, patch: { autoUpgrade: { value: autoUpgrade } } });
    };

    return {
        extensions,
        isLoading: extensionsQuery.isLoading,
        error: queryErrorOrNull(extensionsQuery),

        toggleEnabled,
        isToggling: enableMutation.isPending || disableMutation.isPending,
        setAutoUpgrade,
        isPatching: patchMutation.isPending,

        refetch: extensionsQuery.refetch,
    };
}

export function useExtension(id: string | undefined) {
    const listQuery = useQuery({
        queryKey: extensionQueryKeys.list(),
        queryFn: fetchExtensions,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const extension = useMemo(() => {
        if (!id || !listQuery.data) return null;
        const match = listQuery.data.find((ext) => ext.id === id);
        return match ? transformExtension(match) : null;
    }, [id, listQuery.data]);

    return {
        extension,
        isLoading: listQuery.isLoading,
        error: queryErrorOrNull(listQuery),
        refetch: listQuery.refetch,
    };
}

export interface ExtensionVersion {
    id: string;
    version: string;
    createdAt: string;
}

export function useExtensionVersions(extensionName: string | undefined) {
    const extensionsQuery = useQuery({
        queryKey: extensionQueryKeys.list(),
        queryFn: fetchExtensions,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const versions: ExtensionVersion[] = useMemo(() => {
        if (!extensionName || !extensionsQuery.data) return [];
        const sameNameExts = extensionsQuery.data
            .filter((ext) => ext.name === extensionName)
            .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
        return sameNameExts.slice(1).map((ext) => ({
            id: ext.id,
            version: ext.version,
            createdAt: ext.createdAt,
        }));
    }, [extensionName, extensionsQuery.data]);

    return { versions, isLoading: extensionsQuery.isLoading };
}

export interface InstallExtensionInput {
    packageId: string;

    version?: string;
}

export interface InstallExtensionResult {
    extension: ApiExtension;

    restartRequired: boolean;
}

export interface ExtensionUpdateInfo {
    extensionId: string;

    packageId: string | null;

    currentVersion: string;

    latestVersion: string | null;

    hasUpdate: boolean;
}

interface InstallResponseDto {
    extension: ApiExtension;

    restartRequired: boolean;
}

async function installExtensionApi(body: InstallExtensionInput): Promise<InstallResponseDto> {
    return apiPost<InstallResponseDto>('/extensions/install', body);
}

async function installExtensionFromFileApi(file: File): Promise<InstallResponseDto> {
    const form = new FormData();
    form.append('file', file, file.name);
    return apiPost<InstallResponseDto>('/extensions/install-file', form);
}

async function uninstallExtensionApi(id: string): Promise<void> {
    return apiPost(`/extensions/${id}/uninstall`);
}

async function updateExtensionApi(input: { id: string; version?: string }): Promise<InstallResponseDto> {
    return apiPost<InstallResponseDto>(`/extensions/${input.id}/update`, { version: input.version });
}

async function checkUpdatesApi(): Promise<ExtensionUpdateInfo[]> {
    return apiPost<ExtensionUpdateInfo[]>('/extensions/check-updates');
}

export function useExtensionActions() {
    const queryClient = useQueryClient();
    const { setRestartRequired } = useRestartRequired();

    const invalidateAll = () => {
        queryClient.invalidateQueries({ queryKey: extensionQueryKeys.all });
    };

    const installMutation = useMutation({
        mutationFn: installExtensionApi,
        onSuccess: invalidateAll,
    });

    const installFromFileMutation = useMutation({
        mutationFn: installExtensionFromFileApi,
        onSuccess: invalidateAll,
    });

    const uninstallMutation = useMutation({
        mutationFn: uninstallExtensionApi,
        onSuccess: invalidateAll,
    });

    const updateMutation = useMutation({
        mutationFn: updateExtensionApi,
        onSuccess: invalidateAll,
    });

    const checkUpdatesMutation = useMutation({
        mutationFn: checkUpdatesApi,
        onSuccess: invalidateAll,
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: ExtensionPatch }) => patchExtension(id, patch),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const installExtension = async (input: InstallExtensionInput): Promise<InstallExtensionResult> => {
        const response = await installMutation.mutateAsync(input);
        if (response.restartRequired) {
            setRestartRequired(true);
        }
        return { extension: response.extension, restartRequired: response.restartRequired };
    };

    const installExtensionFromFile = async (file: File): Promise<InstallExtensionResult> => {
        const response = await installFromFileMutation.mutateAsync(file);
        if (response.restartRequired) {
            setRestartRequired(true);
        }
        return { extension: response.extension, restartRequired: response.restartRequired };
    };

    const updateExtension = async (id: string, version?: string): Promise<InstallExtensionResult> => {
        const response = await updateMutation.mutateAsync({ id, version });
        if (response.restartRequired) {
            setRestartRequired(true);
        }
        return { extension: response.extension, restartRequired: response.restartRequired };
    };

    const uninstallExtension = async (id: string): Promise<void> => {
        await uninstallMutation.mutateAsync(id);
        // Eagerly mark restart-required: backend always sets X-Magpie-Restart-Required on uninstall, but the API client doesn't surface response headers yet.
        setRestartRequired(true);
    };

    return {
        installExtension,
        isInstalling: installMutation.isPending,

        installExtensionFromFile,
        isInstallingFromFile: installFromFileMutation.isPending,

        uninstallExtension,
        isUninstalling: uninstallMutation.isPending,

        updateExtension,
        isUpdating: updateMutation.isPending,

        checkUpdates: checkUpdatesMutation.mutateAsync,
        isCheckingUpdates: checkUpdatesMutation.isPending,
        latestUpdateInfo: checkUpdatesMutation.data ?? null,

        patchExtension: (id: string, patch: ExtensionPatch) => patchMutation.mutateAsync({ id, patch }),
        isPatching: patchMutation.isPending,
    };
}
