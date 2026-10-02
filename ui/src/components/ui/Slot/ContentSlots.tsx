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
