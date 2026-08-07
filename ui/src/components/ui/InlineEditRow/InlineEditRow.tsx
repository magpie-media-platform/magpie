import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';

import { PencilSimpleIcon } from '@phosphor-icons/react';

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
    children: (props: InlineEditRenderProps) => ReactNode;
}

export function InlineEditRow({
    readValue,
    canEdit,
    editAriaLabel,
    triggerTestId,
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

    let value: ReactNode;
    if (!canEdit) {
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

    return <DetailInfoRow label={label} value={value} {...rowProps} />;
}

InlineEditRow.displayName = 'InlineEditRow';

export default InlineEditRow;
