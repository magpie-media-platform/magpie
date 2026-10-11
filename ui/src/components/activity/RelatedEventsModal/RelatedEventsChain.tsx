import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { ArrowDownIcon, CheckCircleIcon, ClockIcon, InfoIcon, WarningIcon, XCircleIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Pager } from '@/components/ui/Pager';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    MagpieActivityEntryKind,
    useContentMagpieActivity,
    useCorrelatedMagpieActivity,
    useJobExecutionTreeActivity,
    type JobExecutionTreeNode,
    type MagpieActivityEntry,
} from '@/hooks/useMagpieActivity';
import { formatDateTime } from '@/lib/dateFormatting';
import { getMagpieActivityStatusBadge } from '@/lib/magpieActivityStatusBadge';

import { entryLabel } from './entryLabel';
import { LocalContentLink, LocalContentItemReference } from '@/components/content/LocalContentPreview';

import './RelatedEventsModal.css';

const RELATED_PAGE_SIZE = 20;

function matchesCorrelation(entry: MagpieActivityEntry, correlationId: string | null): boolean {
    return (
        !!entry.correlationId && !!correlationId && entry.correlationId.toLowerCase() === correlationId.toLowerCase()
    );
}

interface ChainNode {
    entry: MagpieActivityEntry;
    children: ChainNode[];
    parent?: MagpieActivityEntry;
}

function buildChainNode(
    node: JobExecutionTreeNode,
    displayById: Map<string, MagpieActivityEntry>,
    parent?: MagpieActivityEntry,
): ChainNode | null {
    const entry = displayById.get(node.id);
    if (!entry) {
        return null;
    }
    const children: ChainNode[] = [];
    for (const child of node.children) {
        const built = buildChainNode(child, displayById, entry);
        if (built) {
            children.push(built);
        }
    }
    return { entry, children, parent };
}

