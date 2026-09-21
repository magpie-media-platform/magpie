import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface SubjectSlotProps {
    children?: ReactNode;

    className?: string;

    entityId?: string;

    entityType?: string;
}

export function SubjectListSlot({ children, className, entityId, entityType }: SubjectSlotProps) {
    return (
        <Slot slot="subject.list" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

SubjectListSlot.displayName = 'SubjectListSlot';

export function SubjectDetailsHeaderSlot({ children, className, entityId, entityType }: SubjectSlotProps) {
    return (
        <Slot slot="subject.details.header" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

SubjectDetailsHeaderSlot.displayName = 'SubjectDetailsHeaderSlot';
