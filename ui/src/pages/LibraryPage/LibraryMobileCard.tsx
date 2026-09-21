import { EyeIcon, EyeSlashIcon } from '@phosphor-icons/react';

import { AcquisitionBadge } from '@/components/ui/AcquisitionBadge';
import { Badge } from '@/components/ui/Badge';
import { ContentFulfillmentBadge } from '@/components/content/ContentCardPrimitives';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
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
    const monitored = item.effectiveMonitoring;
    const monitoringLabel = monitored ? 'Monitored' : 'Not monitored';
    const monitoringTitle = item.monitoring === null ? `${monitoringLabel} (inherited)` : monitoringLabel;
    const MonitoringIcon = monitored ? EyeIcon : EyeSlashIcon;
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
                <span
                    className={`library-page__mobile-eye${monitored ? ' library-page__mobile-eye--on' : ''}`}
                    title={monitoringTitle}
                    data-monitoring={item.effectiveMonitoring}
                >
                    <MonitoringIcon weight={monitored ? 'fill' : 'regular'} aria-label={monitoringTitle} />
                </span>
            }
            description={item.overview || undefined}
        >
            {kindLabel && (
                <Badge variant="default" size="sm">
                    {kindLabel}
                </Badge>
            )}
            <ContentFulfillmentBadge fulfillment={item.fulfillment} effectiveMonitoring={item.effectiveMonitoring} />
            {item.acquisitionStatus && <AcquisitionBadge status={item.acquisitionStatus} />}
        </MobileMediaCard>
    );
}

LibraryMobileCard.displayName = 'LibraryMobileCard';

export default LibraryMobileCard;
