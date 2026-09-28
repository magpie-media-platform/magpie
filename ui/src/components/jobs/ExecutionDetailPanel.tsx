import { type ReactNode } from 'react';
import { LocalContentLink, LocalContentItemReference } from '@/components/content/LocalContentPreview';
import { ArrowsSplitIcon, CheckCircleIcon, CircleNotchIcon, InfoIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { Link } from 'react-router-dom';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import type { ApiJobExecution } from '@/hooks/useJobs';
import { useJobExecutionAudits, useJobExecutionTreeActivity } from '@/hooks/useMagpieActivity';
import { useScheduleTrigger } from '@/hooks/useScheduleTriggers';
import { humanizeCustomKey } from '@/lib/contentTypeDisplay';
import { formatDateTime } from '@/lib/dateFormatting';
import {
    calculateDuration,
    formatLogTime,
    humanizeJobType,
    mapStatusToDisplay,
    type JobExecutionStatusDisplay,
} from '@/lib/jobExecution';
import { CORRELATION_ID_PARAM, JOB_PARAM, ROOTS_ONLY_PARAM } from '@/pages/JobsPage/jobsSearchParams';
import { isGuid } from '@/lib/isGuid';

import './ExecutionDetailPanel.css';

const RELATED_IN_LEDGER = 6;

interface ExecutionDetailPanelProps {
    execution: ApiJobExecution;

    active?: boolean;
}

export function ExecutionDetailPanel({ execution, active = true }: ExecutionDetailPanelProps) {
    const displayStatus = mapStatusToDisplay(execution.status, execution.notBeforeAt);

    return (
        <section
            className="execution-detail"
            aria-label={`${execution.jobType} execution details`}
            data-testid="execution-detail"
        >
            <ExecutionDetails execution={execution} displayStatus={displayStatus} active={active} />
            <ExecutionLedger execution={execution} displayStatus={displayStatus} active={active} />
        </section>
    );
}

ExecutionDetailPanel.displayName = 'ExecutionDetailPanel';

type DisplayStatus = JobExecutionStatusDisplay;

interface ExecutionSectionProps {
    execution: ApiJobExecution;
    displayStatus: DisplayStatus;
    active: boolean;
}

function ExecutionLedger({ execution, displayStatus, active }: ExecutionSectionProps) {
    const {
        audits,
        hasPrevious: hasPreviousAudits,
        loadPrevious: loadPreviousAudits,
        isLoadingPrevious: isLoadingPreviousAudits,
        hasMore: hasMoreAudits,
        loadMore: loadMoreAudits,
        isLoadingMore: isLoadingMoreAudits,
        error: auditError,
    } = useJobExecutionAudits(active ? execution.id : null);
    const { activity, error: relatedError } = useJobExecutionTreeActivity(active ? execution.id : null);

    const entries: LedgerEntry[] = [];
    const anchor = new Date(execution.startedAt ?? execution.createdAt ?? Date.now()).getTime();
    if (execution.createdAt) entries.push({ key: 'created', at: execution.createdAt, tone: 'info', text: 'Created' });
    if (execution.startedAt) entries.push({ key: 'started', at: execution.startedAt, tone: 'info', text: 'Started' });

    for (const audit of audits ?? []) {
        entries.push({
            key: `audit-${audit.id}`,
            at: audit.timestamp,
            tone: 'info',
            text: audit.detail || humanizeCustomKey(audit.label) || audit.label,
        });
    }

    const relatedAll = (activity?.entries ?? []).filter((related) => related.id !== execution.id);
    const relatedNear = [...relatedAll]
        .sort(
            (a, b) =>
                Math.abs(new Date(a.timestamp).getTime() - anchor) - Math.abs(new Date(b.timestamp).getTime() - anchor),
        )
        .slice(0, RELATED_IN_LEDGER);
    for (const related of relatedNear) {
        entries.push({
            key: `job-${related.id}`,
            at: related.timestamp,
            tone: 'job',
            text: humanizeJobType(related.label),
            href: `?${JOB_PARAM}=${related.id}`,
        });
    }

    if (execution.completedAt) {
        entries.push({
            key: 'completed',
            at: execution.completedAt,
            tone: displayStatus === 'failed' ? 'error' : 'done',
            text:
                displayStatus === 'failed'
                    ? (execution.statusMessage ?? 'Failed')
                    : displayStatus === 'cancelled'
                      ? 'Cancelled'
                      : 'Completed successfully',
        });
    }

    const rank = (entry: LedgerEntry) => (entry.key === 'created' ? 0 : entry.key === 'completed' ? 2 : 1);
    entries.sort((a, b) => {
        const byRank = rank(a) - rank(b);
        if (byRank !== 0) return byRank;
        return new Date(a.at).getTime() - new Date(b.at).getTime();
    });
    const hidden = relatedAll.length - relatedNear.length;
    const error = auditError ?? relatedError;

    return (
        <div className="execution-ledger" data-testid="execution-detail__ledger">
            {error && <Alert variant="error">Failed to load the full record: {error.message}</Alert>}
            <ol className="execution-ledger__list" role="list">
                {entries.map((entry) => (
                    <li key={entry.key} className="execution-ledger__row" data-tone={entry.tone}>
                        <time className="execution-ledger__time" dateTime={entry.at} title={formatDateTime(entry.at)}>
                            {formatLogTime(entry.at)}
                        </time>
                        <LedgerIcon tone={entry.tone} />
                        {entry.href ? (
                            <Link className="execution-ledger__text execution-ledger__link" to={{ search: entry.href }}>
                                {entry.text}
                            </Link>
                        ) : (
                            <span className="execution-ledger__text">{entry.text}</span>
                        )}
                    </li>
                ))}
                {displayStatus === 'running' && (
                    <li
                        className="execution-ledger__row"
                        data-tone="live"
                        role="status"
                        aria-live="polite"
                        data-testid="execution-detail__live"
                    >
                        <span className="execution-ledger__time execution-ledger__time--live">now</span>
                        <CircleNotchIcon
                            weight="bold"
                            className="execution-ledger__icon execution-ledger__icon--spin"
                            aria-hidden="true"
                        />
                        <span className="execution-ledger__text">{execution.statusMessage ?? 'Working'}</span>
                    </li>
                )}
            </ol>
            {hidden > 0 && execution.correlationId && (
                <Link
                    className="execution-ledger__more"
                    to={{ search: `?${CORRELATION_ID_PARAM}=${execution.correlationId}&${ROOTS_ONLY_PARAM}=0` }}
                >
                    {hidden} more related {hidden === 1 ? 'job' : 'jobs'}
                </Link>
            )}
            {hasPreviousAudits && (
                <Button
                    variant="outline"
                    size="sm"
                    className="execution-ledger__load-more"
                    onClick={() => void loadPreviousAudits()}
                    loading={isLoadingPreviousAudits}
                >
                    Load previous audit records
                </Button>
            )}
            {hasMoreAudits && (
                <Button
                    variant="outline"
                    size="sm"
                    className="execution-ledger__load-more"
                    onClick={() => void loadMoreAudits()}
                    loading={isLoadingMoreAudits}
                >
                    Load more audit records
                </Button>
            )}
        </div>
    );
}

interface LedgerEntry {
    key: string;
    at: string;
    tone: 'info' | 'done' | 'error' | 'job';
    text: string;
    href?: string;
}

function LedgerIcon({ tone }: { tone: LedgerEntry['tone'] }) {
    if (tone === 'done') {
        return <CheckCircleIcon weight="regular" className="execution-ledger__icon" aria-hidden="true" />;
    }
    if (tone === 'error') {
        return <WarningCircleIcon weight="regular" className="execution-ledger__icon" aria-hidden="true" />;
    }
    if (tone === 'job') {
        return <ArrowsSplitIcon weight="regular" className="execution-ledger__icon" aria-hidden="true" />;
    }
    return <InfoIcon weight="regular" className="execution-ledger__icon" aria-hidden="true" />;
}

function ExecutionDetails({ execution, displayStatus, active }: ExecutionSectionProps) {
    const eventName = execution.triggerContext?.['event.name'];
    const publisherId = execution.triggerContext?.['event.jobExecutionId'];
    const { trigger: schedule } = useScheduleTrigger(
        execution.scheduleTriggerId ?? '',
        active && Boolean(execution.scheduleTriggerId),
    );

    const rows: { key: string; label: string; value: ReactNode }[] = [];

    let trigger: ReactNode = 'Manual';
    if (execution.eventTriggerId) {
        trigger = (
            <>
                {'Event '}
                <span className="execution-details__accent">{eventName ?? 'unknown'}</span>
                {publisherId && (
                    <Button asChild variant="outline" size="sm" className="execution-details__action">
                        <Link to={{ search: `?${JOB_PARAM}=${publisherId}` }}>Parent job</Link>
                    </Button>
                )}
            </>
        );
    } else if (execution.scheduleTriggerId) {
        trigger = (
            <>
                {'Schedule '}
                <span className="execution-details__accent">{schedule?.name ?? ''}</span>
            </>
        );
    }
    rows.push({ key: 'trigger', label: 'Trigger', value: trigger });

    if (displayStatus !== 'running' && execution.startedAt && execution.completedAt) {
        rows.push({
            key: 'duration',
            label: 'Duration',
            value: calculateDuration(execution.startedAt, execution.completedAt),
        });
    }

    const attemptLabel = execution.attempt?.toString() ?? '—';
    if ((execution.attempt ?? 1) > 1 || execution.originalExecutionId) {
        rows.push({
            key: 'attempt',
            label: 'Attempt',
            value: execution.originalExecutionId ? (
                <>
                    <span>{attemptLabel}</span>
                    <Button asChild variant="outline" size="sm" className="execution-details__action">
                        <Link
                            to={{
                                search: `?${CORRELATION_ID_PARAM}=${execution.correlationId ?? ''}&${ROOTS_ONLY_PARAM}=0`,
                            }}
                        >
                            View all attempts
                        </Link>
                    </Button>
                </>
            ) : (
                attemptLabel
            ),
        });
    }

    if (execution.nodeId) rows.push({ key: 'node', label: 'Node', value: execution.nodeId });

    for (const [key, value] of Object.entries(execution.parameters ?? {})) {
        rows.push({
            key: `param-${key}`,
            label: humanizeCustomKey(key) || key,
            value:
                key === 'contentHierarchyItemId' && isGuid(value) ? (
                    <LocalContentLink contentId={value}>{value}</LocalContentLink>
                ) : key === 'contentItemId' && isGuid(value) ? (
                    <LocalContentItemReference contentItemId={value}>{value}</LocalContentItemReference>
                ) : (
                    value
                ),
        });
    }

    return (
        <dl className="execution-details" data-testid="execution-detail__details">
            {rows.map((row) => (
                <div key={row.key} className="execution-details__row">
                    <dt className="execution-details__key" title={row.label}>
                        {row.label}
                    </dt>
                    {typeof row.value === 'string' ? (
                        <dd className="execution-details__value execution-details__value--text" title={row.value}>
                            {row.value}
                        </dd>
                    ) : (
                        <dd className="execution-details__value">{row.value}</dd>
                    )}
                </div>
            ))}
        </dl>
    );
}

export default ExecutionDetailPanel;