function flattenChainNodes(nodes: ChainNode[], entries: ChainNode[] = []): ChainNode[] {
    for (const node of nodes) {
        entries.push(node);
        flattenChainNodes(node.children, entries);
    }
    return entries;
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
    const [alsoPage, setAlsoPage] = useState(1);
    const [chainPage, setChainPage] = useState(1);
    const contextParams = useMemo(() => ({ pageNumber: 1, pageSize: 1000 }), []);
    const shouldLoadContext = active && !!correlationId;
    const seeded = useContentMagpieActivity(shouldLoadContext ? seedEntityId : null, contextParams);
    const correlated = useCorrelatedMagpieActivity(
        shouldLoadContext && !seedEntityId ? correlationId : null,
        contextParams,
    );
    const {
        activity: contentActivity,
        isLoading: isLoadingContext,
        error: contextError,
    } = seedEntityId ? seeded : correlated;
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

    const chainIds = useMemo(() => new Set((activity?.entries ?? []).map((entry) => entry.id)), [activity?.entries]);

    const displayById = useMemo(() => {
        const map = new Map<string, MagpieActivityEntry>();
        for (const entry of activity?.entries ?? []) {
            map.set(entry.id, entry);
        }
        for (const entry of contentActivity?.entries ?? []) {
            if (matchesCorrelation(entry, correlationId) && map.has(entry.id)) {
                map.set(entry.id, entry);
            }
        }
        if (selfEntry && map.has(selfEntry.id)) {
            map.set(selfEntry.id, selfEntry);
        }
        return map;
    }, [activity?.entries, contentActivity?.entries, correlationId, selfEntry]);

    const chainRoots = useMemo(() => {
        if (!activity?.tree) {
            return [];
        }
        const root = buildChainNode(activity.tree, displayById);
        return root ? [root] : [];
    }, [activity?.tree, displayById]);

    const chainNodes = useMemo(() => flattenChainNodes(chainRoots), [chainRoots]);
    const chainNodeCount = chainNodes.length;
    const chainTotalPages = Math.max(1, Math.ceil(chainNodeCount / RELATED_PAGE_SIZE));
    const safeChainPage = Math.min(chainPage, chainTotalPages);
    const chainPageRoots = useMemo(() => {
        const start = (safeChainPage - 1) * RELATED_PAGE_SIZE;
        const nodes = new Map<string, ChainNode>(
            chainNodes
                .slice(start, start + RELATED_PAGE_SIZE)
                .map((node) => [node.entry.id, { ...node, children: [] }]),
        );
        const roots: ChainNode[] = [];
        for (const node of nodes.values()) {
            const parent = node.parent ? nodes.get(node.parent.id) : undefined;
            if (parent) {
                parent.children.push(node);
            } else {
                roots.push(node);
            }
        }
        return roots;
    }, [chainNodes, safeChainPage]);

    const chainTarget = selfId ?? resolvedExecutionId;
    const pagedChainTarget = useRef<string | null>(null);
    useEffect(() => {
        if (chainNodes.length === 0 || pagedChainTarget.current === chainTarget) {
            return;
        }
        pagedChainTarget.current = chainTarget;
        const index = chainNodes.findIndex((node) => node.entry.id === chainTarget);
        setChainPage(index < 0 ? 1 : Math.floor(index / RELATED_PAGE_SIZE) + 1);
    }, [chainNodes, chainTarget]);

    const also = useMemo(() => {
        const inChain = (entry: Pick<MagpieActivityEntry, 'id' | 'kind'>) =>
            entry.kind === MagpieActivityEntryKind.JobExecution && chainIds.has(entry.id);

        const entries = new Map<string, MagpieActivityEntry>();
        for (const entry of contentActivity?.entries ?? []) {
            if (matchesCorrelation(entry, correlationId) && !inChain(entry)) {
                entries.set(`${entry.kind}-${entry.id}`, entry);
            }
        }
        if (selfEntry && !inChain(selfEntry)) {
            entries.set(`${selfEntry.kind}-${selfEntry.id}`, selfEntry);
        }
        return [...entries.values()].sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime());
    }, [contentActivity?.entries, correlationId, chainIds, selfEntry]);

    const alsoTarget = selfId == null ? '' : `${selfKind}-${selfId}`;
    const pagedAlsoTarget = useRef<string | null>(null);
    useEffect(() => {
        if (isLoading || also.length === 0 || pagedAlsoTarget.current === alsoTarget) {
            return;
        }
        pagedAlsoTarget.current = alsoTarget;
        const index = also.findIndex((e) => e.id === selfId && e.kind === selfKind);
        setAlsoPage(index < 0 ? 1 : Math.floor(index / RELATED_PAGE_SIZE) + 1);
    }, [also, alsoTarget, isLoading, selfId, selfKind]);

    const totalRelatedCount = chainNodeCount + also.length;
    const alsoTotalPages = Math.max(1, Math.ceil(also.length / RELATED_PAGE_SIZE));
    const safeAlsoPage = Math.min(alsoPage, alsoTotalPages);
    const alsoStart = (safeAlsoPage - 1) * RELATED_PAGE_SIZE;
    const alsoPageEntries = also.slice(alsoStart, alsoStart + RELATED_PAGE_SIZE);

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
            ) : totalRelatedCount === 0 ? (
                <p className="related-events__empty">No activity is related to this one.</p>
            ) : (
                <div className="related-events__chain" data-testid="related-events__chain">
                    {activity?.capped && (
                        <p className="related-events__capped-note" data-testid="related-events-capped" role="status">
                            <InfoIcon weight="duotone" aria-hidden="true" />
                            This chain is truncated at {activity.totalCount} activities.
                        </p>
                    )}
                    {contentActivity?.capped && (
                        <p
                            className="related-events__capped-note"
                            data-testid="related-events-content-capped"
                            role="status"
                        >
                            <InfoIcon weight="duotone" aria-hidden="true" />
                            Only the latest {contentActivity.entries.length} of {contentActivity.totalCount} activities
                            for this item were searched.
                        </p>
                    )}

                    {chainRoots.length > 0 && (
                        <>
                            <ol className="related-events__chain-list" role="list">
                                {chainPageRoots.map((root) => (
                                    <ChainBranch
                                        key={root.entry.id}
                                        node={root}
                                        selfId={selfId}
                                        selfKind={selfKind}
                                        showParent
                                    />
                                ))}
                            </ol>
                            {chainTotalPages > 1 && (
                                <Pager
                                    aria-label="Job chain pagination"
                                    className="related-events__pager"
                                    page={safeChainPage}
                                    pageCount={chainTotalPages}
                                    totalItems={chainNodeCount}
                                    onFirst={() => setChainPage(1)}
                                    onPrevious={() => setChainPage((page) => Math.max(1, page - 1))}
                                    onNext={() => setChainPage((page) => Math.min(chainTotalPages, page + 1))}
                                    onLast={() => setChainPage(chainTotalPages)}
                                />
                            )}
                        </>
                    )}

                    {also.length > 0 && (
                        <div className="related-events__also" data-testid="related-events__also">
                            <h4 className="related-events__also-title">Also in this activity group</h4>
                            <ol className="related-events__also-list" role="list">
                                {alsoPageEntries.map((entry) => (
                                    <AlsoStep
                                        key={`${entry.kind}-${entry.id}`}
                                        entry={entry}
                                        selfId={selfId}
                                        selfKind={selfKind}
                                    />
                                ))}
                            </ol>
                            {also.length > RELATED_PAGE_SIZE && (
                                <Pager
                                    aria-label="Related activity pagination"
                                    className="related-events__pager"
                                    page={safeAlsoPage}
                                    pageCount={alsoTotalPages}
                                    totalItems={also.length}
                                    onFirst={() => setAlsoPage(1)}
                                    onPrevious={() => setAlsoPage(Math.max(1, safeAlsoPage - 1))}
                                    onNext={() => setAlsoPage(Math.min(alsoTotalPages, safeAlsoPage + 1))}
                                    onLast={() => setAlsoPage(alsoTotalPages)}
                                />
                            )}
                        </div>
                    )}

                    {totalRelatedCount === 1 && (
                        <p className="related-events__empty">No other activity is related to this one.</p>
                    )}
                </div>
            )}
        </div>
    );
}

