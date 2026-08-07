import { renderHook, waitFor, act } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';

import { apiGet, apiPost } from '@/lib/api';
import { useDiagnosticFindings, type DiagnosticFinding } from './useDiagnosticFindings';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
}));

vi.mock('@/auth', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/auth')>();
    return {
        ...actual,
        useAuth: () => ({ isAuthenticated: true }) as ReturnType<typeof actual.useAuth>,
    };
});

const findings: DiagnosticFinding[] = [
    {
        id: 'f-1',
        diagnosticCheckId: 'd-1',
        severity: 'Error',
        title: 'Missing default job',
        detail: 'The library sync job is not scheduled.',
        affectedEntity: 'Job:sync',
        repairId: 'create-default-job',
        detectedAt: '2026-05-01T00:00:00Z',
    },
];

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
    });
    return function Wrapper({ children }: { children: ReactNode }) {
        return createElement(QueryClientProvider, { client: queryClient }, children);
    };
}

beforeEach(() => {
    vi.clearAllMocks();
    let lastScannedAt = '2026-05-01T00:00:00Z';
    vi.mocked(apiGet).mockImplementation((path: string) => {
        if (path === '/diagnostics/findings') {
            return Promise.resolve({ findings, lastScannedAt });
        }
        return Promise.reject(new Error(`Unexpected GET ${path}`));
    });
    vi.mocked(apiPost).mockImplementation((path: string) => {
        if (path === '/jobs/execute-immediate') {
            lastScannedAt = '2026-05-02T00:00:00Z';
            return Promise.resolve('execution-1');
        }
        if (path === '/diagnostics/findings/f-1/repair') {
            return Promise.resolve({ success: true, message: 'Repair applied' });
        }
        return Promise.reject(new Error(`Unexpected POST ${path}`));
    });
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('useDiagnosticFindings', () => {
    it('loads findings and their scan timestamp from the diagnostics endpoint', async () => {
        const { result } = renderHook(() => useDiagnosticFindings(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.findings).toHaveLength(1);
        });

        expect(apiGet).toHaveBeenCalledWith('/diagnostics/findings');
        expect(result.current.findings[0].title).toBe('Missing default job');
        expect(result.current.lastScannedAt).toBe('2026-05-01T00:00:00Z');
    });

    it('starts a diagnostic scan and refreshes the findings response', async () => {
        const { result } = renderHook(() => useDiagnosticFindings(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.findings).toHaveLength(1);
        });

        await act(async () => {
            await result.current.runScan();
        });

        expect(apiPost).toHaveBeenCalledWith('/jobs/execute-immediate', { jobType: 'DiagnosticCheck' });
        await waitFor(() => {
            expect(result.current.lastScannedAt).toBe('2026-05-02T00:00:00Z');
        });
    });

    it('applies the repair identified by the finding', async () => {
        const { result } = renderHook(() => useDiagnosticFindings(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.findings).toHaveLength(1);
        });

        await act(async () => {
            await result.current.applyRepair({ findingId: 'f-1' });
        });

        expect(apiPost).toHaveBeenCalledWith('/diagnostics/findings/f-1/repair');
    });
});
