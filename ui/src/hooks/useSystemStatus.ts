import { useQuery } from '@tanstack/react-query';

import { useAuth } from '@/auth';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet } from '@/lib/api';
import { queryErrorOrNull } from '@/lib/utils';
import type { DiskSpaceInfo, DiskSpaceTotals } from '@/types/system';

interface ApiSystemInfo {
    version: string;
    runtime: string;
    operatingSystem: string;
    architecture: string;
    systemRoot: string;
    nodeId: string;
    startedAt: string;
    restartSupported: boolean;
}

interface ApiDiskSpaceInfo {
    id: string;
    name: string;
    path: string;
    totalBytes: number;
    freeBytes: number;
    usedBytes: number;
}

interface ApiDiskSpaceResponse {
    folders: ApiDiskSpaceInfo[];
    totalBytes: number;
    freeBytes: number;
    usedBytes: number;
}

export interface SystemStatusData {
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

    diskSpaceTotals: DiskSpaceTotals;

    isLoading: boolean;
}

const systemStatusQueryKeys = {
    info: ['system-status', 'info'] as const,
    diskSpace: ['system-status', 'disk-space'] as const,
};

async function fetchSystemInfo(): Promise<ApiSystemInfo> {
    return apiGet<ApiSystemInfo>('/system');
}

async function fetchDiskSpace(): Promise<ApiDiskSpaceResponse> {
    return apiGet<ApiDiskSpaceResponse>('/system/disk-space');
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

function getDiskSpaceData(data: ApiDiskSpaceResponse | undefined) {
    return {
        folders: data ? mapApiDiskSpaceToUi(data.folders) : [],
        totals: data
            ? {
                  usedBytes: data.usedBytes,
                  totalBytes: data.totalBytes,
                  freeBytes: data.freeBytes,
              }
            : { usedBytes: 0, totalBytes: 0, freeBytes: 0 },
    };
}

function useDiskSpaceQuery(isAuthenticated: boolean) {
    useInvalidateQueriesOnEvents(
        [
            'content.hierarchyItem.deleted',
            'content.hierarchyItem.updated',
            'download.imported',
            'library.rootFolder.created',
            'library.rootFolder.updated',
            'library.rootFolder.deleted',
        ],
        [systemStatusQueryKeys.diskSpace],
        { debounceMs: 1000, exact: true, maxWaitMs: 3000 },
    );
    return useQuery({
        queryKey: systemStatusQueryKeys.diskSpace,
        queryFn: fetchDiskSpace,
        enabled: isAuthenticated,
    });
}

export function useSystemStatus(): SystemStatusData {
    const { isAuthenticated } = useAuth();

    const infoQuery = useQuery({
        queryKey: systemStatusQueryKeys.info,
        queryFn: fetchSystemInfo,
        staleTime: Infinity,
        enabled: isAuthenticated,
    });

    const diskSpaceQuery = useDiskSpaceQuery(isAuthenticated);

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

    const diskSpaceData = getDiskSpaceData(diskSpaceQuery.data);

    const isLoading = infoQuery.isLoading || diskSpaceQuery.isLoading;

    return {
        systemInfo,
        diskSpace: diskSpaceData.folders,
        diskSpaceTotals: diskSpaceData.totals,
        isLoading,
    };
}

export function useSystemInfo() {
    const { isAuthenticated } = useAuth();

    return useQuery({
        queryKey: systemStatusQueryKeys.info,
        queryFn: fetchSystemInfo,
        staleTime: Infinity,
        enabled: isAuthenticated,
    });
}

export function useDiskSpace() {
    const { isAuthenticated } = useAuth();
    const diskSpaceQuery = useDiskSpaceQuery(isAuthenticated);
    const diskSpaceData = getDiskSpaceData(diskSpaceQuery.data);

    return {
        folders: diskSpaceData.folders,
        totals: diskSpaceData.totals,
        isLoading: diskSpaceQuery.isLoading,
        error: queryErrorOrNull(diskSpaceQuery),
        refetch: diskSpaceQuery.refetch,
    };
}
