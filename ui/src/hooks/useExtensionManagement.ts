import type { ScalarPatch } from '@/types/patch';
import { EXTENSION_CATALOG_EVENTS } from '@/lib/extensionCatalogEvents';
import { useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiFetchResponse, apiGet, apiPatch, apiPost } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { useRestartRequired } from '@/hooks/ui/useRestartRequired';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';

const RESTART_REQUIRED_HEADER = 'X-Magpie-Restart-Required';

export interface ApiExtension {
    id: string;

    packageId: string;

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

    packageId: string;

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

    author?: string | null;

    description?: string | null;
}

const extensionQueryKeys = {
    all: ['extensions'] as const,
    list: () => ['extensions', 'list'] as const,
};

function transformExtension(ext: ApiExtension): ExtensionItem {
    return {
        id: ext.id,
        packageId: ext.packageId,
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

        author: ext.author ?? null,
        description: ext.description ?? null,
    };
}

interface ExtensionListItemDto {
    extension: ApiExtension;

    updateInfo: ExtensionUpdateInfo | null;
}

interface ExtensionListData {
    extensions: ApiExtension[];

    updateInfo: ExtensionUpdateInfo[];
}

async function fetchExtensions(): Promise<ExtensionListData> {
    const items = await apiGet<ExtensionListItemDto[]>('/extensions');
    return {
        extensions: items.map((item) => item.extension),
        updateInfo: items.flatMap((item) => (item.updateInfo ? [item.updateInfo] : [])),
    };
}

async function enableExtension(id: string): Promise<ApiExtension> {
    return apiPost<ApiExtension>(`/extensions/${id}/enable`);
}

async function disableExtension(id: string): Promise<ApiExtension> {
    return apiPost<ApiExtension>(`/extensions/${id}/disable`);
}

interface ExtensionPatch {
    autoUpgrade?: ScalarPatch<boolean>;
    nuGetId?: ScalarPatch<string | null>;
    packageRepository?: ScalarPatch<string | null>;
    loadOrder?: ScalarPatch<number>;
}

async function patchExtension(id: string, patch: ExtensionPatch): Promise<void> {
    return apiPatch(`/extensions/${id}`, patch);
}

interface ExtensionLoadOrderUpdate {
    id: string;
    loadOrder: number;
}

async function persistExtensionOrder(updates: ExtensionLoadOrderUpdate[]): Promise<void> {
    for (const { id, loadOrder } of updates) {
        await patchExtension(id, { loadOrder: { value: loadOrder } });
    }
}

function comparePackageIdDescending(left: string, right: string): number {
    const normalizedLeft = left.toUpperCase();
    const normalizedRight = right.toUpperCase();
    if (normalizedLeft < normalizedRight) {
        return 1;
    }
    if (normalizedLeft > normalizedRight) {
        return -1;
    }
    return 0;
}

function compareExtensionOrder(left: ExtensionItem, right: ExtensionItem): number {
    return right.loadOrder - left.loadOrder || comparePackageIdDescending(left.packageId, right.packageId);
}

