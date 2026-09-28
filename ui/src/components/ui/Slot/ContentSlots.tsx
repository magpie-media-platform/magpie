import type { ReactNode } from 'react';

import type { ContentPreviewClip } from '@/types/content';

import type { SlotContext } from './buildExtensionAttributes';
import { Slot } from './Slot';

function previewContext(preview: ContentPreviewClip | undefined): SlotContext {
    return {
        'preview-url': preview?.url,
        'preview-kind': preview?.kind,
        'preview-title': preview?.title,
        'preview-embed-kind': preview?.embedKind,
    };
}

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

export interface ContentGalleryPosterSlotProps {
    children?: ReactNode;

    className?: string;

    contentId: string;

    level: string;

    posterUrl?: string;

    declaresPreviews: boolean;

    selectionMode: boolean;

    preview?: ContentPreviewClip;
}

export function ContentGalleryPosterSlot({
    children,
    className,
    contentId,
    level,
    posterUrl,
    declaresPreviews,
    selectionMode,
    preview,
}: ContentGalleryPosterSlotProps) {
    return (
        <Slot
            slot="content.gallery.poster"
            className={className}
            entityId={contentId}
            context={{
                level,
                'poster-url': posterUrl,
                'declares-previews': String(declaresPreviews),
                'selection-mode': String(selectionMode),
                ...previewContext(preview),
            }}
        >
            {children}
        </Slot>
    );
}

ContentGalleryPosterSlot.displayName = 'ContentGalleryPosterSlot';

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
