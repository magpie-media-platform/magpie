import { useState, useCallback, useEffect, useMemo } from 'react';
import {
    FloppyDiskIcon,
    MagnifyingGlassIcon,
    XIcon,
    TagIcon,
    PlusIcon,
    MinusIcon,
    CheckSquareIcon,
    SquareIcon,
    MinusSquareIcon,
} from '@phosphor-icons/react';

import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';

import { Button } from '../Button';
import { Input } from '../Input';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter, SheetClose } from '../Sheet';

import './BulkTagAssignment.css';

export interface BulkTagItem {
    id: string;

    name: string;

    color?: string;
}

export interface ContentWithTags {
    id: string;
    parentId?: string;
    title: string;
    tags?: string[];
}

export type TagAssignmentAction = 'add' | 'remove' | 'unchanged';

export interface BulkTagAssignmentResult {
    tagsToAdd: string[];

    tagsToRemove: string[];
}

export interface BulkTagAssignmentProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    availableTags: BulkTagItem[];

    selectedItems: ContentWithTags[];

    selectedCount?: number;

    onApply: (result: BulkTagAssignmentResult) => void;

    isApplying?: boolean;
}

type TagPresence = 'all' | 'some' | 'none';

interface TagState {
    presence: TagPresence;
    action: TagAssignmentAction;
}

