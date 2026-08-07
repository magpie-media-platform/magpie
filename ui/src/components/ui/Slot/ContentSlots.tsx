import type { ReactNode } from 'react';

import { Slot } from './Slot';

export interface ContentSlotProps {
    children?: ReactNode;

    className?: string;

    entityId?: string;

    entityType?: string;
}

export function ContentListSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.list" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentListSlot.displayName = 'ContentListSlot';

export function ContentListColumnsSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.list.columns" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentListColumnsSlot.displayName = 'ContentListColumnsSlot';

export function ContentListActionsSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.list.actions" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentListActionsSlot.displayName = 'ContentListActionsSlot';

export function ContentDetailsHeaderSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.details.header" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentDetailsHeaderSlot.displayName = 'ContentDetailsHeaderSlot';

export function ContentDetailsActionsSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.details.actions" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentDetailsActionsSlot.displayName = 'ContentDetailsActionsSlot';

export function ContentDetailsMetadataSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.details.metadata" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentDetailsMetadataSlot.displayName = 'ContentDetailsMetadataSlot';

export function ContentDetailsTabsSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.details.tabs" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentDetailsTabsSlot.displayName = 'ContentDetailsTabsSlot';

export function ContentDetailsOverviewSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.details.overview" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentDetailsOverviewSlot.displayName = 'ContentDetailsOverviewSlot';

export function ContentDetailsFilesSlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.details.files" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentDetailsFilesSlot.displayName = 'ContentDetailsFilesSlot';

export function ContentDetailsHistorySlot({ children, className, entityId, entityType }: ContentSlotProps) {
    return (
        <Slot slot="content.details.history" className={className} entityId={entityId} entityType={entityType}>
            {children}
        </Slot>
    );
}

ContentDetailsHistorySlot.displayName = 'ContentDetailsHistorySlot';
