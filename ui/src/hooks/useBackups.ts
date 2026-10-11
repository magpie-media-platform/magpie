import { useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { executeJobRequest } from '@/lib/jobActions';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export type BackupType = 'manual' | 'scheduled';

export interface ApiBackup {
    id: string;

    name: string;

    createdAt: string;

    sizeBytes: number;

    kind?: 'Manual' | 'Scheduled' | 'manual' | 'scheduled';
}

export interface BackupItem {
    id: string;

    name: string;

    createdAt: string;

    sizeBytes: number;

    type: BackupType;
}

interface BackupCreateRequest {
    name?: string;
}

export interface BackupCapabilities {
    provider: string;

    backupRestoreSupported: boolean;

    optimizeSupported: boolean;

    lastOptimizedAt?: string | null;

    backupDeniedReason?: string | null;

    restoreDeniedReason?: string | null;

    deleteDeniedReason?: string | null;
}

export const backupQueryKeys = {
    capabilities: () => ['backups', 'capabilities'] as const,
    list: () => ['backups', 'list'] as const,
};

function backupTypeFromKind(kind: ApiBackup['kind']): BackupType {
    return kind?.toLowerCase() === 'scheduled' ? 'scheduled' : 'manual';
}

function transformBackup(backup: ApiBackup): BackupItem {
    return {
        id: backup.id,
        name: backup.name,
        createdAt: backup.createdAt,
        sizeBytes: backup.sizeBytes ?? 0,
        type: backupTypeFromKind(backup.kind),
    };
}

async function fetchBackups(): Promise<ApiBackup[]> {
    return apiGet<ApiBackup[]>('/database/backups');
}

async function fetchBackupCapabilities(): Promise<BackupCapabilities> {
    return apiGet<BackupCapabilities>('/database');
}

async function createBackup(request?: BackupCreateRequest): Promise<void> {
    await executeJobRequest({
        jobType: 'DatabaseBackup',
        parameters: request?.name ? { name: request.name } : undefined,
    });
}

async function deleteBackup(id: string): Promise<void> {
    return apiDelete(`/database/backups/${encodeURIComponent(id)}`);
}

async function restoreBackup(id: string): Promise<void> {
    return apiPost(`/database/backups/${encodeURIComponent(id)}/restore`);
}

export function useBackups() {
    const queryClient = useQueryClient();
    useInvalidateQueriesOnEvents(
        ['backup.completed', 'backup.failed', 'backup.deleted'],
        [backupQueryKeys.list(), backupQueryKeys.capabilities()],
        { exact: true },
    );
    const invalidateCapabilities = () => queryClient.invalidateQueries({ queryKey: backupQueryKeys.capabilities() });

    const capabilitiesQuery = useQuery({
        queryKey: backupQueryKeys.capabilities(),
        queryFn: fetchBackupCapabilities,
        staleTime: POLLING_INTERVALS.slow,
    });

    const backupRestoreSupported = capabilitiesQuery.data?.backupRestoreSupported !== false;

    const backupsQuery = useQuery({
        queryKey: backupQueryKeys.list(),
        queryFn: fetchBackups,
        enabled: capabilitiesQuery.isSuccess && backupRestoreSupported,
    });

    const createMutation = useMutation({
        mutationFn: createBackup,
        onSuccess: () => queryClient.invalidateQueries({ queryKey: backupQueryKeys.list() }),
    });

    const deleteMutation = useMutation({
        mutationFn: deleteBackup,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: backupQueryKeys.list() });
        },
        onError: invalidateCapabilities,
    });

    const restoreMutation = useMutation({
        mutationFn: restoreBackup,
        onError: invalidateCapabilities,
    });

    const backups: BackupItem[] = useMemo(() => backupsQuery.data?.map(transformBackup) ?? [], [backupsQuery.data]);

    return {
        backups,
        capabilities: capabilitiesQuery.data,
        backupRestoreSupported,
        isCapabilityLoading: capabilitiesQuery.isLoading,
        isLoading: capabilitiesQuery.isLoading || backupsQuery.isLoading,
        error: queryErrorOrNull(capabilitiesQuery) ?? queryErrorOrNull(backupsQuery),

        createBackup: createMutation.mutateAsync,
        isCreating: createMutation.isPending,
        createError: queryErrorOrNull(createMutation),

        deleteBackup: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,
        deleteError: queryErrorOrNull(deleteMutation),

        restoreBackup: restoreMutation.mutateAsync,
        isRestoring: restoreMutation.isPending,
        restoreError: queryErrorOrNull(restoreMutation),

        refetch: async () => {
            await Promise.all([capabilitiesQuery.refetch(), backupsQuery.refetch()]);
        },
    };
}
