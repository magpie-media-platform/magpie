import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface CalendarSlotProps {
    children?: ReactNode;

    className?: string;
}

export function CalendarMainSlot({ children, className }: CalendarSlotProps) {
    return (
        <Slot slot="calendar.main" className={className}>
            {children}
        </Slot>
    );
}

CalendarMainSlot.displayName = 'CalendarMainSlot';
