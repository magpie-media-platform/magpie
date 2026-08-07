import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { CheckIcon, FloppyDiskIcon, PencilSimpleIcon, WarningCircleIcon, XIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { TagInput } from '@/components/ui/TagInput';
import { getErrorMessage, getFieldError } from '@/lib/errors';

import './MimeTypesEditor.css';

const MIME_TOKEN = /^[a-z0-9!#$%&'+.^_`|~-]+$/i;

export function normalizeMimePatterns(values: readonly string[]): { patterns: string[]; error: string | null } {
    const patterns: string[] = [];
    const seen = new Set<string>();
    for (const value of values) {
        const pattern = value.trim().toLowerCase();
        const parts = pattern.split('/');
        if (parts.length !== 2 || !MIME_TOKEN.test(parts[0]) || (parts[1] !== '*' && !MIME_TOKEN.test(parts[1]))) {
            return { patterns: [], error: `${value} is not a valid MIME pattern.` };
        }
        if (!seen.has(pattern)) {
            seen.add(pattern);
            patterns.push(pattern);
        }
    }
    return { patterns, error: null };
}

function formatMimeTypes(patterns: readonly string[]): string {
    return patterns.length === 0 ? 'Any MIME type' : patterns.join(', ');
}

interface MimeTypesEditorProps {
    value: string[];
    canEdit: boolean;
    onSave: (patterns: string[]) => Promise<void>;
    label: string;
}

export function MimeTypesEditor({ value, canEdit, onSave, label }: MimeTypesEditorProps) {
    const errorId = useId();
    const [editing, setEditing] = useState(false);
    const [draft, setDraft] = useState(value);
    const [displayValue, setDisplayValue] = useState(value);
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [saved, setSaved] = useState(false);
    const editButtonRef = useRef<HTMLButtonElement>(null);
    const restoreFocus = useRef(false);
    const needsAttention = normalizeMimePatterns(displayValue).error !== null;

    useEffect(() => {
        setDisplayValue(value);
        setDraft(value);
    }, [value]);

    useEffect(() => {
        if (!editing && restoreFocus.current) {
            editButtonRef.current?.focus();
            restoreFocus.current = false;
        }
    }, [editing]);

    const cancel = useCallback(() => {
        setDraft(value);
        setError(null);
        restoreFocus.current = true;
        setEditing(false);
    }, [value]);

    const save = useCallback(async () => {
        const normalized = normalizeMimePatterns(draft);
        if (normalized.error) {
            setError(normalized.error);
            return;
        }

        setIsSaving(true);
        setError(null);
        setSaved(false);
        try {
            await onSave(normalized.patterns);
            setDraft(normalized.patterns);
            setDisplayValue(normalized.patterns);
            restoreFocus.current = true;
            setEditing(false);
            setSaved(true);
        } catch (saveError) {
            setError(getFieldError(saveError, 'mimeTypes') ?? getErrorMessage(saveError, 'Failed to save MIME types.'));
        } finally {
            setIsSaving(false);
        }
    }, [draft, onSave]);

    const handleKeyDown = useCallback(
        (event: KeyboardEvent<HTMLDivElement>) => {
            if (event.key === 'Escape') {
                event.preventDefault();
                cancel();
            }
            if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
                event.preventDefault();
                void save();
            }
        },
        [cancel, save],
    );

    if (!editing) {
        return (
            <div className="mime-types-editor" data-state={saved ? 'saved' : 'idle'}>
                <div
                    className="mime-types-editor__patterns"
                    role="group"
                    aria-label={`MIME types: ${formatMimeTypes(displayValue)}`}
                >
                    {displayValue.length === 0 ? (
                        <span className="mime-types-editor__any">Any MIME type</span>
                    ) : (
                        displayValue.map((pattern) => (
                            <Badge key={pattern} variant="default" size="sm">
                                {pattern}
                            </Badge>
                        ))
                    )}
                    {needsAttention && (
                        <Badge variant="warning" size="sm">
                            <WarningCircleIcon weight="fill" aria-hidden="true" />
                            Needs attention
                        </Badge>
                    )}
                </div>
                {canEdit && (
                    <button
                        ref={editButtonRef}
                        type="button"
                        className="mime-types-editor__edit"
                        onClick={() => {
                            setSaved(false);
                            setEditing(true);
                        }}
                        aria-label={`Edit MIME types for ${label}`}
                    >
                        <PencilSimpleIcon weight="bold" aria-hidden="true" />
                    </button>
                )}
                {saved && (
                    <span className="mime-types-editor__status" role="status">
                        <CheckIcon weight="bold" aria-hidden="true" /> Saved
                    </span>
                )}
            </div>
        );
    }

    return (
        <div className="mime-types-editor mime-types-editor--editing" onKeyDown={handleKeyDown}>
            <TagInput
                value={draft}
                onChange={(next) => {
                    setDraft(next);
                    setError(null);
                }}
                allowCreate
                placeholder="audio/*"
                label={`MIME types for ${label}`}
                disabled={isSaving}
                autoFocus
                aria-invalid={!!error}
                aria-describedby={error ? errorId : undefined}
            />
            <div className="mime-types-editor__actions">
                <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={cancel}
                    disabled={isSaving}
                    aria-label="Cancel MIME changes"
                >
                    <XIcon weight="bold" aria-hidden="true" />
                </Button>
                <Button
                    type="button"
                    variant="primary"
                    size="sm"
                    onClick={() => void save()}
                    loading={isSaving}
                    aria-label="Save MIME changes"
                >
                    <FloppyDiskIcon weight="bold" aria-hidden="true" />
                </Button>
            </div>
            {isSaving && (
                <span className="mime-types-editor__status" role="status">
                    Saving MIME types.
                </span>
            )}
            {error && (
                <span id={errorId} className="mime-types-editor__error" role="alert">
                    {error}
                </span>
            )}
        </div>
    );
}

MimeTypesEditor.displayName = 'MimeTypesEditor';
