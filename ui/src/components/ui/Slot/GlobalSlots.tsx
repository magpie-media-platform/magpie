import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface GlobalSlotProps {
    children?: ReactNode;

    className?: string;
}

export function HeaderSlot({ children, className }: GlobalSlotProps) {
    return (
        <Slot slot="global.header" className={className}>
            {children}
        </Slot>
    );
}

HeaderSlot.displayName = 'HeaderSlot';

export function OverlaySlot({ children, className }: GlobalSlotProps) {
    return (
        <Slot slot="global.overlay" className={className}>
            {children}
        </Slot>
    );
}

OverlaySlot.displayName = 'OverlaySlot';

export function HeaderLeftSlot({ children, className }: GlobalSlotProps) {
    return (
        <Slot slot="header.left" className={className}>
            {children}
        </Slot>
    );
}

HeaderLeftSlot.displayName = 'HeaderLeftSlot';

export function HeaderRightSlot({ children, className }: GlobalSlotProps) {
    return (
        <Slot slot="header.right" className={className}>
            {children}
        </Slot>
    );
}

HeaderRightSlot.displayName = 'HeaderRightSlot';
