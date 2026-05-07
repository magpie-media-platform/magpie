import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface DownloadSlotProps {
    children?: ReactNode;

    className?: string;
}

export function DownloadQueueSlot({ children, className }: DownloadSlotProps) {
    return (
        <Slot slot="downloads.queue" className={className}>
            {children}
        </Slot>
    );
}

DownloadQueueSlot.displayName = 'DownloadQueueSlot';

export function DownloadQueueActionsSlot({ children, className }: DownloadSlotProps) {
    return (
        <Slot slot="downloads.queue.actions" className={className}>
            {children}
        </Slot>
    );
}

DownloadQueueActionsSlot.displayName = 'DownloadQueueActionsSlot';
