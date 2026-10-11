import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { CaretRightIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import type { BadgeVariant } from '@/types/badge';
import type { LibraryImportCandidateStatus } from '@/hooks/useLibraryImportCandidates';
import type { LibraryImportCandidateStatusCounts } from '@/hooks/useLibraryImportCandidateGroups';

import './importDisplay.css';

export function fileName(path: string): string {
    const segments = path.split(/[/\\]/);
    return segments[segments.length - 1] || path;
}

export const STATUS_BADGE_VARIANT: Record<LibraryImportCandidateStatus, BadgeVariant> = {
    Failed: 'failed',
    MatchingPending: 'warning',
    Pending: 'pending',
    Queued: 'queued',
    Importing: 'importing',
    Invalidated: 'stale',
    Rejected: 'stale',
};

const STATUS_LABEL: Partial<Record<LibraryImportCandidateStatus, string>> = {
    MatchingPending: 'Matching pending',
    Rejected: 'Silenced',
};

/** Returns the display name of a candidate status. */
export function statusLabel(status: LibraryImportCandidateStatus): string {
    return STATUS_LABEL[status] ?? status;
}

/** Counts the candidates in each status. */
export function countStatuses(
    candidates: { status: LibraryImportCandidateStatus }[],
): LibraryImportCandidateStatusCounts {
    const counts: LibraryImportCandidateStatusCounts = {};
    for (const { status } of candidates) {
        counts[status] = (counts[status] ?? 0) + 1;
    }
    return counts;
}

/** Shows the status counts of a group. */
export function StatusRollup({ statusCounts }: { statusCounts: LibraryImportCandidateStatusCounts }) {
    const statuses = (Object.keys(STATUS_BADGE_VARIANT) as LibraryImportCandidateStatus[]).filter(
        (status) => status !== 'Pending' && (statusCounts[status] ?? 0) > 0,
    );
    if (statuses.length === 0) {
        return null;
    }
    return (
        <div className="import-display__status-rollup">
            {statuses.map((status) => (
                <Badge key={status} size="sm" variant={STATUS_BADGE_VARIANT[status]}>
                    {statusCounts[status]} {statusLabel(status)}
                </Badge>
            ))}
        </div>
    );
}

/** The name of an import group, with the name of its level before it. */
export function GroupName({ level, name }: { level?: string; name: string }) {
    return (
        <>
            {level && <span className="import-display__level">{level} </span>}
            {name}
        </>
    );
}

/** The name of an import group, as the link that opens the group. */
export function GroupLink({ to, children }: { to: string; children: ReactNode }) {
    return (
        <Link to={to} className="import-display__group-link" data-action="open-group">
            <span className="import-display__group-link-label">{children}</span>
            <CaretRightIcon weight="bold" aria-hidden="true" />
        </Link>
    );
}
