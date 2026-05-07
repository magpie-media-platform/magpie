import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface SystemSlotProps {
    children?: ReactNode;

    className?: string;
}

export function SystemStatusSlot({ children, className }: SystemSlotProps) {
    return (
        <Slot slot="system.status" className={className}>
            {children}
        </Slot>
    );
}

SystemStatusSlot.displayName = 'SystemStatusSlot';

export function SystemLogsSlot({ children, className }: SystemSlotProps) {
    return (
        <Slot slot="system.logs" className={className}>
            {children}
        </Slot>
    );
}

SystemLogsSlot.displayName = 'SystemLogsSlot';
