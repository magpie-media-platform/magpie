import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';

import { apiGet, buildUrl } from '@/lib/api';
import { isTerminalJobExecutionStatus, useJobExecution } from '@/hooks/useJobs';
import { libraryImportCandidateQueryKeys, type PagedLibraryImportCandidates } from '@/hooks/useLibraryImportCandidates';
import { useToast } from '@/hooks/ui/useToast';
import { useQueryClient } from '@tanstack/react-query';

interface TrackedScan {
    executionId: string;
    rootFolderName: string;
    beforeCount: number;
}

export interface ScanBatchInfo {
    id: string;
    label: string;
    total: number;
    index: number;
}

interface ScanCompletionTrackerValue {
    trackScan: (executionId: string, rootFolderName: string, batch?: ScanBatchInfo) => void;
}

const ScanCompletionTrackerContext = createContext<ScanCompletionTrackerValue | null>(null);

export function ScanCompletionTrackerProvider({ children }: { children: ReactNode }) {
    const [scans, setScans] = useState<TrackedScan[]>([]);
    const batchBaselineRef = useRef<Map<string, number>>(new Map());

    const removeScan = useCallback((executionId: string) => {
        setScans((prev) => prev.filter((s) => s.executionId !== executionId));
    }, []);

    const trackScan = useCallback((executionId: string, rootFolderName: string, batch?: ScanBatchInfo) => {
        void (async () => {
            if (batch && batch.index === 1) {
                batchBaselineRef.current.set(batch.id, await fetchCurrentPendingCount());
            }
            if (batch && batch.index < batch.total) {
                return;
            }
            const beforeCount = batch
                ? (batchBaselineRef.current.get(batch.id) ?? (await fetchCurrentPendingCount()))
                : await fetchCurrentPendingCount();
            if (batch) {
                batchBaselineRef.current.delete(batch.id);
            }
            const displayName = batch ? batch.label : rootFolderName;
            setScans((prev) => {
                if (prev.some((s) => s.executionId === executionId)) {
                    return prev;
                }
                return [...prev, { executionId, rootFolderName: displayName, beforeCount }];
            });
        })();
    }, []);

    const value = useMemo<ScanCompletionTrackerValue>(() => ({ trackScan }), [trackScan]);

    return (
        <ScanCompletionTrackerContext.Provider value={value}>
            {children}
            {scans.map((scan) => (
                <ScanWatcher
                    key={scan.executionId}
                    executionId={scan.executionId}
                    rootFolderName={scan.rootFolderName}
                    beforeCount={scan.beforeCount}
                    onSettled={() => removeScan(scan.executionId)}
                />
            ))}
        </ScanCompletionTrackerContext.Provider>
    );
}

export function useScanCompletionTracker(): ScanCompletionTrackerValue {
    const ctx = useContext(ScanCompletionTrackerContext);
    if (!ctx) {
        return { trackScan: () => {} };
    }
    return ctx;
}

async function fetchCurrentPendingCount(): Promise<number> {
    try {
        const url = buildUrl('/import/candidates', { pageSize: 1 });
        const response = await apiGet<PagedLibraryImportCandidates>(url);
        return response.totalItems;
    } catch {
        return 0;
    }
}

interface ScanWatcherProps {
    executionId: string;
    rootFolderName: string;
    beforeCount: number;
    onSettled: () => void;
}

function ScanWatcher({ executionId, rootFolderName, beforeCount, onSettled }: ScanWatcherProps) {
    const { job } = useJobExecution(executionId);
    const { toast } = useToast();
    const navigate = useNavigate();
    const queryClient = useQueryClient();
    const firedRef = useRef(false);

    useEffect(() => {
        if (firedRef.current) {
            return;
        }
        if (!job || !isTerminalJobExecutionStatus(job.status)) {
            return;
        }
        firedRef.current = true;

        if (job.status === 'Completed') {
            void emitCompletionToast({
                rootFolderName,
                beforeCount,
                toast,
                navigate,
                queryClient,
            });
        } else {
            toast({
                title: `Scan ${job.status === 'Failed' ? 'failed' : 'canceled'}`,
                description:
                    job.status === 'Failed'
                        ? `The scan of "${rootFolderName}" did not complete.`
                        : `Magpie canceled the scan of "${rootFolderName}".`,
                variant: job.status === 'Failed' ? 'error' : 'info',
            });
        }

        onSettled();
    }, [job, rootFolderName, beforeCount, toast, navigate, queryClient, onSettled]);

    return null;
}

interface EmitCompletionToastArgs {
    rootFolderName: string;
    beforeCount: number;
    toast: ReturnType<typeof useToast>['toast'];
    navigate: ReturnType<typeof useNavigate>;
    queryClient: ReturnType<typeof useQueryClient>;
}

async function emitCompletionToast({
    rootFolderName,
    beforeCount,
    toast,
    navigate,
    queryClient,
}: EmitCompletionToastArgs): Promise<void> {
    const afterCount = await fetchCurrentPendingCount();
    const queued = Math.max(0, afterCount - beforeCount);

    await queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });

    if (queued === 0) {
        toast({
            title: 'Scan complete',
            description: `"${rootFolderName}" is up to date. Nothing new to import.`,
            variant: 'success',
        });
        return;
    }

    const queuedFileCountLabel = `${queued} file${queued === 1 ? '' : 's'}`;
    toast({
        title: 'Scan complete',
        description: `"${rootFolderName}": ${queuedFileCountLabel} ready to import.`,
        variant: 'info',
        action: {
            label: 'Import now',
            altText: 'Open the library import page',
            onClick: () => {
                void navigate('/library/import');
            },
        },
    });
}
