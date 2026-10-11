import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { ImageBrokenIcon, UsersThreeIcon } from '@phosphor-icons/react';

import { Avatar } from '@/components/ui/Avatar';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { useContentRelationships } from '@/hooks/useContent';
import type { ContentRelationship } from '@/types';

import './RelatedSubjectsTab.css';

interface RelatedSubjectsTabProps {
    contentId: string | undefined;
}

export function RelatedSubjectsTab({ contentId }: RelatedSubjectsTabProps) {
    const { data, isLoading } = useContentRelationships(contentId);

    const grouped = useMemo(() => {
        const map = new Map<string, ContentRelationship[]>();
        for (const row of data ?? []) {
            const kind = row.kind || 'other';
            if (!map.has(kind)) {
                map.set(kind, []);
            }
            map.get(kind)!.push(row);
        }
        for (const rows of map.values()) {
            rows.sort((a, b) => a.ordinal - b.ordinal);
        }
        return Array.from(map.entries())
            .map(([kind, rows]) => ({ kind, rows }))
            .sort((a, b) => a.rows[0].ordinal - b.rows[0].ordinal || a.kind.localeCompare(b.kind));
    }, [data]);

    if (isLoading) {
        return (
            <div className="related-subjects-tab" data-component="related-subjects-tab" data-loading="true">
                <Skeleton height={96} />
            </div>
        );
    }

    if (grouped.length === 0) {
        return (
            <div className="related-subjects-tab" data-component="related-subjects-tab">
                <EmptyState
                    icon={<UsersThreeIcon weight="duotone" />}
                    title="No related information"
                    description="This item has no related subjects."
                />
            </div>
        );
    }

    return (
        <div className="related-subjects-tab" data-component="related-subjects-tab">
            {grouped.map(({ kind, rows }) => (
                <section key={kind} className="related-subjects-tab__group" data-kind={kind}>
                    <h3 className="related-subjects-tab__group-title">{formatKindHeading(kind)}</h3>
                    <ul className="related-subjects-tab__grid" role="list">
                        {rows.map((row) => (
                            <li key={row.id} className="related-subjects-tab__item" data-relationship-id={row.id}>
                                <RelatedSubjectEntry row={row} />
                            </li>
                        ))}
                    </ul>
                </section>
            ))}
        </div>
    );
}

function RelatedSubjectEntry({ row }: { row: ContentRelationship }) {
    const avatar = (
        <Avatar
            key={row.imageUrl}
            src={row.imageUrl ?? undefined}
            alt={row.name}
            loading="lazy"
            className="related-subjects-tab__avatar"
            fallback={
                <ImageBrokenIcon weight="thin" aria-hidden="true" data-testid="related-subject-avatar-placeholder" />
            }
        />
    );
    const roleLabel = row.qualifier ? row.qualifier : null;

    const body = (
        <>
            {avatar}
            <span className="related-subjects-tab__name">{row.name}</span>
            {roleLabel && <span className="related-subjects-tab__qualifier">{roleLabel}</span>}
        </>
    );

    if (row.subjectId) {
        return (
            <Link
                to={`/subjects/${row.subjectId}`}
                className="related-subjects-tab__card related-subjects-tab__card--link"
                data-testid={`related-subject-link-${row.id}`}
            >
                {body}
            </Link>
        );
    }

    return (
        <div
            className="related-subjects-tab__card related-subjects-tab__card--dangling"
            data-testid={`related-subject-name-${row.id}`}
        >
            {body}
        </div>
    );
}

RelatedSubjectsTab.displayName = 'RelatedSubjectsTab';

function formatKindHeading(kind: string): string {
    if (!kind) {
        return 'Other';
    }
    return kind
        .split(/[-_\s]+/)
        .filter(Boolean)
        .map((word) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
        .join(' ');
}

export default RelatedSubjectsTab;
