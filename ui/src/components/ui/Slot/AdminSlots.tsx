import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface AdminSlotProps {
    children?: ReactNode;

    className?: string;
}

export function AdminUsersSlot({ children, className }: AdminSlotProps) {
    return (
        <Slot slot="admin.users" className={className}>
            {children}
        </Slot>
    );
}

AdminUsersSlot.displayName = 'AdminUsersSlot';

export function AdminProvidersSlot({ children, className }: AdminSlotProps) {
    return (
        <Slot slot="admin.providers" className={className}>
            {children}
        </Slot>
    );
}

AdminProvidersSlot.displayName = 'AdminProvidersSlot';

export function AdminExtensionsSlot({ children, className }: AdminSlotProps) {
    return (
        <Slot slot="admin.extensions" className={className}>
            {children}
        </Slot>
    );
}

AdminExtensionsSlot.displayName = 'AdminExtensionsSlot';

export function AdminExtensionSettingsSlot({ children, className }: AdminSlotProps) {
    return (
        <Slot slot="admin.extensions.settings" className={className}>
            {children}
        </Slot>
    );
}

AdminExtensionSettingsSlot.displayName = 'AdminExtensionSettingsSlot';

export function AdminBackupsSlot({ children, className }: AdminSlotProps) {
    return (
        <Slot slot="admin.backups" className={className}>
            {children}
        </Slot>
    );
}

AdminBackupsSlot.displayName = 'AdminBackupsSlot';
