import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { DiskSpaceInfo, DownloadStateCounts, ActiveDownloadItem } from '@/types/system';
import {
    useIndexerHealth,
    useDownloadClientHealth,
    useMediaServerHealth,
    useActiveDownloads,
    useRecentJobs,
    type HealthCounts,
    type RecentJob,
} from './useDashboard';

export type ServiceStatus = 'healthy' | 'unhealthy' | 'degraded' | 'unknown';

export interface ServiceHealth {
    name: string;
    status: ServiceStatus;
    message?: string;
    lastCheckedAt: string;
    responseTime?: number;
}

interface ApiSystemInfo {
    version: string;
    runtime: string;
    operatingSystem: string;
    architecture: string;
    systemRoot: string;
    nodeId: string;
    startedAt: string;
}

interface ApiHealthResponse {
    status: string;
}

interface ApiServiceHealthResponse {
    name: string;
    status: string;
    message?: string;
    responseTimeMs?: number;
}

interface ApiDiskSpaceInfo {
    id: string;
    name: string;
    path: string;
    totalBytes: number;
    freeBytes: number;
    usedBytes: number;
}

export interface SystemStatusData {
    services: {
        api: ServiceHealth;
        database: ServiceHealth;
        scheduler: ServiceHealth;
    };

    integrations: {
        indexers: HealthCounts;
        downloadClients: HealthCounts;
        mediaServers: HealthCounts;
    };

    downloadQueue: {
        counts: DownloadStateCounts;
        activeDownloads: ActiveDownloadItem[];
    };

    systemInfo: {
        version: string;
        runtime: string;
        operatingSystem: string;
        architecture: string;
        systemRoot: string;
        nodeId: string;
        startedAt: string | null;
    };

    diskSpace: DiskSpaceInfo[];

    recentJobs: RecentJob[];

    isLoading: boolean;

    error: Error | null;
}

export const systemStatusQueryKeys = {
    all: ['system-status'] as const,
    health: ['system-status', 'health'] as const,
    databaseHealth: ['system-status', 'database-health'] as const,
    schedulerHealth: ['system-status', 'scheduler-health'] as const,
    info: ['system-status', 'info'] as const,
    diskSpace: ['system-status', 'disk-space'] as const,
};

async function fetchSystemHealth(): Promise<ApiHealthResponse> {
    return apiGet<ApiHealthResponse>('/system/health', { skipAuth: true });
}

async function fetchSystemInfo(): Promise<ApiSystemInfo> {
    return apiGet<ApiSystemInfo>('/system/info');
}

async function fetchDiskSpace(): Promise<ApiDiskSpaceInfo[]> {
    return apiGet<ApiDiskSpaceInfo[]>('/system/disk-space');
}

async function fetchDatabaseHealth(): Promise<ApiServiceHealthResponse> {
    return apiGet<ApiServiceHealthResponse>('/system/health/database');
}

async function fetchSchedulerHealth(): Promise<ApiServiceHealthResponse> {
    return apiGet<ApiServiceHealthResponse>('/system/health/scheduler');
}

function mapHealthStatus(status: string): ServiceStatus {
    const normalized = status.toLowerCase();
    if (normalized === 'healthy') {
        return 'healthy';
    }
    if (normalized === 'unhealthy') {
        return 'unhealthy';
    }
    if (normalized === 'degraded') {
        return 'degraded';
    }
    return 'unknown';
}

function timestampToIso(dataUpdatedAt: number): string {
    return dataUpdatedAt ? new Date(dataUpdatedAt).toISOString() : new Date(0).toISOString();
}

function mapApiDiskSpaceToUi(diskSpaceInfo: ApiDiskSpaceInfo[]): DiskSpaceInfo[] {
    return diskSpaceInfo.map((info) => ({
        id: info.id,
        name: info.name,
        path: info.path,
        usedBytes: info.usedBytes,
        totalBytes: info.totalBytes,
        freeBytes: info.freeBytes,
    }));
}

const emptyHealthCounts: HealthCounts = {
    healthy: 0,
    unhealthy: 0,
    unknown: 0,
    stale: 0,
};

const emptyDownloadCounts: DownloadStateCounts = {
    downloading: 0,
    queued: 0,
    stalled: 0,
};

