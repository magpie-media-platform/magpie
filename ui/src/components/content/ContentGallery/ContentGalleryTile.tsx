import './ContentGallery.css';
import { useState, type MouseEvent, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useOpenLocalContentPreview } from '@/components/content/LocalContentPreview';

import {
    ContentFulfillmentBadge,
    ContentMonitoringIndicator,
    ContentPoster,
} from '@/components/content/ContentCardPrimitives';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { SelectionCircleToggle } from '@/components/ui/SelectionCircleToggle';
import { ContentGalleryPosterSlot } from '@/components/ui/Slot';
import { useLongPress } from '@/hooks/ui/useLongPress';
import { getStandardMetadataField, useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import type { ContentListItem } from '@/types';
import { useSlotExtensions } from '@/extensions/useExtensions';
import { useLocalContentPreview } from '@/hooks/useLocalContentPreview';
import { usePreviewByExternalId, type PreviewByExternalIdRequest } from '@/hooks/usePreviewByExternalId';

export type ContentGalleryItem = Pick<ContentListItem, 'id' | 'title'> & Partial<ContentListItem>;

export interface ContentGalleryTileProps {
    item: ContentGalleryItem;
    selectionMode?: boolean;
    selected?: boolean;
    selectionDisabled?: boolean;
    onToggle?: () => void;
    onLongPress?: () => void;
    to?: string | null;
    onOpen?: (invokingElement?: HTMLElement) => void;
    previewOnOpen?: boolean;
    detail?: ReactNode;
    actions?: ReactNode;
    posterActions?: ReactNode;
    muted?: boolean;
    localContentId?: string | null;
    previewTarget?: PreviewByExternalIdRequest;
}

function isPlainLeftClick(event: MouseEvent<HTMLAnchorElement>): boolean {
    return event.button === 0 && !event.metaKey && !event.altKey && !event.ctrlKey && !event.shiftKey;
}

export function ContentGalleryTile({
    item,
    selectionMode = false,
    selected = false,
    selectionDisabled = false,
    onToggle,
    onLongPress,
    to = `/library/${item.id}`,
    onOpen,
    previewOnOpen = false,
    detail,
    actions,
    posterActions,
    muted = false,
    localContentId = item.id,
    previewTarget,
}: ContentGalleryTileProps) {
    const longPress = useLongPress({ onLongPress: onLongPress ?? (() => {}), enabled: !selectionMode });
    const openPreview = useOpenLocalContentPreview();
    const typeIconName = useTypeIconName()(item.contentType ?? '');
    const { contentTypes } = useLibraryTypes();
    const levelDef = contentTypes
        .find((type) => type.slug === item.contentType)
        ?.levels.find((level) => level.slug === item.level);
    const declaresPreviews = !!getStandardMetadataField(levelDef, 'Previews');
    const { extensions } = useSlotExtensions('content.gallery.poster');
    const [loadPreview, setLoadPreview] = useState(false);
    const localPreview = useLocalContentPreview(loadPreview && localContentId ? localContentId : undefined);
    const externalPreview = usePreviewByExternalId();
    const preview = localPreview.data ?? externalPreview.data;
    const isInteractive = Boolean(selectionMode || onOpen || to);
    const requestPreview = () => {
        if (selectionMode || !extensions.length || item.previews?.length || (!declaresPreviews && item.level)) return;
        setLoadPreview(true);
        if (!localContentId && previewTarget && (externalPreview.isIdle || externalPreview.isError)) {
            externalPreview.mutate(previewTarget);
        }
    };

    const text = (
        <>
            <span className="content-gallery-tile__title-row">
                {item.effectiveMonitoring && (
                    <ContentMonitoringIndicator
                        effectiveMonitoring={item.effectiveMonitoring}
                        className="content-gallery-tile__monitoring"
                        activeClassName="content-gallery-tile__monitoring--active"
                    />
                )}
                <span className="content-gallery-tile__title" title={item.title}>
                    {item.title}
                    {item.year !== undefined && <span className="content-gallery-tile__year"> ({item.year})</span>}
                </span>
            </span>
            {detail ??
                (item.fulfillment && (
                    <ContentFulfillmentBadge
                        fulfillment={item.fulfillment}
                        importPending={item.importPending}
                        publishDate={item.publishDate}
                        publishDatePrecision={item.publishDatePrecision}
                        className="content-gallery-tile__fulfillment"
                    />
                ))}
        </>
    );

    const inner = (
        <>
            <div className="content-gallery-tile__poster" onPointerEnter={requestPreview} onFocus={requestPreview}>
                {selectionMode ? (
                    <button
                        type="button"
                        className="content-gallery-tile__poster-target"
                        aria-hidden="true"
                        tabIndex={-1}
                        {...longPress.handlers}
                        onClick={(event) => {
                            if (!longPress.consumeClick(event)) onToggle?.();
                        }}
                        disabled={selectionDisabled}
                    />
                ) : onOpen ? (
                    <button
                        type="button"
                        className="content-gallery-tile__poster-target"
                        aria-label={`Preview ${item.title}`}
                        {...longPress.handlers}
                        onClick={(event) => {
                            if (!longPress.consumeClick(event)) onOpen(event.currentTarget);
                        }}
                    />
                ) : to ? (
                    <Link
                        to={to}
                        className="content-gallery-tile__poster-target"
                        aria-label={`Open ${item.title}`}
                        {...longPress.handlers}
                        onClick={(event) => {
                            if (longPress.consumeClick(event)) {
                                return;
                            }
                            if (previewOnOpen && localContentId && !event.defaultPrevented && isPlainLeftClick(event)) {
                                event.preventDefault();
                                event.stopPropagation();
                                openPreview(localContentId, event.currentTarget);
                            }
                        }}
                    />
                ) : null}
                <ContentGalleryPosterSlot
                    contentId={localContentId ?? undefined}
                    level={item.level ?? preview?.levelSlug ?? ''}
                    posterUrl={item.posterUrl}
                    declaresPreviews={declaresPreviews || !!preview?.previews.length}
                    selectionMode={selectionMode}
                    preview={item.previews?.[0] ?? preview?.previews[0]}
                >
                    <ContentPoster
                        posterUrl={item.posterUrl}
                        title={item.title}
                        alt=""
                        className="content-gallery-tile__img"
                        placeholderClassName="content-gallery-tile__poster--placeholder"
                    />
                </ContentGalleryPosterSlot>
                {selectionMode && (
                    <SelectionCircleToggle
                        selected={selected}
                        disabled={selectionDisabled}
                        onToggle={() => onToggle?.()}
                        aria-label={`Select ${item.title}`}
                        data-action="select-row"
                    />
                )}
                {item.contentType && (
                    <CdnPhosphorIcon
                        name={typeIconName}
                        weight="fill"
                        size={28}
                        className="content-gallery-tile__type"
                        aria-hidden="true"
                    />
                )}
                {posterActions && <div className="content-gallery-tile__poster-actions">{posterActions}</div>}
            </div>
            {onOpen || (selectionMode && localContentId) ? (
                <button
                    type="button"
                    className="content-gallery-tile__text"
                    aria-label={selectionMode ? `Preview ${item.title}` : undefined}
                    onClick={(event) =>
                        onOpen
                            ? onOpen(event.currentTarget)
                            : localContentId && openPreview(localContentId, event.currentTarget)
                    }
                >
                    {text}
                </button>
            ) : to ? (
                <Link
                    to={to}
                    className="content-gallery-tile__text"
                    onClick={
                        previewOnOpen && localContentId
                            ? (event) => {
                                  if (!event.defaultPrevented && isPlainLeftClick(event)) {
                                      event.preventDefault();
                                      event.stopPropagation();
                                      openPreview(localContentId, event.currentTarget);
                                  }
                              }
                            : undefined
                    }
                >
                    {text}
                </Link>
            ) : (
                <div className="content-gallery-tile__text">{text}</div>
            )}
            {actions && <div className="content-gallery-tile__actions">{actions}</div>}
        </>
    );

    return (
        <div
            className="content-gallery-tile"
            data-content-id={item.id}
            data-selected={selected ? 'true' : 'false'}
            data-muted={muted || undefined}
            data-interactive={isInteractive ? 'true' : undefined}
        >
            {inner}
        </div>
    );
}

ContentGalleryTile.displayName = 'ContentGalleryTile';

export default ContentGalleryTile;