export function useExtensionManagement() {
    useInvalidateQueriesOnEvents(EXTENSION_CATALOG_EVENTS, [extensionQueryKeys.list()], {
        exact: true,
    });
    const queryClient = useQueryClient();
    const { setRestartRequired } = useRestartRequired();

    const extensionsQuery = useQuery({
        queryKey: extensionQueryKeys.list(),
        queryFn: fetchExtensions,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const enableMutation = useMutation({
        mutationFn: enableExtension,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const disableMutation = useMutation({
        mutationFn: disableExtension,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: ExtensionPatch }) => patchExtension(id, patch),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const reorderMutation = useMutation({
        mutationFn: persistExtensionOrder,
        onSettled: () => {
            void queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
        onSuccess: () => {
            setRestartRequired(true);
        },
    });

    const extensions: ExtensionItem[] = useMemo(
        () => extensionsQuery.data?.extensions.map(transformExtension) ?? [],
        [extensionsQuery.data],
    );

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

    const reorderExtensions = async (orderedIds: string[]): Promise<void> => {
        const currentOrder = [...extensions].sort(compareExtensionOrder);
        if (
            orderedIds.length !== currentOrder.length ||
            new Set(orderedIds).size !== currentOrder.length ||
            currentOrder.some((extension) => !orderedIds.includes(extension.id))
        ) {
            throw new Error('Extension order does not match the current extension list.');
        }

        let assignedLoadOrders = new Map(orderedIds.map((id, index) => [id, currentOrder[index].loadOrder]));
        const assignedOrder = [...currentOrder]
            .map((extension) => ({
                ...extension,
                loadOrder: assignedLoadOrders.get(extension.id) ?? extension.loadOrder,
            }))
            .sort(compareExtensionOrder)
            .map((extension) => extension.id);

        if (assignedOrder.some((id, index) => id !== orderedIds[index])) {
            assignedLoadOrders = new Map(orderedIds.map((id, index) => [id, orderedIds.length - index]));
        }

        const updates = currentOrder.flatMap((extension) => {
            const loadOrder = assignedLoadOrders.get(extension.id);
            return loadOrder !== undefined && loadOrder !== extension.loadOrder
                ? [{ id: extension.id, loadOrder }]
                : [];
        });
        if (updates.length === 0) {
            return;
        }

        await reorderMutation.mutateAsync(updates);
    };

    return {
        extensions,
        isLoading: extensionsQuery.isLoading,
        error: queryErrorOrNull(extensionsQuery),

        toggleEnabled,
        isToggling: enableMutation.isPending || disableMutation.isPending,
        setAutoUpgrade,
        isPatching: patchMutation.isPending,
        reorderExtensions,
        isMoving: reorderMutation.isPending,

        refetch: extensionsQuery.refetch,
    };
}

export function useExtension(id: string | undefined) {
    useInvalidateQueriesOnEvents(EXTENSION_CATALOG_EVENTS, [extensionQueryKeys.list()], {
        entityIds: id ? [id] : [],
        exact: true,
    });
    const listQuery = useQuery({
        queryKey: extensionQueryKeys.list(),
        queryFn: fetchExtensions,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const extension = useMemo(() => {
        if (!id || !listQuery.data) return null;
        const match = listQuery.data.extensions.find((ext) => ext.id === id);
        return match ? transformExtension(match) : null;
    }, [id, listQuery.data]);

    return {
        extension,
        isLoading: listQuery.isLoading,
        error: queryErrorOrNull(listQuery),
        refetch: listQuery.refetch,
    };
}

interface ExtensionVersion {
    id: string;
    version: string;
    createdAt: string;
}

export function useExtensionVersions(extensionPackageId: string | undefined) {
    useInvalidateQueriesOnEvents(EXTENSION_CATALOG_EVENTS, [extensionQueryKeys.list()], {
        exact: true,
    });
    const extensionsQuery = useQuery({
        queryKey: extensionQueryKeys.list(),
        queryFn: fetchExtensions,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const versions: ExtensionVersion[] = useMemo(() => {
        if (!extensionPackageId || !extensionsQuery.data) return [];
        const sameNameExts = extensionsQuery.data.extensions
            .filter((ext) => ext.packageId === extensionPackageId)
            .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
        return sameNameExts.slice(1).map((ext) => ({
            id: ext.id,
            version: ext.version,
            createdAt: ext.createdAt,
        }));
    }, [extensionPackageId, extensionsQuery.data]);

    return { versions, isLoading: extensionsQuery.isLoading };
}

interface InstallExtensionInput {
    packageId: string;

    version?: string;
}

export interface ExtensionUpdateInfo {
    extensionId: string;

    packageId: string | null;

    currentVersion: string;

    latestVersion: string | null;

    hasUpdate: boolean;
}

interface InstallExtensionResult {
    extension: ApiExtension;

    restartRequired: boolean;
}

async function installExtensionApi(body: InstallExtensionInput): Promise<InstallExtensionResult> {
    return apiPost<InstallExtensionResult>('/extensions', body);
}

async function installExtensionFromFileApi(file: File): Promise<InstallExtensionResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return apiPost<InstallExtensionResult>('/extensions/upload', form);
}

async function uninstallExtensionApi(id: string): Promise<boolean> {
    const response = await apiFetchResponse(`/extensions/${id}`, { method: 'DELETE' });
    return response.headers.get(RESTART_REQUIRED_HEADER)?.trim().toLowerCase() === 'true';
}

async function updateExtensionApi(input: { id: string; version?: string }): Promise<InstallExtensionResult> {
    const path = input.version
        ? `/extensions/${input.id}/upgrade/${encodeURIComponent(input.version)}`
        : `/extensions/${input.id}/upgrade/latest`;
    return apiPost<InstallExtensionResult>(path);
}

export function useExtensionActions() {
    const queryClient = useQueryClient();
    const { setRestartRequired } = useRestartRequired();

    const invalidateAll = () => {
        void queryClient.invalidateQueries({ queryKey: extensionQueryKeys.all });
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
        mutationFn: async () => {
            const result = await queryClient.fetchQuery({
                queryKey: extensionQueryKeys.list(),
                queryFn: fetchExtensions,
                staleTime: 0,
            });
            return result.updateInfo;
        },
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: ExtensionPatch }) => patchExtension(id, patch),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: extensionQueryKeys.list() });
        },
    });

    const installExtension = async (input: InstallExtensionInput): Promise<InstallExtensionResult> => {
        const response = await installMutation.mutateAsync(input);
        if (response.restartRequired) {
            setRestartRequired(true);
        }
        return response;
    };

    const installExtensionFromFile = async (file: File): Promise<InstallExtensionResult> => {
        const response = await installFromFileMutation.mutateAsync(file);
        if (response.restartRequired) {
            setRestartRequired(true);
        }
        return response;
    };

    const updateExtension = async (id: string, version?: string): Promise<InstallExtensionResult> => {
        const response = await updateMutation.mutateAsync({ id, version });
        if (response.restartRequired) {
            setRestartRequired(true);
        }
        return response;
    };

    const uninstallExtension = async (id: string): Promise<void> => {
        const restartRequired = await uninstallMutation.mutateAsync(id);
        if (restartRequired) {
            setRestartRequired(true);
        }
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
