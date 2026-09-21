import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface AdminSlotProps {
    children?: ReactNode;

    className?: string;
}

export function AdminExtensionsSlot({ children, className }: AdminSlotProps) {
    return (
        <Slot slot="admin.extensions" className={className}>
            {children}
        </Slot>
    );
}

AdminExtensionsSlot.displayName = 'AdminExtensionsSlot';

export function AdminBackupsSlot({ children, className }: AdminSlotProps) {
    return (
        <Slot slot="admin.backups" className={className}>
            {children}
        </Slot>
    );
}

AdminBackupsSlot.displayName = 'AdminBackupsSlot';
