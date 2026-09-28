import { AcquisitionBadge } from '@/components/ui/AcquisitionBadge';
import { Badge } from '@/components/ui/Badge';
import { ContentFulfillmentBadge, ContentMonitoringIndicator } from '@/components/content/ContentCardPrimitives';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { formatShortDate } from '@/lib/dateFormatting';
import { formatPrimaryMediaSummary } from '@/lib/formatSize';
import type { ContentListItem } from '@/types';

interface LibraryMobileCardProps {
    item: ContentListItem;
    selectionMode?: boolean;
    selected?: boolean;
    onToggle?: () => void;
    onLongPress?: () => void;
}

export function LibraryMobileCard({ item, selectionMode, selected, onToggle, onLongPress }: LibraryMobileCardProps) {
    const { contentTypes } = useLibraryTypes();
    const kindLabel = resolveContentTypeLabel(
        { level: item.level, levelLabel: item.levelLabel, contentType: item.contentType },
        contentTypes,
    );

    return (
        <MobileMediaCard
            to={`/library/${item.id}`}
            data-content-id={item.id}
            posterUrl={item.posterUrl}
            posterAlt={`${item.title} artwork`}
            title={
                <ContentLineageTitle
                    qualifiedName={{ id: item.id, parentId: item.parentId, title: item.title }}
                    fallback={{ contentType: item.contentType, level: item.level }}
                />
            }
            selectable={selectionMode}
            selected={selected}
            onToggleSelected={onToggle}
            onLongPress={onLongPress}
            selectionLabel={`Select ${item.title} card`}
            titleAccessory={
                <ContentMonitoringIndicator
                    monitoring={item.monitoring}
                    effectiveMonitoring={item.effectiveMonitoring}
                    className="library-page__mobile-eye"
                    activeClassName="library-page__mobile-eye--on"
                    dataMonitoring={item.effectiveMonitoring !== 'Unmonitored' ? 'true' : 'false'}
                />
            }
            description={item.overview || undefined}
        >
            {kindLabel && (
                <Badge variant="default" size="sm">
                    {kindLabel}
                </Badge>
            )}
            <ContentFulfillmentBadge fulfillment={item.fulfillment} importPending={item.importPending} />
            {item.acquisitionStatus && <AcquisitionBadge status={item.acquisitionStatus} />}
            <span>Media: {formatPrimaryMediaSummary(item.primaryMediaFileCount, item.primaryMediaSize)}</span>
            <span>Fulfilled: {item.fulfilledAt ? formatShortDate(item.fulfilledAt) : '—'}</span>
        </MobileMediaCard>
    );
}

LibraryMobileCard.displayName = 'LibraryMobileCard';

export default LibraryMobileCard;
