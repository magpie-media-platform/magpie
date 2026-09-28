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

export interface LibraryGalleryTileProps {
    item: ContentListItem;
    selectionMode: boolean;
    selected?: boolean;
    onToggle?: () => void;
    onLongPress?: () => void;
}

export function LibraryGalleryTile({
    item,
    selectionMode,
    selected = false,
    onToggle,
    onLongPress,
}: LibraryGalleryTileProps) {
    const longPress = useLongPress({ onLongPress: onLongPress ?? (() => {}) });
    const openPreview = useOpenLocalContentPreview();
    const typeIconName = useTypeIconName()(item.contentType);
    const { contentTypes } = useLibraryTypes();
    const levelDef = contentTypes
        .find((type) => type.slug === item.contentType)
        ?.levels.find((level) => level.slug === item.level);

    const text = (
        <>
            <span className="library-gallery-tile__title-row">
                <ContentMonitoringIndicator
                    monitoring={item.monitoring}
                    effectiveMonitoring={item.effectiveMonitoring}
                    className="library-gallery-tile__monitoring"
                    activeClassName="library-gallery-tile__monitoring--active"
                />
                <span className="library-gallery-tile__title" title={item.title}>
                    {item.title}
                    {item.year !== undefined && <span className="library-gallery-tile__year"> ({item.year})</span>}
                </span>
            </span>
            <ContentFulfillmentBadge
                fulfillment={item.fulfillment}
                importPending={item.importPending}
                className="library-gallery-tile__fulfillment"
            />
        </>
    );

    const inner = (
        <>
            <div className="library-gallery-tile__poster">
                {selectionMode ? (
                    // The whole poster is a pointer target. The checkbox is the control for keyboards and screen readers.
                    <button
                        type="button"
                        className="library-gallery-tile__poster-target"
                        aria-hidden="true"
                        tabIndex={-1}
                        onClick={onToggle}
                    />
                ) : (
                    <Link
                        to={`/library/${item.id}`}
                        className="library-gallery-tile__poster-target"
                        aria-label={`Open ${item.title}`}
                        {...longPress.handlers}
                        onClick={(event) => longPress.consumeClick(event)}
                    />
                )}
                <ContentGalleryPosterSlot
                    contentId={item.id}
                    level={item.level}
                    posterUrl={item.posterUrl}
                    declaresPreviews={!!getStandardMetadataField(levelDef, 'Previews')}
                    selectionMode={selectionMode}
                    preview={item.previews?.[0]}
                >
                    <ContentPoster
                        posterUrl={item.posterUrl}
                        title={item.title}
                        alt=""
                        className="library-gallery-tile__img"
                        placeholderClassName="library-gallery-tile__poster--placeholder"
                    />
                </ContentGalleryPosterSlot>
                {selectionMode && (
                    <SelectionCircleToggle
                        selected={selected}
                        onToggle={() => onToggle?.()}
                        aria-label={`Select ${item.title}`}
                        data-action="select-row"
                    />
                )}
                <CdnPhosphorIcon
                    name={typeIconName}
                    weight="fill"
                    size={28}
                    className="library-gallery-tile__type"
                    aria-hidden="true"
                />
            </div>
            {selectionMode ? (
                <button
                    type="button"
                    className="library-gallery-tile__text"
                    aria-label={`Preview ${item.title}`}
                    onClick={(event) => openPreview(item.id, event.currentTarget)}
                >
                    {text}
                </button>
            ) : (
                <Link to={`/library/${item.id}`} className="library-gallery-tile__text">
                    {text}
                </Link>
            )}
        </>
    );

    return (
        <div className="library-gallery-tile" data-content-id={item.id} data-selected={selected ? 'true' : 'false'}>
            {inner}
        </div>
    );
}

LibraryGalleryTile.displayName = 'LibraryGalleryTile';

export default LibraryGalleryTile;