export function useSystemStatus(): SystemStatusData {
    const healthQuery = useQuery({
        queryKey: systemStatusQueryKeys.health,
        queryFn: fetchSystemHealth,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const infoQuery = useQuery({
        queryKey: systemStatusQueryKeys.info,
        queryFn: fetchSystemInfo,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const diskSpaceQuery = useQuery({
        queryKey: systemStatusQueryKeys.diskSpace,
        queryFn: fetchDiskSpace,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const databaseHealthQuery = useQuery({
        queryKey: systemStatusQueryKeys.databaseHealth,
        queryFn: fetchDatabaseHealth,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const schedulerHealthQuery = useQuery({
        queryKey: systemStatusQueryKeys.schedulerHealth,
        queryFn: fetchSchedulerHealth,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const indexerHealth = useIndexerHealth();
    const clientHealth = useDownloadClientHealth();
    const serverHealth = useMediaServerHealth();
    const activeDownloads = useActiveDownloads(3);
    const recentJobs = useRecentJobs(5);

    const apiStatus: ServiceStatus = healthQuery.isError
        ? 'unhealthy'
        : healthQuery.data
          ? mapHealthStatus(healthQuery.data.status)
          : 'unknown';

    const apiServiceHealth: ServiceHealth = {
        name: 'API',
        status: apiStatus,
        message:
            apiStatus === 'healthy'
                ? 'All endpoints responding normally'
                : apiStatus === 'unhealthy'
                  ? 'Service unavailable'
                  : 'Checking status...',
        lastCheckedAt: timestampToIso(healthQuery.dataUpdatedAt),
    };

    const databaseServiceHealth: ServiceHealth = databaseHealthQuery.data
        ? {
              name: databaseHealthQuery.data.name,
              status: mapHealthStatus(databaseHealthQuery.data.status),
              message: databaseHealthQuery.data.message,
              lastCheckedAt: timestampToIso(databaseHealthQuery.dataUpdatedAt),
              responseTime: databaseHealthQuery.data.responseTimeMs,
          }
        : {
              name: 'Database',
              status: databaseHealthQuery.isError ? 'unhealthy' : 'unknown',
              message: databaseHealthQuery.isError ? 'Health check failed' : 'Checking status...',
              lastCheckedAt: timestampToIso(databaseHealthQuery.dataUpdatedAt),
          };

    const schedulerServiceHealth: ServiceHealth = schedulerHealthQuery.data
        ? {
              name: schedulerHealthQuery.data.name,
              status: mapHealthStatus(schedulerHealthQuery.data.status),
              message: schedulerHealthQuery.data.message,
              lastCheckedAt: timestampToIso(schedulerHealthQuery.dataUpdatedAt),
              responseTime: schedulerHealthQuery.data.responseTimeMs,
          }
        : {
              name: 'Scheduler',
              status: schedulerHealthQuery.isError ? 'unhealthy' : 'unknown',
              message: schedulerHealthQuery.isError ? 'Health check failed' : 'Checking status...',
              lastCheckedAt: timestampToIso(schedulerHealthQuery.dataUpdatedAt),
          };

    const systemInfo = infoQuery.data
        ? {
              version: infoQuery.data.version,
              runtime: infoQuery.data.runtime,
              operatingSystem: infoQuery.data.operatingSystem,
              architecture: infoQuery.data.architecture,
              systemRoot: infoQuery.data.systemRoot,
              nodeId: infoQuery.data.nodeId,
              startedAt: infoQuery.data.startedAt,
          }
        : {
              version: '-',
              runtime: '-',
              operatingSystem: '-',
              architecture: '-',
              systemRoot: '-',
              nodeId: '-',
              startedAt: null,
          };

    const diskSpace = diskSpaceQuery.data ? mapApiDiskSpaceToUi(diskSpaceQuery.data) : [];

    const downloadQueue = {
        counts: activeDownloads.data?.counts ?? emptyDownloadCounts,
        activeDownloads:
            activeDownloads.data?.items?.map((item) => ({
                id: item.id,
                title: item.title,
                progress: item.progress,
                state: item.state,
                size: item.size,
                eta: item.eta,
            })) ?? [],
    };

    const isLoading =
        healthQuery.isLoading ||
        infoQuery.isLoading ||
        databaseHealthQuery.isLoading ||
        schedulerHealthQuery.isLoading ||
        indexerHealth.isLoading ||
        clientHealth.isLoading ||
        serverHealth.isLoading;

    const error =
        healthQuery.error ||
        infoQuery.error ||
        databaseHealthQuery.error ||
        schedulerHealthQuery.error ||
        indexerHealth.error ||
        clientHealth.error ||
        serverHealth.error;

    return {
        services: {
            api: apiServiceHealth,
            database: databaseServiceHealth,
            scheduler: schedulerServiceHealth,
        },
        integrations: {
            indexers: indexerHealth.data ?? emptyHealthCounts,
            downloadClients: clientHealth.data ?? emptyHealthCounts,
            mediaServers: serverHealth.data ?? emptyHealthCounts,
        },
        downloadQueue,
        systemInfo,
        diskSpace,
        recentJobs: recentJobs.data ?? [],
        isLoading,
        error: queryErrorOrNull({ error }),
    };
}

export function useSystemInfo() {
    return useQuery({
        queryKey: systemStatusQueryKeys.info,
        queryFn: fetchSystemInfo,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useSystemHealth() {
    return useQuery({
        queryKey: systemStatusQueryKeys.health,
        queryFn: fetchSystemHealth,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}
