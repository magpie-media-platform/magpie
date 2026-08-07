import { useEffect, useId, useState, type ReactNode } from 'react';

import { FloppyDiskIcon, XIcon } from '@phosphor-icons/react';

import { Button } from '../Button';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '../Dialog';
import { Select, SelectContent, SelectItem, SelectTrigger } from '../Select';

import './BulkValuePicker.css';

const INHERIT_VALUE = '__inherit__';

export interface BulkValuePickerOption {
    id: string;

    label: string;
}

export interface BulkValuePickerProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    title: string;

    icon?: ReactNode;

    description: string;

    selectLabel: string;

    inheritLabel: string;

    options: BulkValuePickerOption[];

    emptyHint?: string;

    selectedCount: number;

    onApply: (value: string | null) => void;

    isApplying?: boolean;

    'data-testid'?: string;
}

export function BulkValuePicker({
    open,
    onOpenChange,
    title,
    icon,
    description,
    selectLabel,
    inheritLabel,
    options,
    emptyHint,
    selectedCount,
    onApply,
    isApplying = false,
    'data-testid': dataTestId,
}: BulkValuePickerProps) {
    const selectId = useId();
    const [selectedId, setSelectedId] = useState<string>('');

    useEffect(() => {
        if (open) {
            setSelectedId('');
        }
    }, [open]);

    const handleApply = () => {
        onApply(selectedId ? selectedId : null);
    };

    const triggerLabel = (() => {
        if (!selectedId) {
            return inheritLabel;
        }
        const option = options.find((o) => o.id === selectedId);
        return option ? option.label : inheritLabel;
    })();

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent data-testid={dataTestId} data-component="bulk-value-picker">
                <DialogHeader>
                    <DialogTitle>
                        {icon}
                        {title}
                    </DialogTitle>
                    <DialogDescription>
                        {selectedCount === 1
                            ? `${description} for 1 selected item.`
                            : `${description} for ${selectedCount} selected items.`}
                    </DialogDescription>
                </DialogHeader>

                <DialogBody className="bulk-value-picker__body">
                    <label htmlFor={selectId} className="bulk-value-picker__select-label">
                        {selectLabel}
                    </label>
                    <Select
                        value={selectedId || INHERIT_VALUE}
                        onValueChange={(value) => setSelectedId(value === INHERIT_VALUE ? '' : value)}
                    >
                        <SelectTrigger id={selectId} data-testid="bulk-value-picker__select">
                            {triggerLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={INHERIT_VALUE}>{inheritLabel}</SelectItem>
                            {options.map((option) => (
                                <SelectItem key={option.id} value={option.id}>
                                    {option.label}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                    {options.length === 0 && emptyHint && <p className="bulk-value-picker__empty">{emptyHint}</p>}
                </DialogBody>

                <DialogFooter>
                    <Button
                        type="button"
                        variant="secondary"
                        onClick={() => onOpenChange(false)}
                        disabled={isApplying}
                        data-action="cancel"
                    >
                        <XIcon weight="bold" aria-hidden="true" />
                        Cancel
                    </Button>
                    <Button
                        type="button"
                        variant="primary"
                        onClick={handleApply}
                        loading={isApplying}
                        disabled={isApplying}
                        data-action="apply"
                    >
                        <FloppyDiskIcon weight="bold" aria-hidden="true" />
                        {isApplying ? 'Applying...' : 'Apply'}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

BulkValuePicker.displayName = 'BulkValuePicker';

export default BulkValuePicker;
