import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { Role } from '@/auth';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

import { DiagnosticsSection } from './DiagnosticsSection';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
    };
});

const authState = vi.hoisted(() => ({ role: 3 }));

vi.mock('@/auth', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/auth')>();
    return {
        ...actual,
        useAuth: () => ({
            isLoading: false,
            isAuthenticated: true,
            user: { id: 'u1', username: 'admin', role: authState.role },
            hasRole: (minRole: number) => authState.role >= minRole,
            hasPermission: () => true,
            signIn: vi.fn(),
            signOut: vi.fn(),
            login: vi.fn(),
        }),
    };
});

import { apiGet, apiPost } from '@/lib/api';

const findings = [
    {
        id: 'f-info',
        diagnosticCheckId: 'setup.optional',
        severity: 'Info',
        title: 'Optional setup step skipped',
        detail: 'A media server has not been configured yet.',
        affectedEntity: null,
        repairId: null,
        detectedAt: '2026-05-01T00:00:00Z',
    },
    {
        id: 'f-warn',
        diagnosticCheckId: 'quality.empty',
        severity: 'Warning',
        title: 'Empty quality profile',
        detail: 'The default quality profile has no enabled tiers.',
        affectedEntity: 'QualityProfile:7',
        repairId: null,
        detectedAt: '2026-05-01T00:00:00Z',
    },
    {
        id: 'f-err',
        diagnosticCheckId: 'jobs.missing-default',
        severity: 'Error',
        title: 'Missing default job',
        detail: 'The library sync job is not scheduled.',
        affectedEntity: 'Job:sync',
        repairId: 'create-default-job',
        detectedAt: '2026-05-01T00:00:00Z',
    },
];

let currentDiagnosticFindings: unknown = findings;
let currentLastScannedAtUtc: string | null = '2026-05-01T00:00:00Z';

function mockDiagnosticGet({
    diagnosticFindings = findings,
    lastScannedAtUtc = '2026-05-01T00:00:00Z',
}: {
    diagnosticFindings?: unknown;
    lastScannedAtUtc?: string | null;
} = {}) {
    currentDiagnosticFindings = diagnosticFindings;
    currentLastScannedAtUtc = lastScannedAtUtc;
    vi.mocked(apiGet).mockImplementation((path: string) => {
        if (path === '/diagnostics/findings') {
            return Promise.resolve({ findings: currentDiagnosticFindings, lastScannedAt: currentLastScannedAtUtc });
        }
        return Promise.reject(new Error(`Unexpected GET ${path}`));
    });
}

function mockDiagnosticPost({
    scanFindings = findings,
    scanLastScannedAtUtc = '2026-05-02T00:00:00Z',
}: {
    scanFindings?: unknown;
    scanLastScannedAtUtc?: string;
} = {}) {
    vi.mocked(apiPost).mockImplementation((path: string) => {
        if (path === '/jobs/execute-immediate') {
            currentDiagnosticFindings = scanFindings;
            currentLastScannedAtUtc = scanLastScannedAtUtc;
            return Promise.resolve('execution-1');
        }
        return Promise.resolve({ success: true, message: 'Repair applied' });
    });
}

function getCallCount(path: string) {
    return vi.mocked(apiGet).mock.calls.filter(([calledPath]) => calledPath === path).length;
}

beforeEach(() => {
    authState.role = Role.Admin;
    vi.clearAllMocks();
    mockDiagnosticGet();
    mockDiagnosticPost();
});

afterEach(() => {
    vi.restoreAllMocks();
});

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
    });
    return (
        <MemoryRouter>
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <ToastProvider>
                        <ToastStateProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </TooltipProvider>
            </QueryClientProvider>
        </MemoryRouter>
    );
}

