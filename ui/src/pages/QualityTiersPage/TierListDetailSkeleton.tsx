import { Skeleton } from '@/components/ui/Skeleton';

export function TierListDetailSkeleton() {
    return (
        <div className="tier-list-detail-page" data-page="tier-list-detail">
            <div className="tier-list-detail-page__loading">
                <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--breadcrumb" />
                <div className="tier-list-detail-page__loading-header">
                    <Skeleton
                        className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--icon"
                        variant="circle"
                    />
                    <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--title" />
                    <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--badge" />
                </div>
                <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--description" />
                <div className="tier-list-detail-page__loading-tiers">
                    <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--tier-card" />
                    <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--tier-card" />
                    <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--tier-card" />
                </div>
            </div>
        </div>
    );
}

TierListDetailSkeleton.displayName = 'TierListDetailSkeleton';