RelatedEventsChain.displayName = 'RelatedEventsChain';

interface RelatedEventCardProps {
    entry: MagpieActivityEntry;
    isSelf: boolean;
}

function RelatedEventCard({ entry, isSelf }: RelatedEventCardProps) {
    const isJob = entry.kind === MagpieActivityEntryKind.JobExecution;
    const { variant, label: statusLabel } = getMagpieActivityStatusBadge(entry.status);
    const label = entryLabel(entry);

    return (
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
                {entry.entityId && entry.entityType === 'ContentHierarchyItem' && (
                    <LocalContentLink contentId={entry.entityId}>Open content preview</LocalContentLink>
                )}
                {entry.entityId && entry.entityType === 'ContentItem' && (
                    <LocalContentItemReference contentItemId={entry.entityId}>
                        Open content preview
                    </LocalContentItemReference>
                )}
            </div>
        </div>
    );
}

interface ChainBranchProps {
    node: ChainNode;
    showParent?: boolean;
    selfId: string | null;
    selfKind: string | null;
}

function ChainBranch({ node, selfId, selfKind, showParent }: ChainBranchProps) {
    const isSelf = selfId != null && node.entry.id === selfId && node.entry.kind === selfKind;

    return (
        <li className="related-events__step" data-kind="job" data-id={node.entry.id} data-self={isSelf || undefined}>
            {showParent && node.parent && (
                <span className="related-events__step-time">Parent: {entryLabel(node.parent)}</span>
            )}
            <RelatedEventCard entry={node.entry} isSelf={isSelf} />
            {node.children.length > 0 && (
                <>
                    <div className="related-events__connector" aria-hidden="true">
                        <ArrowDownIcon weight="bold" />
                    </div>
                    <ol className="related-events__children" role="list">
                        {node.children.map((child) => (
                            <ChainBranch key={child.entry.id} node={child} selfId={selfId} selfKind={selfKind} />
                        ))}
                    </ol>
                </>
            )}
        </li>
    );
}

interface AlsoStepProps {
    entry: MagpieActivityEntry;
    selfId: string | null;
    selfKind: string | null;
}

function AlsoStep({ entry, selfId, selfKind }: AlsoStepProps) {
    const isJob = entry.kind === MagpieActivityEntryKind.JobExecution;
    const isSelf = selfId != null && entry.id === selfId && entry.kind === selfKind;

    return (
        <li
            className="related-events__step"
            data-kind={isJob ? 'job' : 'audit'}
            data-id={entry.id}
            data-self={isSelf || undefined}
        >
            <RelatedEventCard entry={entry} isSelf={isSelf} />
        </li>
    );
}

export default RelatedEventsChain;
