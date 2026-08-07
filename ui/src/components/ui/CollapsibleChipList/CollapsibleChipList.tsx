import { useState, type ComponentPropsWithoutRef, type ReactNode } from 'react';

import './CollapsibleChipList.css';

export interface CollapsibleChipItem {
    key: string;

    node: ReactNode;
}

export interface CollapsibleChipListProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    items: CollapsibleChipItem[];

    initialVisible?: number;

    listClassName?: string;

    toggleTestId?: string;
}

const DEFAULT_INITIAL_VISIBLE = 10;

export function CollapsibleChipList({
    items,
    initialVisible = DEFAULT_INITIAL_VISIBLE,
    listClassName,
    toggleTestId,
    className,
    ...props
}: CollapsibleChipListProps) {
    const [expanded, setExpanded] = useState(false);

    if (items.length === 0) {
        return null;
    }

    const canCollapse = items.length > initialVisible;
    const visible = canCollapse && !expanded ? items.slice(0, initialVisible) : items;
    const hiddenCount = items.length - initialVisible;
    const classes = ['collapsible-chip-list', className].filter(Boolean).join(' ');
    const listClasses = ['collapsible-chip-list__list', listClassName].filter(Boolean).join(' ');

    return (
        <div className={classes} data-component="collapsible-chip-list" {...props}>
            <ul className={listClasses} role="list">
                {visible.map((item) => (
                    <li key={item.key}>{item.node}</li>
                ))}
            </ul>
            {canCollapse && (
                <button
                    type="button"
                    className="collapsible-chip-list__toggle"
                    onClick={() => setExpanded((value) => !value)}
                    aria-expanded={expanded}
                    data-action="toggle-collapsible"
                    data-testid={toggleTestId}
                >
                    {expanded ? 'Show fewer' : `Show all (${hiddenCount} more)`}
                </button>
            )}
        </div>
    );
}

CollapsibleChipList.displayName = 'CollapsibleChipList';

export default CollapsibleChipList;
