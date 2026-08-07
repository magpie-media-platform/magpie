import { TagIcon } from '@phosphor-icons/react';

import { Badge } from '../Badge';

import './TagsCell.css';

export interface TagsCellProps {
    tags?: string[];

    showEmptyLabel?: boolean;
}

export function TagsCell({ tags, showEmptyLabel = false }: TagsCellProps) {
    if (!tags || tags.length === 0) {
        return (
            <span className="tags-cell__empty">
                <TagIcon weight="thin" aria-hidden="true" />
                {showEmptyLabel && <span className="tags-cell__empty-label">No tags</span>}
            </span>
        );
    }

    return (
        <div className="tags-cell">
            {tags.slice(0, 3).map((tag) => (
                <Badge key={tag} variant="default" size="sm">
                    {tag}
                </Badge>
            ))}
            {tags.length > 3 && (
                <span className="tags-cell__overflow" title={tags.slice(3).join(', ')}>
                    +{tags.length - 3}
                </span>
            )}
        </div>
    );
}
