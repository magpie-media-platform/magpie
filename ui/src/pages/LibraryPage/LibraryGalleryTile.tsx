import { type KeyboardEvent } from 'react';
import { Link } from 'react-router-dom';

import { ContentMonitoringIndicator, ContentPoster } from '@/components/content/ContentCardPrimitives';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { Checkbox } from '@/components/ui/Checkbox';
import { ContentGalleryPosterSlot } from '@/components/ui/Slot';
import { useLongPress } from '@/hooks/ui/useLongPress';
import { getStandardMetadataField, useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { getFulfillmentDotColor } from '@/types/content';
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
    const typeIconName = useTypeIconName()(item.contentType);
    const { contentTypes } = useLibraryTypes();
    const levelDef = contentTypes
        .find((type) => type.slug === item.contentType)
        ?.levels.find((level) => level.slug === item.level);

    const inner = (
        <>
            <span className="library-gallery-tile__poster">
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
                    <span className="library-gallery-tile__check">
                        <Checkbox checked={selected} size="sm" aria-hidden="true" tabIndex={-1} />
                    </span>
                )}
                <CdnPhosphorIcon
                    name={typeIconName}
                    weight="fill"
                    size={28}
                    className="library-gallery-tile__type"
                    aria-hidden="true"
                />
            </span>
            <span className="library-gallery-tile__title-row">
                {item.effectiveMonitoring ? (
                    <span
                        className="library-gallery-tile__status-dot"
                        style={{ '--dot-color': getFulfillmentDotColor(item.fulfillment) } as React.CSSProperties}
                        aria-hidden="true"
                    />
                ) : (
                    <ContentMonitoringIndicator
                        monitoring={item.monitoring}
                        effectiveMonitoring={item.effectiveMonitoring}
                        className="library-gallery-tile__unmonitored"
                    />
                )}
                <span className="library-gallery-tile__title" title={item.title}>
                    {item.title}
                    {item.year !== undefined && <span className="library-gallery-tile__year"> ({item.year})</span>}
                </span>
            </span>
        </>
    );

    if (selectionMode) {
        const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                onToggle?.();
            }
        };

        return (
            <div
                className="library-gallery-tile library-gallery-tile--selectable"
                role="button"
                tabIndex={0}
                aria-pressed={selected}
                aria-label={`Select ${item.title}`}
                data-selected={selected ? 'true' : 'false'}
                onClick={onToggle}
                onKeyDown={handleKeyDown}
            >
                {inner}
            </div>
        );
    }

    return (
        <Link
            to={`/library/${item.id}`}
            className="library-gallery-tile"
            data-content-id={item.id}
            data-selected="false"
            {...longPress.handlers}
            onClick={(event) => longPress.consumeClick(event)}
        >
            {inner}
        </Link>
    );
}

LibraryGalleryTile.displayName = 'LibraryGalleryTile';

export default LibraryGalleryTile;
