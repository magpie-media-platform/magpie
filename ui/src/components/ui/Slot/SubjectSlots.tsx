import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface SubjectSlotProps {
    children?: ReactNode;

    className?: string;
}

export function SubjectListSlot({ children, className }: SubjectSlotProps) {
    return (
        <Slot slot="subject.list" className={className}>
            {children}
        </Slot>
    );
}

SubjectListSlot.displayName = 'SubjectListSlot';

export function SubjectDetailsHeaderSlot({ children, className }: SubjectSlotProps) {
    return (
        <Slot slot="subject.details.header" className={className}>
            {children}
        </Slot>
    );
}

SubjectDetailsHeaderSlot.displayName = 'SubjectDetailsHeaderSlot';

export function SubjectDetailsTabsSlot({ children, className }: SubjectSlotProps) {
    return (
        <Slot slot="subject.details.tabs" className={className}>
            {children}
        </Slot>
    );
}

SubjectDetailsTabsSlot.displayName = 'SubjectDetailsTabsSlot';
