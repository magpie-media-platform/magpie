import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface FiltersSlotProps {
    children?: ReactNode;

    className?: string;
}

export function FiltersEditorSlot({ children, className }: FiltersSlotProps) {
    return (
        <Slot slot="filters.editor" className={className}>
            {children}
        </Slot>
    );
}

FiltersEditorSlot.displayName = 'FiltersEditorSlot';

export function FiltersListSlot({ children, className }: FiltersSlotProps) {
    return (
        <Slot slot="filters.list" className={className}>
            {children}
        </Slot>
    );
}

FiltersListSlot.displayName = 'FiltersListSlot';
