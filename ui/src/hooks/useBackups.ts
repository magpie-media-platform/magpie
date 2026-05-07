import { useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export type BackupType = 'manual' | 'scheduled';

export interface ApiBackup {
    id: string;

    name: string;

    createdAt: string;

    sizeBytes: number;
}

export interface BackupItem {
    id: string;

    name: string;

    createdAt: string;

    sizeBytes: number;

    type: BackupType;
}

export interface BackupCreateRequest {
    name?: string;
}

export interface BackupCreateResponse {
    id: string;
}

export interface BackupSettings {
    enabled: boolean;

    interval: 'daily' | 'weekly' | 'biweekly' | 'monthly';

    retention: number;
}

export const backupQueryKeys = {
    all: ['backups'] as const,
    list: () => ['backups', 'list'] as const,
    settings: () => ['backups', 'settings'] as const,
};

/** Server-generated scheduled backups have names starting with "Scheduled "; everything else is a user-supplied name. */
function inferBackupType(backup: ApiBackup): BackupType {
    return backup.name.startsWith('Scheduled ') ? 'scheduled' : 'manual';
}

function transformBackup(backup: ApiBackup): BackupItem {
    return {
        id: backup.id,
        name: backup.name,
        createdAt: backup.createdAt,
        sizeBytes: backup.sizeBytes ?? 0,
        type: inferBackupType(backup),
    };
}

async function fetchBackups(): Promise<ApiBackup[]> {
    return apiGet<ApiBackup[]>('/backups');
}

async function createBackup(request?: BackupCreateRequest): Promise<BackupCreateResponse> {
    return apiPost<BackupCreateResponse>('/backups', request);
}

async function deleteBackup(id: string): Promise<void> {
    return apiDelete(`/backups/${encodeURIComponent(id)}`);
}

async function restoreBackup(id: string): Promise<void> {
    return apiPost(`/backups/${encodeURIComponent(id)}/restore`);
}

export function useBackups() {
    const queryClient = useQueryClient();

    const backupsQuery = useQuery({
        queryKey: backupQueryKeys.list(),
        queryFn: fetchBackups,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createBackup,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: backupQueryKeys.list() });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteBackup,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: backupQueryKeys.list() });
        },
    });

    const restoreMutation = useMutation({
        mutationFn: restoreBackup,
    });

    const backups: BackupItem[] = useMemo(() => backupsQuery.data?.map(transformBackup) ?? [], [backupsQuery.data]);

    return {
        backups,
        isLoading: backupsQuery.isLoading,
        error: queryErrorOrNull(backupsQuery),

        createBackup: createMutation.mutateAsync,
        isCreating: createMutation.isPending,
        createError: queryErrorOrNull(createMutation),

        deleteBackup: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,
        deleteError: queryErrorOrNull(deleteMutation),

        restoreBackup: restoreMutation.mutateAsync,
        isRestoring: restoreMutation.isPending,
        restoreError: queryErrorOrNull(restoreMutation),

        refetch: backupsQuery.refetch,
    };
}
