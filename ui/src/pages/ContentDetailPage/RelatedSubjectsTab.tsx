import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { ImageBrokenIcon } from '@phosphor-icons/react';

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
                <p>Loading related subjects...</p>
            </div>
        );
    }

    if (grouped.length === 0) {
        return (
            <div className="related-subjects-tab" data-component="related-subjects-tab">
                <p className="related-subjects-tab__empty" data-empty="true">
                    No related information.
                </p>
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
    const avatar = <Avatar imageUrl={row.imageUrl} name={row.name} />;
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

function Avatar({ imageUrl, name }: { imageUrl?: string | null; name: string }) {
    if (!imageUrl) {
        return (
            <div
                className="related-subjects-tab__avatar related-subjects-tab__avatar--placeholder"
                data-testid="related-subject-avatar-placeholder"
            >
                <ImageBrokenIcon weight="thin" aria-hidden="true" />
            </div>
        );
    }

    return <img src={imageUrl} alt={name} className="related-subjects-tab__avatar" loading="lazy" />;
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
