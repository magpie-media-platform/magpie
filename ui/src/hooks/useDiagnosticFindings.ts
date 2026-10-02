import { useCallback } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useAuth } from '@/auth';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, apiPost } from '@/lib/api';
import { executeImmediateJobRequest } from '@/lib/jobActions';
import { queryErrorOrNull } from '@/lib/utils';

export type DiagnosticFindingSeverity = 'Error' | 'Warning' | 'Info';

export interface DiagnosticFinding {
    id: string;
    diagnosticCheckId: string;
    severity: DiagnosticFindingSeverity;
    title: string;
    detail: string;
    affectedEntity: string | null;
    repairId: string | null;
    detectedAt: string;
}

interface DiagnosticFindingsResponse {
    findings: DiagnosticFinding[];
    lastScannedAt: string | null;
}

interface DiagnosticRepairResult {
    success: boolean;
    message: string;
}

interface ApplyRepairArgs {
    findingId: string;
}

const diagnosticQueryKeys = {
    all: ['diagnostics'] as const,
    findings: ['diagnostics', 'findings'] as const,
};

async function fetchFindings(): Promise<DiagnosticFindingsResponse> {
    return apiGet<DiagnosticFindingsResponse>('/diagnostics/findings');
}

interface UseDiagnosticFindingsResult {
    findings: DiagnosticFinding[];
    lastScannedAt: string | null;
    isLoading: boolean;
    error: Error | null;
    runScan: () => Promise<DiagnosticFindingsResponse>;
    isScanning: boolean;
    applyRepair: (args: ApplyRepairArgs) => Promise<DiagnosticRepairResult>;
    isRepairing: boolean;
    invalidate: () => Promise<void>;
}

export function useDiagnosticFindings(): UseDiagnosticFindingsResult {
    const { isAuthenticated } = useAuth();
    const queryClient = useQueryClient();

    const findingsQuery = useQuery({
        queryKey: diagnosticQueryKeys.findings,
        queryFn: fetchFindings,
        enabled: isAuthenticated,
    });

    const invalidate = useCallback(
        () => queryClient.invalidateQueries({ queryKey: diagnosticQueryKeys.all }),
        [queryClient],
    );

    useInvalidateQueriesOnEvents(
        isAuthenticated ? ['diagnostic.finding.raised', 'diagnostic.finding.updated', 'diagnostic.scan.completed'] : [],
        [diagnosticQueryKeys.findings],
        { debounceMs: 250, exact: true, maxWaitMs: 1000 },
    );

    const scanMutation = useMutation({
        mutationFn: async () => {
            await executeImmediateJobRequest({ jobType: 'DiagnosticCheck' });
            const result = await findingsQuery.refetch();

            return result.data ?? { findings: [], lastScannedAt: new Date().toISOString() };
        },
    });

    const repairMutation = useMutation({
        mutationFn: ({ findingId }: ApplyRepairArgs) =>
            apiPost<DiagnosticRepairResult>(`/diagnostics/findings/${findingId}/repair`),
        onSettled: () => {
            void invalidate();
        },
    });

    return {
        findings: findingsQuery.data?.findings ?? [],
        lastScannedAt: findingsQuery.data?.lastScannedAt ?? null,
        isLoading: findingsQuery.isLoading,
        error: queryErrorOrNull(findingsQuery),
        runScan: scanMutation.mutateAsync,
        isScanning: scanMutation.isPending,
        applyRepair: repairMutation.mutateAsync,
        isRepairing: repairMutation.isPending,
        invalidate,
    };
}
