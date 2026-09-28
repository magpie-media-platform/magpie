import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { LocalContentLink } from '@/components/content/LocalContentPreview';
import { ArrowClockwiseIcon, InfoIcon, WarningIcon, WarningOctagonIcon, WrenchIcon } from '@phosphor-icons/react';

import { Role, useAuth } from '@/auth';
import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';
import {
    useDiagnosticFindings,
    type DiagnosticFinding,
    type DiagnosticFindingSeverity,
} from '@/hooks/useDiagnosticFindings';
import { formatDateTime } from '@/lib/dateFormatting';
import { formatRelativeTime } from '@/lib/formatTime';

import { describeAffectedEntity } from './affectedEntity';

import './DiagnosticsSection.css';

const SEVERITY_ORDER: DiagnosticFindingSeverity[] = ['Error', 'Warning', 'Info'];

const SEVERITY_LABELS: Record<DiagnosticFindingSeverity, string> = {
    Error: 'Errors',
    Warning: 'Warnings',
    Info: 'Info',
};

const SEVERITY_BADGE: Record<DiagnosticFindingSeverity, BadgeVariant> = {
    Error: 'unhealthy',
    Warning: 'warning',
    Info: 'info',
};

function AffectedEntity({ token }: { token: string | null }): ReactNode {
    const display = describeAffectedEntity(token);
    if (display === null) {
        return <span className="diagnostics__affected-empty">-</span>;
    }

    const reference = display.reference ? <span className="diagnostics__affected-ref">{display.reference}</span> : null;

    if (token?.startsWith('ContentHierarchyItem:'))
        return (
            <span className="diagnostics__affected-entity">
                <LocalContentLink
                    contentId={token.slice('ContentHierarchyItem:'.length)}
                    className="diagnostics__affected-link"
                >
                    {display.label}
                </LocalContentLink>
                {reference}
            </span>
        );

    if (display.to) {
        return (
            <span className="diagnostics__affected-entity">
                <Link to={display.to} className="diagnostics__affected-link">
                    {display.label}
                </Link>
                {reference}
            </span>
        );
    }

    return (
        <span className="diagnostics__affected-entity">
            <span className="diagnostics__affected-label">{display.label}</span>
            {reference}
        </span>
    );
}

function severityIcon(severity: DiagnosticFindingSeverity) {
    switch (severity) {
        case 'Error':
            return (
                <WarningOctagonIcon
                    weight="fill"
                    className="diagnostics__severity-icon diagnostics__severity-icon--error"
                />
            );
        case 'Warning':
            return (
                <WarningIcon weight="fill" className="diagnostics__severity-icon diagnostics__severity-icon--warning" />
            );
        case 'Info':
        default:
            return <InfoIcon weight="fill" className="diagnostics__severity-icon diagnostics__severity-icon--info" />;
    }
}

