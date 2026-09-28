import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';

import { ArrowClockwiseIcon, PencilSimpleIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { DetailInfoRow, type DetailInfoRowProps } from '@/components/ui/DetailInfoGrid';

import './InlineEditRow.css';

export interface InlineEditRenderProps {
    close: () => void;
}

export interface InlineEditRowProps extends Omit<DetailInfoRowProps, 'value' | 'control'> {
    readValue: ReactNode;
    canEdit: boolean;
    editAriaLabel: string;
    triggerTestId?: string;
    /** When set, the read value reports this failed lookup instead of an editable value. */
    error?: Error | null;
    onRetry?: () => void;
    retrying?: boolean;
    retryAriaLabel?: string;
    children: (props: InlineEditRenderProps) => ReactNode;
}

export function InlineEditRow({
    readValue,
    canEdit,
    editAriaLabel,
    triggerTestId,
    error = null,
    onRetry,
    retrying = false,
    retryAriaLabel,
    children,
    label,
    ...rowProps
}: InlineEditRowProps) {
    const [isEditing, setIsEditing] = useState(false);
    const triggerRef = useRef<HTMLButtonElement>(null);
    const restoreFocusRef = useRef(false);

    const close = useCallback(() => {
        restoreFocusRef.current = true;
        setIsEditing(false);
    }, []);

    useEffect(() => {
        if (isEditing || !restoreFocusRef.current) {
            return;
        }
        restoreFocusRef.current = false;
        triggerRef.current?.focus();
    }, [isEditing]);

    const rowRef = useRef<HTMLDivElement>(null);
    const retryFocusRef = useRef(false);

    useEffect(() => {
        if (error || !retryFocusRef.current) {
            return;
        }
        retryFocusRef.current = false;
        if (document.activeElement === document.body || document.activeElement === null) {
            (triggerRef.current ?? rowRef.current)?.focus();
        }
    }, [error]);

    let value: ReactNode;
    if (error) {
        value = (
            <div className="inline-edit-row__error" data-component="inline-edit-error">
                <span className="inline-edit-row__error-text" role="alert">
                    {readValue}
                </span>
                {onRetry && (
                    <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                            if (retrying) {
                                return;
                            }
                            retryFocusRef.current = true;
                            onRetry();
                        }}
                        aria-disabled={retrying}
                        aria-busy={retrying}
                        aria-label={retryAriaLabel}
                        data-action="retry"
                    >
                        <ArrowClockwiseIcon
                            weight="bold"
                            aria-hidden="true"
                            className={retrying ? 'animate-spin' : undefined}
                        />
                        Retry
                    </Button>
                )}
            </div>
        );
    } else if (!canEdit) {
        value = readValue;
    } else {
        value = (
            <div
                className="inline-edit-row__field"
                data-component="inline-edit-field"
                data-state={isEditing ? 'editing' : 'idle'}
            >
                {isEditing ? (
                    children({ close })
                ) : (
                    <button
                        type="button"
                        ref={triggerRef}
                        className="inline-edit-row__trigger"
                        onClick={() => setIsEditing(true)}
                        aria-label={editAriaLabel}
                        data-testid={triggerTestId}
                    >
                        <span className="inline-edit-row__read">{readValue}</span>
                        <PencilSimpleIcon weight="bold" aria-hidden="true" className="inline-edit-row__pencil" />
                    </button>
                )}
            </div>
        );
    }

    return <DetailInfoRow ref={rowRef} label={label} value={value} tabIndex={canEdit ? undefined : -1} {...rowProps} />;
}

InlineEditRow.displayName = 'InlineEditRow';

export default InlineEditRow;