describe('DiagnosticsSection', () => {
    it('renders findings grouped by severity in Error, Warning, Info order', async () => {
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Missing default job')).toBeInTheDocument();
        });

        const groups = Array.from(document.querySelectorAll('.diagnostics__group'));
        expect(groups.map((group) => group.getAttribute('data-severity'))).toEqual(['Error', 'Warning', 'Info']);

        expect(screen.getByText('Empty quality profile')).toBeInTheDocument();
        expect(screen.getByText('Optional setup step skipped')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Job' })).toHaveAttribute('href', '/admin/jobs');
        expect(screen.queryByText('Job:sync')).not.toBeInTheDocument();
    });

    it('shows a Fix button only on findings that carry a repairId', async () => {
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Missing default job')).toBeInTheDocument();
        });

        const errorRow = document.querySelector('tr[data-row-id="f-err"]') as HTMLElement;
        const infoRow = document.querySelector('tr[data-row-id="f-info"]') as HTMLElement;

        expect(within(errorRow).getByRole('button', { name: /Fix/i })).toBeInTheDocument();
        expect(within(infoRow).queryByRole('button', { name: /Fix/i })).not.toBeInTheDocument();
    });

    it('applies the repair endpoint after confirming and refreshes the findings', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Missing default job')).toBeInTheDocument();
        });
        expect(getCallCount('/diagnostics/findings')).toBe(1);

        const fixButton = document.querySelector('[data-action="repair-finding"][data-target="f-err"]') as HTMLElement;
        await user.click(fixButton);

        const confirmButton = document.querySelector('[data-action="confirm-delete"]') as HTMLElement;
        await user.click(confirmButton);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/diagnostics/findings/f-err/repair');
        });
        await waitFor(() => {
            expect(getCallCount('/diagnostics/findings')).toBe(2);
        });
    });

    it('starts a doctor scan job when Run now is clicked', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Missing default job')).toBeInTheDocument();
        });

        const runButton = document.querySelector('[data-action="run-diagnostic-scan"]') as HTMLElement;
        await user.click(runButton);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
                jobType: 'DiagnosticCheck',
            });
        });
    });

    it('shows the empty state when there are no findings', async () => {
        mockDiagnosticGet({ diagnosticFindings: [] });
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No issues found.')).toBeInTheDocument();
        });

        expect(document.querySelector('.diagnostics__group')).toBeNull();
    });

    it('labels the section using the doctor status timestamp', async () => {
        mockDiagnosticGet({ lastScannedAtUtc: '2026-05-03T00:00:00Z' });
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Missing default job')).toBeInTheDocument();
        });

        const lastScanned = screen.getByTestId('diagnostics-last-scanned');
        expect(lastScanned.textContent).toContain('Last scanned:');
        const time = lastScanned.querySelector('time');
        expect(time).not.toBeNull();
        expect(time).toHaveAttribute('dateTime', '2026-05-03T00:00:00Z');
    });

    it('shows Last scanned: Never when there is no doctor status', async () => {
        mockDiagnosticGet({ diagnosticFindings: [], lastScannedAtUtc: null });
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No issues found.')).toBeInTheDocument();
        });

        const lastScanned = screen.getByTestId('diagnostics-last-scanned');
        expect(lastScanned.textContent).toContain('Never');
        expect(lastScanned.querySelector('time')).toBeNull();
    });

    it('updates Last scanned after an all-clear scan', async () => {
        const user = userEvent.setup();
        mockDiagnosticGet({ diagnosticFindings: [], lastScannedAtUtc: null });
        mockDiagnosticPost({ scanFindings: [], scanLastScannedAtUtc: '2026-05-04T00:00:00Z' });

        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No issues found.')).toBeInTheDocument();
        });

        expect(screen.getByTestId('diagnostics-last-scanned').textContent).toContain('Never');

        const runButton = document.querySelector('[data-action="run-diagnostic-scan"]') as HTMLElement;
        await user.click(runButton);

        await waitFor(() => {
            const time = screen.getByTestId('diagnostics-last-scanned').querySelector('time');
            expect(time).toHaveAttribute('dateTime', '2026-05-04T00:00:00Z');
        });
    });

    it('hides the Fix and Run now actions for non-admin users', async () => {
        authState.role = Role.Viewer;
        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Missing default job')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="run-diagnostic-scan"]')).toBeNull();
        expect(document.querySelector('[data-action="repair-finding"]')).toBeNull();
    });

    it('renders the affected entity as a readable, route-linked cell', async () => {
        const contentGuid = 'a1fc4c2b-1234-4def-89ab-0123456789ab';
        mockDiagnosticGet({
            diagnosticFindings: [
                {
                    id: 'f-content',
                    diagnosticCheckId: 'core.history.grab-fail-loop',
                    severity: 'Warning',
                    title: 'A content item is stuck in a grab-and-fail loop',
                    detail: 'Every candidate release keeps failing.',
                    affectedEntity: `ContentHierarchyItem:${contentGuid}`,
                    repairId: null,
                    detectedAt: '2026-05-01T00:00:00Z',
                },
                {
                    id: 'f-indexer',
                    diagnosticCheckId: 'core.health.integrations',
                    severity: 'Warning',
                    title: 'Indexer is degraded',
                    detail: 'The indexer is rate limited.',
                    affectedEntity: 'IndexerConnection:99f4c2b3-1111-2222-3333-444455556666',
                    repairId: null,
                    detectedAt: '2026-05-01T00:00:00Z',
                },
                {
                    id: 'f-client',
                    diagnosticCheckId: 'core.health.integrations',
                    severity: 'Warning',
                    title: 'Download client is unhealthy',
                    detail: 'The download client is offline.',
                    affectedEntity: 'DownloadClient:5cfcabcd-7777-8888-9999-aaaabbbbcccc',
                    repairId: null,
                    detectedAt: '2026-05-01T00:00:00Z',
                },
                {
                    id: 'f-unknown',
                    diagnosticCheckId: 'core.example',
                    severity: 'Info',
                    title: 'Something needs attention',
                    detail: 'A route-less entity type.',
                    affectedEntity: 'WebhookSubscription:abc123',
                    repairId: null,
                    detectedAt: '2026-05-01T00:00:00Z',
                },
            ],
        });

        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Indexer is degraded')).toBeInTheDocument();
        });

        const contentLink = screen.getByRole('link', { name: 'Hierarchy item' });
        expect(contentLink).toHaveAttribute('href', `/library/${contentGuid}`);
        expect(screen.getByText('a1fc4c2b')).toBeInTheDocument();
        expect(screen.queryByText(`ContentHierarchyItem:${contentGuid}`)).not.toBeInTheDocument();

        expect(screen.getByRole('link', { name: 'Indexer' })).toHaveAttribute('href', '/admin/indexers');
        expect(screen.getByRole('link', { name: 'Download client' })).toHaveAttribute(
            'href',
            '/admin/download-clients',
        );

        expect(screen.getByText('Webhook subscription')).toBeInTheDocument();
        expect(screen.queryByText('WebhookSubscription:abc123')).not.toBeInTheDocument();
        expect(
            within(document.querySelector('tr[data-row-id="f-unknown"]') as HTMLElement).queryByRole('link'),
        ).toBeNull();
    });

    it('keeps the raw affected token driving the repair flow while rendering a readable cell', async () => {
        const user = userEvent.setup();
        const contentGuid = 'a1fc4c2b-1234-4def-89ab-0123456789ab';
        mockDiagnosticGet({
            diagnosticFindings: [
                {
                    id: 'f-repair',
                    diagnosticCheckId: 'core.history.grab-fail-loop',
                    severity: 'Warning',
                    title: 'A content item is stuck in a grab-and-fail loop',
                    detail: 'Every candidate release keeps failing.',
                    affectedEntity: `ContentHierarchyItem:${contentGuid}`,
                    repairId: 'reset-content',
                    detectedAt: '2026-05-01T00:00:00Z',
                },
            ],
        });

        render(
            <TestWrapper>
                <DiagnosticsSection />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('link', { name: 'Hierarchy item' })).toBeInTheDocument();
        });
        expect(screen.queryByText(`ContentHierarchyItem:${contentGuid}`)).not.toBeInTheDocument();

        const fixButton = document.querySelector(
            '[data-action="repair-finding"][data-target="f-repair"]',
        ) as HTMLElement;
        await user.click(fixButton);

        const confirmButton = document.querySelector('[data-action="confirm-delete"]') as HTMLElement;
        await user.click(confirmButton);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/diagnostics/findings/f-repair/repair');
        });
    });
});
