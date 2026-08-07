import { useCallback, useState, type ReactNode, type SubmitEvent } from 'react';
import { Link } from 'react-router-dom';
import { PlusIcon, XIcon } from '@phosphor-icons/react';

import { useToast } from '@/components/ui/Toast';
import { DetailInfoRow } from '@/components/ui/DetailInfoGrid';

import './TagsChipStrip.css';

export interface TagsPatch {
    add?: string[];
    remove?: string[];
}

export interface TagsPatchCallbacks {
    onSuccess: () => void;
    onError: () => void;
}

export interface TagsChipStripProps {
    entityId: string | undefined;
    tagNames: string[];
    isLoading: boolean;
    canEdit: boolean;
    isPending: boolean;
    tagHref: (tagName: string) => string;
    updateTags: (patch: TagsPatch, callbacks: TagsPatchCallbacks) => void;
    rowLabel?: ReactNode;
    rowTestId?: string;
}

export function TagsChipStrip({
    entityId,
    tagNames,
    isLoading,
    canEdit,
    isPending,
    tagHref,
    updateTags,
    rowLabel,
    rowTestId,
}: TagsChipStripProps) {
    const { toast } = useToast();
    const [isAdding, setIsAdding] = useState(false);
    const [pendingInput, setPendingInput] = useState('');

    const handleRemove = useCallback(
        (tagName: string) => {
            if (!entityId || isPending) {
                return;
            }
            updateTags(
                { remove: [tagName] },
                {
                    onSuccess: () => {
                        toast({
                            title: 'Tag removed',
                            description: `Tag "${tagName}" removed.`,
                            variant: 'success',
                        });
                    },
                    onError: () => {
                        toast({
                            title: 'Update failed',
                            description: `Failed to remove tag "${tagName}".`,
                            variant: 'error',
                        });
                    },
                },
            );
        },
        [entityId, isPending, toast, updateTags],
    );

    const handleAddSubmit = useCallback(
        (event: SubmitEvent<HTMLFormElement>) => {
            event.preventDefault();
            const value = pendingInput.trim();
            if (!entityId || !value || isPending) {
                return;
            }
            if (tagNames.includes(value)) {
                setIsAdding(false);
                setPendingInput('');
                return;
            }
            updateTags(
                { add: [value] },
                {
                    onSuccess: () => {
                        toast({
                            title: 'Tag added',
                            description: `Tag "${value}" added.`,
                            variant: 'success',
                        });
                        setPendingInput('');
                        setIsAdding(false);
                    },
                    onError: () => {
                        toast({
                            title: 'Update failed',
                            description: `Failed to add tag "${value}".`,
                            variant: 'error',
                        });
                    },
                },
            );
        },
        [entityId, isPending, pendingInput, tagNames, toast, updateTags],
    );

    if (!entityId || (!canEdit && tagNames.length === 0)) {
        return null;
    }

    const strip = (
        <div className="tags-chip-strip" data-component="tags-chip-strip" data-testid="tags-chip-strip">
            <ul className="tags-chip-strip__list" role="list">
                {tagNames.map((name) => (
                    <li key={name}>
                        <span className="tags-chip-strip__chip" data-tag={name}>
                            <Link
                                to={tagHref(name)}
                                className="tags-chip-strip__chip-link"
                                data-testid={`tags-chip-strip-link-${name}`}
                            >
                                {name}
                            </Link>
                            {canEdit && (
                                <button
                                    type="button"
                                    className="tags-chip-strip__chip-remove"
                                    onClick={() => handleRemove(name)}
                                    aria-label={`Remove tag ${name}`}
                                    disabled={isPending}
                                    data-testid={`tags-chip-strip-remove-${name}`}
                                >
                                    <XIcon weight="bold" aria-hidden="true" />
                                </button>
                            )}
                        </span>
                    </li>
                ))}
                {canEdit && !isLoading && (
                    <li>
                        {isAdding ? (
                            <form className="tags-chip-strip__form" onSubmit={handleAddSubmit}>
                                <input
                                    type="text"
                                    autoFocus
                                    placeholder="New tag"
                                    className="tags-chip-strip__input"
                                    value={pendingInput}
                                    onChange={(event) => setPendingInput(event.target.value)}
                                    onBlur={() => {
                                        if (!pendingInput.trim()) {
                                            setIsAdding(false);
                                        }
                                    }}
                                    onKeyDown={(event) => {
                                        if (event.key === 'Escape') {
                                            event.preventDefault();
                                            setIsAdding(false);
                                            setPendingInput('');
                                        }
                                    }}
                                    data-testid="tags-chip-strip-input"
                                />
                            </form>
                        ) : (
                            <button
                                type="button"
                                className="tags-chip-strip__add"
                                onClick={() => setIsAdding(true)}
                                aria-label="Add tag"
                                data-testid="tags-chip-strip-add"
                            >
                                <PlusIcon weight="bold" aria-hidden="true" />
                            </button>
                        )}
                    </li>
                )}
            </ul>
        </div>
    );

    return rowLabel === undefined ? strip : <DetailInfoRow label={rowLabel} value={strip} data-testid={rowTestId} />;
}

TagsChipStrip.displayName = 'TagsChipStrip';

export default TagsChipStrip;
