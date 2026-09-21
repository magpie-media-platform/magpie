import { useCallback, useEffect, useRef, useState } from 'react';

import { apiGet, buildUrl } from '@/lib/api';
import { isTerminalJobExecutionStatus, useJobExecution } from '@/hooks/useJobs';
import { useStartRootFolderContentDiscovery } from '@/hooks/useStartRootFolderContentDiscovery';
import type { PagedLibraryImportCandidates } from '@/hooks/useLibraryImportCandidates';

export interface ScanTarget {
    id: string;
    name: string;
}

export interface ScanSummary {
    rootFolderName: string;
    outcome: 'completed' | 'failed' | 'cancelled';
    toImport: number | null;
}

export interface ImportScanControllerValue {
    isScanning: boolean;
    executionId: string | null;
    progress: number | null;
    statusMessage: string | null;
    lastSummary: ScanSummary | null;
    isStarting: boolean;
    startScan: (rootFolderId: string, rootFolderName: string) => Promise<void>;
    startScanAll: (folders: ScanTarget[], label?: string) => Promise<void>;
    clearSummary: () => void;
}

interface BatchState {
    id: string;
    label: string;
    total: number;
    index: number;
    toImport: number | null;
    outcome: ScanSummary['outcome'];
}

let batchSequence = 0;

function nextBatchId(): string {
    batchSequence += 1;
    return `import-batch-${batchSequence}`;
}

async function fetchPendingTotal(rootFolderId: string): Promise<number | null> {
    try {
        const url = buildUrl('/import/candidates', { rootFolderId, pageSize: 1 });
        const result = await apiGet<PagedLibraryImportCandidates>(url);
        return result.totalItems;
    } catch {
        return null;
    }
}

export function useImportScanController(): ImportScanControllerValue {
    const startMutation = useStartRootFolderContentDiscovery();

    const [executionId, setExecutionId] = useState<string | null>(null);
    const [trackedName, setTrackedName] = useState<string>('');
    const [lastSummary, setLastSummary] = useState<ScanSummary | null>(null);
    const [batch, setBatch] = useState<BatchState | null>(null);

    const beforeRef = useRef<{ rootFolderId: string; total: number | null } | null>(null);
    const queueRef = useRef<ScanTarget[]>([]);
    const batchRef = useRef<BatchState | null>(null);
    const settledExecutionRef = useRef<string | null>(null);

    const { job } = useJobExecution(executionId ?? '', executionId !== null);

    const startOne = useCallback(
        async (folder: ScanTarget) => {
            const before = await fetchPendingTotal(folder.id);
            const active = batchRef.current;
            const batch = active
                ? { id: active.id, label: active.label, total: active.total, index: active.index }
                : undefined;
            const result = await startMutation.mutateAsync({
                rootFolderId: folder.id,
                rootFolderName: folder.name,
                batch,
            });
            beforeRef.current = { rootFolderId: folder.id, total: before };
            setTrackedName(folder.name);
            setExecutionId(result.executionId);
        },
        [startMutation],
    );

    const startScan = useCallback(
        async (rootFolderId: string, rootFolderName: string) => {
            setLastSummary(null);
            queueRef.current = [];
            batchRef.current = null;
            setBatch(null);
            await startOne({ id: rootFolderId, name: rootFolderName });
        },
        [startOne],
    );

    const startScanAll = useCallback(
        async (folders: ScanTarget[], label = 'All root folders') => {
            if (folders.length === 0) {
                return;
            }
            setLastSummary(null);
            const [first, ...rest] = folders;
            queueRef.current = rest;
            const initial: BatchState = {
                id: nextBatchId(),
                label,
                total: folders.length,
                index: 1,
                toImport: 0,
                outcome: 'completed',
            };
            batchRef.current = initial;
            setBatch(initial);
            await startOne(first);
        },
        [startOne],
    );

    useEffect(() => {
        if (executionId === null || settledExecutionRef.current === executionId) {
            return;
        }
        if (!job || job.id !== executionId) {
            return;
        }
        // The worker reports 100% a beat before the runtime flips the status, so either one settles the scan.
        const reportedComplete = (job.progress ?? 0) >= 100;
        if (!isTerminalJobExecutionStatus(job.status) && !reportedComplete) {
            return;
        }
        settledExecutionRef.current = executionId;

        const before = beforeRef.current;
        const folderOutcome: ScanSummary['outcome'] =
            job.status === 'Failed' ? 'failed' : job.status === 'Cancelled' ? 'cancelled' : 'completed';
        const finishedName = trackedName;

        void (async () => {
            const after = before ? await fetchPendingTotal(before.rootFolderId) : null;
            const folderToImport = before?.total != null && after !== null ? Math.max(0, after - before.total) : null;

            const current = batchRef.current;
            if (current) {
                current.toImport =
                    current.toImport !== null && folderToImport !== null ? current.toImport + folderToImport : null;
                if (folderOutcome !== 'completed' && current.outcome === 'completed') {
                    current.outcome = folderOutcome;
                }

                const next = queueRef.current.shift();
                if (next) {
                    const advanced: BatchState = { ...current, index: current.index + 1 };
                    batchRef.current = advanced;
                    setBatch(advanced);
                    try {
                        await startOne(next);
                        return;
                    } catch {
                        current.outcome = 'failed';
                        queueRef.current = [];
                    }
                }

                setLastSummary({
                    rootFolderName: current.label,
                    outcome: current.outcome,
                    toImport: current.toImport,
                });
                batchRef.current = null;
                setBatch(null);
                setExecutionId(null);
                return;
            }

            setLastSummary({ rootFolderName: finishedName, outcome: folderOutcome, toImport: folderToImport });
            setExecutionId(null);
        })();
    }, [job, executionId, trackedName, startOne]);

    const clearSummary = useCallback(() => setLastSummary(null), []);

    const statusMessage = batch
        ? `${job?.statusMessage ?? `Scanning ${trackedName}`} (${batch.index} of ${batch.total})`
        : (job?.statusMessage ?? null);

    return {
        isScanning: executionId !== null,
        executionId,
        progress: job?.progress ?? null,
        statusMessage,
        lastSummary,
        isStarting: startMutation.isPending,
        startScan,
        startScanAll,
        clearSummary,
    };
}