export function BulkTagAssignment({
    open,
    onOpenChange,
    availableTags,
    selectedItems,
    selectedCount,
    onApply,
    isApplying = false,
}: BulkTagAssignmentProps) {
    const headerCount = selectedCount ?? selectedItems.length;
    const initialTagStates = useMemo(() => {
        const states: Record<string, TagState> = {};

        for (const tag of availableTags) {
            const itemsWithTag = selectedItems.filter((item) => item.tags?.includes(tag.id)).length;

            let presence: TagPresence;
            if (itemsWithTag === 0) {
                presence = 'none';
            } else if (itemsWithTag === selectedItems.length) {
                presence = 'all';
            } else {
                presence = 'some';
            }

            states[tag.id] = {
                presence,
                action: 'unchanged',
            };
        }

        return states;
    }, [availableTags, selectedItems]);

    const [actions, setActions] = useState<Record<string, TagAssignmentAction>>({});
    const [searchQuery, setSearchQuery] = useState('');

    useEffect(() => {
        if (open) {
            setActions({});
            setSearchQuery('');
        }
    }, [open]);

    const tagStates = useMemo(() => {
        const states: Record<string, TagState> = {};
        for (const [tagId, state] of Object.entries(initialTagStates)) {
            states[tagId] = { presence: state.presence, action: actions[tagId] ?? 'unchanged' };
        }
        return states;
    }, [initialTagStates, actions]);

    const filteredTags = useMemo(() => {
        if (!searchQuery) {
            return availableTags;
        }
        const query = searchQuery.toLowerCase();
        return availableTags.filter((tag) => tag.name.toLowerCase().includes(query));
    }, [availableTags, searchQuery]);

    const handleTagClick = useCallback(
        (tagId: string) => {
            const initial = initialTagStates[tagId];
            if (!initial) {
                return;
            }

            setActions((prev) => {
                const currentAction = prev[tagId] ?? 'unchanged';

                let newAction: TagAssignmentAction;

                if (currentAction === 'unchanged') {
                    if (initial.presence === 'all') {
                        newAction = 'remove';
                    } else {
                        newAction = 'add';
                    }
                } else if (currentAction === 'add') {
                    newAction = 'remove';
                } else {
                    newAction = 'unchanged';
                }

                return { ...prev, [tagId]: newAction };
            });
        },
        [initialTagStates],
    );

    const changes = useMemo(() => {
        const tagsToAdd: string[] = [];
        const tagsToRemove: string[] = [];

        for (const [tagId, state] of Object.entries(tagStates)) {
            if (state.action === 'add') {
                tagsToAdd.push(tagId);
            } else if (state.action === 'remove') {
                tagsToRemove.push(tagId);
            }
        }

        return { tagsToAdd, tagsToRemove };
    }, [tagStates]);

    const hasChanges = changes.tagsToAdd.length > 0 || changes.tagsToRemove.length > 0;

    const handleApply = useCallback(() => {
        onApply(changes);
    }, [onApply, changes]);

    const getCheckboxState = (
        tagId: string,
    ): { icon: 'checked' | 'unchecked' | 'indeterminate'; actionIndicator?: 'add' | 'remove' } => {
        const state = tagStates[tagId];
        if (!state) {
            return { icon: 'unchecked' };
        }

        if (state.action === 'add') {
            return { icon: 'checked', actionIndicator: 'add' };
        }
        if (state.action === 'remove') {
            return { icon: 'unchecked', actionIndicator: 'remove' };
        }

        if (state.presence === 'all') {
            return { icon: 'checked' };
        }
        if (state.presence === 'some') {
            return { icon: 'indeterminate' };
        }
        return { icon: 'unchecked' };
    };

    return (
        <Sheet open={open} onOpenChange={onOpenChange}>
            <SheetContent side="right" className="bulk-tag-assignment__sheet" data-component="bulk-tag-assignment">
                <SheetHeader>
                    <SheetTitle>
                        <TagIcon weight="duotone" aria-hidden="true" />
                        Assign Tags
                    </SheetTitle>
                    <SheetDescription>
                        {headerCount === 1 && selectedItems.length === 1 ? (
                            <>
                                Modify tags for &quot;
                                <ContentLineageTitle
                                    qualifiedName={{
                                        id: selectedItems[0].id,
                                        parentId: selectedItems[0].parentId,
                                        title: selectedItems[0].title,
                                    }}
                                />
                                &quot;
                            </>
                        ) : (
                            `Modify tags for ${headerCount} selected items`
                        )}
                    </SheetDescription>
                </SheetHeader>

                <div className="bulk-tag-assignment__body">
                    {availableTags.length > 0 && (
                        <div className="bulk-tag-assignment__search">
                            <Input
                                type="search"
                                placeholder="Search tags..."
                                size="sm"
                                value={searchQuery}
                                onChange={(e) => setSearchQuery(e.target.value)}
                                leadingIcon={<MagnifyingGlassIcon weight="regular" />}
                                trailingIcon={
                                    searchQuery ? (
                                        <button
                                            type="button"
                                            className="bulk-tag-assignment__clear-btn"
                                            onClick={() => setSearchQuery('')}
                                            aria-label="Clear search"
                                        >
                                            <XIcon weight="bold" />
                                        </button>
                                    ) : undefined
                                }
                            />
                        </div>
                    )}

                    <div className="bulk-tag-assignment__tags" role="group" aria-label="Available tags">
                        {availableTags.length === 0 ? (
                            <div className="bulk-tag-assignment__empty">
                                <TagIcon weight="thin" aria-hidden="true" />
                                <p>No tags available</p>
                                <p className="bulk-tag-assignment__empty-hint">
                                    Add tags from an item&apos;s detail page to use them here
                                </p>
                            </div>
                        ) : filteredTags.length === 0 ? (
                            <div className="bulk-tag-assignment__empty">
                                <MagnifyingGlassIcon weight="thin" aria-hidden="true" />
                                <p>No matching tags</p>
                                <p className="bulk-tag-assignment__empty-hint">Try a different search term</p>
                            </div>
                        ) : (
                            filteredTags.map((tag) => {
                                const checkboxState = getCheckboxState(tag.id);
                                const tagColor = tag.color || 'var(--accent-secondary)';

                                return (
                                    <button
                                        key={tag.id}
                                        type="button"
                                        className={`bulk-tag-assignment__tag ${
                                            checkboxState.actionIndicator
                                                ? `bulk-tag-assignment__tag--${checkboxState.actionIndicator}`
                                                : ''
                                        }`}
                                        onClick={() => handleTagClick(tag.id)}
                                        disabled={isApplying}
                                        data-tag-id={tag.id}
                                        aria-pressed={checkboxState.icon === 'checked'}
                                    >
                                        <span className="bulk-tag-assignment__checkbox">
                                            {checkboxState.icon === 'checked' && (
                                                <CheckSquareIcon weight="fill" aria-hidden="true" />
                                            )}
                                            {checkboxState.icon === 'unchecked' && (
                                                <SquareIcon weight="regular" aria-hidden="true" />
                                            )}
                                            {checkboxState.icon === 'indeterminate' && (
                                                <MinusSquareIcon weight="fill" aria-hidden="true" />
                                            )}
                                        </span>
                                        <span
                                            className="bulk-tag-assignment__tag-icon"
                                            style={{ '--tag-color': tagColor } as React.CSSProperties}
                                        >
                                            <TagIcon weight="duotone" aria-hidden="true" />
                                        </span>
                                        <span
                                            className="bulk-tag-assignment__tag-name"
                                            style={{ '--tag-color': tagColor } as React.CSSProperties}
                                        >
                                            {tag.name}
                                        </span>
                                        {checkboxState.actionIndicator && (
                                            <span
                                                className={`bulk-tag-assignment__action-badge bulk-tag-assignment__action-badge--${checkboxState.actionIndicator}`}
                                            >
                                                {checkboxState.actionIndicator === 'add' ? (
                                                    <>
                                                        <PlusIcon weight="bold" aria-hidden="true" />
                                                        <span>Add</span>
                                                    </>
                                                ) : (
                                                    <>
                                                        <MinusIcon weight="bold" aria-hidden="true" />
                                                        <span>Remove</span>
                                                    </>
                                                )}
                                            </span>
                                        )}
                                    </button>
                                );
                            })
                        )}
                    </div>

                    {hasChanges && (
                        <div className="bulk-tag-assignment__summary">
                            <span className="bulk-tag-assignment__summary-label">Changes:</span>
                            {changes.tagsToAdd.length > 0 && (
                                <span className="bulk-tag-assignment__summary-item bulk-tag-assignment__summary-item--add">
                                    <PlusIcon weight="bold" aria-hidden="true" />
                                    {changes.tagsToAdd.length} tag{changes.tagsToAdd.length !== 1 ? 's' : ''} to add
                                </span>
                            )}
                            {changes.tagsToRemove.length > 0 && (
                                <span className="bulk-tag-assignment__summary-item bulk-tag-assignment__summary-item--remove">
                                    <MinusIcon weight="bold" aria-hidden="true" />
                                    {changes.tagsToRemove.length} tag{changes.tagsToRemove.length !== 1 ? 's' : ''} to
                                    remove
                                </span>
                            )}
                        </div>
                    )}
                </div>

                <SheetFooter>
                    <SheetClose asChild>
                        <Button type="button" variant="ghost" disabled={isApplying} data-action="cancel">
                            <XIcon weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                    </SheetClose>
                    <Button
                        type="button"
                        variant="primary"
                        disabled={isApplying || !hasChanges}
                        onClick={handleApply}
                        data-action="apply"
                    >
                        <FloppyDiskIcon weight="bold" aria-hidden="true" />
                        {isApplying ? 'Applying...' : 'Apply'}
                    </Button>
                </SheetFooter>
            </SheetContent>
        </Sheet>
    );
}

BulkTagAssignment.displayName = 'BulkTagAssignment';

export default BulkTagAssignment;
