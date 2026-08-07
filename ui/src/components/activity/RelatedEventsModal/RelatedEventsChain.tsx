import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { ArrowDownIcon, CheckCircleIcon, ClockIcon, InfoIcon, WarningIcon, XCircleIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Pager } from '@/components/ui/Pager';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    MagpieActivityEntryKind,
    useContentMagpieActivity,
    useJobExecutionTreeActivity,
    type MagpieActivityEntry,
} from '@/hooks/useMagpieActivity';
import { formatDateTime } from '@/lib/dateFormatting';
import { getMagpieActivityStatusBadge } from '@/lib/magpieActivityStatusBadge';

import { entryLabel } from './entryLabel';

import './RelatedEventsModal.css';

const CHAIN_PAGE_SIZE = 20;

function matchesCorrelation(entry: MagpieActivityEntry, correlationId: string | null): boolean {
    return (
        !!entry.correlationId && !!correlationId && entry.correlationId.toLowerCase() === correlationId.toLowerCase()
    );
}

function statusIcon(status?: string | null) {
    switch (status) {
        case 'Completed':
            return (
                <CheckCircleIcon
                    weight="fill"
                    className="related-events__icon related-events__icon--ok"
                    aria-hidden="true"
                />
            );
        case 'Failed':
            return (
                <XCircleIcon
                    weight="fill"
                    className="related-events__icon related-events__icon--bad"
                    aria-hidden="true"
                />
            );
        case 'Cancelled':
        case 'CancellationRequested':
            return (
                <WarningIcon
                    weight="fill"
                    className="related-events__icon related-events__icon--warn"
                    aria-hidden="true"
                />
            );
        case 'Running':
            return (
                <ClockIcon
                    weight="fill"
                    className="related-events__icon related-events__icon--info"
                    aria-hidden="true"
                />
            );
        case null:
        case undefined:
        default:
            return <InfoIcon weight="duotone" className="related-events__icon" aria-hidden="true" />;
    }
}

export interface RelatedEventsChainProps {
    executionId: string | null;

    correlationId?: string | null;

    active?: boolean;

    selfEntry?: MagpieActivityEntry | null;

    seedEntityId?: string | null;
}

