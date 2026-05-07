import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface ContentSlotProps {
    children?: ReactNode;

    className?: string;
}

export function ContentListSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.list" className={className}>
            {children}
        </Slot>
    );
}

ContentListSlot.displayName = 'ContentListSlot';

export function ContentListColumnsSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.list.columns" className={className}>
            {children}
        </Slot>
    );
}

ContentListColumnsSlot.displayName = 'ContentListColumnsSlot';

export function ContentListActionsSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.list.actions" className={className}>
            {children}
        </Slot>
    );
}

ContentListActionsSlot.displayName = 'ContentListActionsSlot';

export function ContentDetailsHeaderSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.details.header" className={className}>
            {children}
        </Slot>
    );
}

ContentDetailsHeaderSlot.displayName = 'ContentDetailsHeaderSlot';

export function ContentDetailsActionsSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.details.actions" className={className}>
            {children}
        </Slot>
    );
}

ContentDetailsActionsSlot.displayName = 'ContentDetailsActionsSlot';

export function ContentDetailsMetadataSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.details.metadata" className={className}>
            {children}
        </Slot>
    );
}

ContentDetailsMetadataSlot.displayName = 'ContentDetailsMetadataSlot';

export function ContentDetailsTabsSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.details.tabs" className={className}>
            {children}
        </Slot>
    );
}

ContentDetailsTabsSlot.displayName = 'ContentDetailsTabsSlot';

export function ContentDetailsOverviewSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.details.overview" className={className}>
            {children}
        </Slot>
    );
}

ContentDetailsOverviewSlot.displayName = 'ContentDetailsOverviewSlot';

export function ContentDetailsFilesSlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.details.files" className={className}>
            {children}
        </Slot>
    );
}

ContentDetailsFilesSlot.displayName = 'ContentDetailsFilesSlot';

export function ContentDetailsHistorySlot({ children, className }: ContentSlotProps) {
    return (
        <Slot slot="content.details.history" className={className}>
            {children}
        </Slot>
    );
}

ContentDetailsHistorySlot.displayName = 'ContentDetailsHistorySlot';