export function DiagnosticsSection(): ReactNode {
    const { hasRole } = useAuth();
    const { toast } = useToast();
    const { findings, lastScannedAt, isLoading, runScan, isScanning, applyRepair, isRepairing } =
        useDiagnosticFindings();
    const isAdmin = hasRole(Role.Admin);

    const [pendingRepair, setPendingRepair] = useState<DiagnosticFinding | null>(null);

    const grouped = useMemo(() => {
        return SEVERITY_ORDER.map((severity) => ({
            severity,
            rows: findings.filter((finding) => finding.severity === severity),
        })).filter((group) => group.rows.length > 0);
    }, [findings]);

    const handleRunScan = useCallback(async () => {
        try {
            await runScan();
            toast({ title: 'Doctor scan complete', variant: 'success' });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Scan failed';
            toast({ title: 'Doctor scan failed', description: message, variant: 'error' });
        }
    }, [runScan, toast]);

    const handleConfirmRepair = useCallback(async () => {
        if (!pendingRepair?.repairId) {
            return;
        }
        const finding = pendingRepair;
        try {
            const result = await applyRepair({ findingId: finding.id });
            toast({
                title: finding.title,
                description: result.message,
                variant: result.success ? 'success' : 'error',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Repair failed';
            toast({ title: finding.title, description: message, variant: 'error' });
        } finally {
            setPendingRepair(null);
        }
    }, [pendingRepair, applyRepair, toast]);

    const lastScannedLabel = lastScannedAt ? (
        <time dateTime={lastScannedAt} title={formatDateTime(lastScannedAt)}>
            {formatRelativeTime(lastScannedAt)}
        </time>
    ) : (
        'Never'
    );

    return (
        <div className="diagnostics" data-component="diagnostics">
            <div className="diagnostics__actions">
                <span className="diagnostics__last-scanned" data-testid="diagnostics-last-scanned">
                    Last scanned: {lastScannedLabel}
                </span>
                {isAdmin && (
                    <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        onClick={handleRunScan}
                        loading={isScanning}
                        data-action="run-diagnostic-scan"
                    >
                        <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                        Run now
                    </Button>
                )}
            </div>

            {isLoading ? (
                <div className="diagnostics__loading" data-testid="diagnostics-loading">
                    <Skeleton height={120} />
                </div>
            ) : grouped.length === 0 ? (
                <EmptyState title="No issues found." size="sm" data-component="diagnostics-empty" />
            ) : (
                grouped.map((group) => (
                    <section
                        key={group.severity}
                        className="diagnostics__group"
                        aria-label={SEVERITY_LABELS[group.severity]}
                        data-severity={group.severity}
                    >
                        <header className="diagnostics__group-header">
                            {severityIcon(group.severity)}
                            <h3 className="diagnostics__group-title">{SEVERITY_LABELS[group.severity]}</h3>
                            <Badge variant={SEVERITY_BADGE[group.severity]} size="sm">
                                {group.rows.length}
                            </Badge>
                        </header>
                        <table className="diagnostics__table">
                            <thead>
                                <tr>
                                    <th scope="col">Issue</th>
                                    <th scope="col">Affected</th>
                                    <th scope="col" className="diagnostics__th-action">
                                        <span className="visually-hidden">Repair</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                {group.rows.map((finding) => (
                                    <tr key={finding.id} data-row-id={finding.id} data-severity={finding.severity}>
                                        <td>
                                            <span className="diagnostics__row-title">{finding.title}</span>
                                            <p className="diagnostics__row-detail">{finding.detail}</p>
                                        </td>
                                        <td className="diagnostics__affected">
                                            <AffectedEntity token={finding.affectedEntity} />
                                        </td>
                                        <td className="diagnostics__action-cell">
                                            {isAdmin && finding.repairId ? (
                                                <Button
                                                    type="button"
                                                    size="sm"
                                                    variant="secondary"
                                                    onClick={() => setPendingRepair(finding)}
                                                    disabled={isRepairing}
                                                    data-action="repair-finding"
                                                    data-target={finding.id}
                                                >
                                                    <WrenchIcon weight="bold" aria-hidden="true" />
                                                    Fix
                                                </Button>
                                            ) : null}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </section>
                ))
            )}

            <DeleteConfirmDialog
                open={pendingRepair !== null}
                onConfirm={handleConfirmRepair}
                onCancel={() => setPendingRepair(null)}
                itemName={pendingRepair?.title ?? ''}
                itemType="repair"
                title="Apply repair?"
                description={
                    pendingRepair
                        ? `Apply the repair for "${pendingRepair.title}"? Magpie will attempt to resolve this issue.`
                        : ''
                }
                isDeleting={isRepairing}
                confirmVariant="primary"
                confirmLabel="Fix"
                confirmBusyLabel="Fixing..."
                confirmIcon={<WrenchIcon weight="bold" aria-hidden="true" />}
            />
        </div>
    );
}

DiagnosticsSection.displayName = 'DiagnosticsSection';

export default DiagnosticsSection;
