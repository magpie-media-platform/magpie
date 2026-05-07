import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface DashboardSlotProps {
    children?: ReactNode;

    className?: string;
}

export function DashboardMainSlot({ children, className }: DashboardSlotProps) {
    return (
        <Slot slot="dashboard.main" className={className}>
            {children}
        </Slot>
    );
}

DashboardMainSlot.displayName = 'DashboardMainSlot';

export function DashboardWidgetsSlot({ children, className }: DashboardSlotProps) {
    return (
        <Slot slot="dashboard.widgets" className={className}>
            {children}
        </Slot>
    );
}

DashboardWidgetsSlot.displayName = 'DashboardWidgetsSlot';