export function RelatedEventsChain({
    executionId,
    correlationId = null,
    active = true,
    selfEntry = null,
    seedEntityId = null,
}: RelatedEventsChainProps): ReactNode {
    const [page, setPage] = useState(1);
    const contextParams = useMemo(() => ({ pageNumber: 1, pageSize: 1000 }), []);
    const shouldLoadContext = active && !!correlationId && !!seedEntityId;
    const {
        activity: contentActivity,
        isLoading: isLoadingContext,
        error: contextError,
    } = useContentMagpieActivity(shouldLoadContext ? seedEntityId : null, contextParams);
    const resolvedExecutionId =
        executionId ??
        contentActivity?.entries.find(
            (entry) => entry.kind === MagpieActivityEntryKind.JobExecution && matchesCorrelation(entry, correlationId),
        )?.id ??
        null;
    const {
        activity,
        isLoading: isLoadingTree,
        error: treeError,
    } = useJobExecutionTreeActivity(active ? resolvedExecutionId : null);
    const isLoading = (shouldLoadContext && isLoadingContext) || (!!resolvedExecutionId && isLoadingTree);
    const error = treeError ?? (shouldLoadContext ? contextError : null);

    const selfId = selfEntry?.id ?? null;
    const selfKind = selfEntry?.kind ?? null;

    const ordered = useMemo(() => {
        const entries = new Map<string, MagpieActivityEntry>();
        const addEntry = (entry: MagpieActivityEntry) => {
            entries.set(`${entry.kind}-${entry.id}`, entry);
        };

        for (const entry of activity?.entries ?? []) {
            addEntry(entry);
        }
        for (const entry of contentActivity?.entries ?? []) {
            if (matchesCorrelation(entry, correlationId)) {
                addEntry(entry);
            }
        }
        if (selfEntry) {
            addEntry(selfEntry);
        }
        return [...entries.values()].sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime());
    }, [activity?.entries, contentActivity?.entries, correlationId, selfEntry]);

    useEffect(() => {
        if (selfId == null) {
            setPage(1);
            return;
        }
        const index = ordered.findIndex((e) => e.id === selfId && e.kind === selfKind);
        setPage(index < 0 ? 1 : Math.floor(index / CHAIN_PAGE_SIZE) + 1);
    }, [ordered, selfId, selfKind]);

    const isCapped = (activity?.capped ?? false) || (contentActivity?.capped ?? false);
    const totalPages = Math.max(1, Math.ceil(ordered.length / CHAIN_PAGE_SIZE));
    const safePage = Math.min(page, totalPages);
    const start = (safePage - 1) * CHAIN_PAGE_SIZE;
    const pageEntries = ordered.slice(start, start + CHAIN_PAGE_SIZE);

    return (
        <div className="related-events__body">
            {error ? (
                <p className="related-events__error" role="alert">
                    Failed to load related events: {error.message}
                </p>
            ) : isLoading ? (
                <div className="related-events__skeletons">
                    {Array.from({ length: 3 }).map((_, i) => (
                        <Skeleton key={i} height={56} />
                    ))}
                </div>
            ) : ordered.length === 0 ? (
                <p className="related-events__empty">No related events for this chain.</p>
            ) : (
                <>
                    {isCapped && (
                        <p className="related-events__capped-note" data-testid="related-events-capped" role="status">
                            <InfoIcon weight="duotone" aria-hidden="true" />
                            Only the most recent activity is shown.
                        </p>
                    )}
                    <ol className="related-events__chain" data-testid="related-events__chain">
                        {pageEntries.map((entry, index) => (
                            <RelatedEventStep
                                key={`${entry.kind}-${entry.id}`}
                                entry={entry}
                                isLast={index === pageEntries.length - 1}
                                isSelf={selfId != null && entry.id === selfId && entry.kind === selfKind}
                            />
                        ))}
                    </ol>

                    {ordered.length === 1 && <p className="related-events__empty">No other activity in this trace.</p>}

                    {ordered.length > CHAIN_PAGE_SIZE && (
                        <Pager
                            className="related-events__pager"
                            page={safePage}
                            pageCount={totalPages}
                            onFirst={() => setPage(1)}
                            onPrevious={() => setPage((p) => Math.max(1, p - 1))}
                            onNext={() => setPage((p) => Math.min(totalPages, p + 1))}
                            onLast={() => setPage(totalPages)}
                        />
                    )}
                </>
            )}
        </div>
    );
}

RelatedEventsChain.displayName = 'RelatedEventsChain';

interface RelatedEventStepProps {
    entry: MagpieActivityEntry;
    isLast: boolean;
    isSelf: boolean;
}

function RelatedEventStep({ entry, isLast, isSelf }: RelatedEventStepProps) {
    const isJob = entry.kind === MagpieActivityEntryKind.JobExecution;
    const { variant, label: statusLabel } = getMagpieActivityStatusBadge(entry.status);
    const label = entryLabel(entry);

    return (
        <li
            className="related-events__step"
            data-kind={isJob ? 'job' : 'audit'}
            data-id={entry.id}
            data-self={isSelf || undefined}
        >
            <div
                className={`related-events__step-card${isSelf ? ' related-events__step-card--self' : ''}`}
                data-testid={isSelf ? 'related-events__self-step' : undefined}
            >
                <div className="related-events__step-icon">{statusIcon(entry.status)}</div>
                <div className="related-events__step-body">
                    <div className="related-events__step-header">
                        <span className="related-events__step-label" title={entry.label}>
                            {label}
                        </span>
                        {isSelf && (
                            <Badge variant="info" size="sm">
                                Selected activity
                            </Badge>
                        )}
                        {isJob && entry.status ? (
                            <Badge variant={variant} size="sm">
                                {statusLabel}
                            </Badge>
                        ) : (
                            <Badge variant="default" size="sm">
                                Recorded
                            </Badge>
                        )}
                        <Badge variant="info" size="sm" badgeStyle="outline">
                            {entry.category}
                        </Badge>
                    </div>
                    <span className="related-events__step-time" title={entry.timestamp}>
                        {formatDateTime(entry.timestamp)}
                    </span>
                    {entry.detail && <p className="related-events__step-detail">{entry.detail}</p>}
                </div>
            </div>
            {!isLast && (
                <div className="related-events__connector" aria-hidden="true">
                    <ArrowDownIcon weight="bold" />
                </div>
            )}
        </li>
    );
}

export default RelatedEventsChain;
