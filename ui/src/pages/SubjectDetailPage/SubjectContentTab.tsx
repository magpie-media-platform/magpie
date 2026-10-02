import { useCallback } from 'react';
import { useOpenLocalContentPreview } from '@/components/content/LocalContentPreview';
import { FilmSlateIcon, MagnifyingGlassIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { ContentList, type ContentItem } from '@/components/ui/ContentList';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { Pager } from '@/components/ui/Pager';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    ContentFulfillmentBadge,
    ContentMonitoringIndicator,
    ContentTypeBadge,
} from '@/components/content/ContentCardPrimitives';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { formatShortDate } from '@/lib/dateFormatting';
import type { ContentListItem } from '@/types';

import { type SubjectContentController } from './useSubjectContent';
import './SubjectContentTab.css';

const CONTENT_COLUMN_VISIBILITY = { dateAdded: false, fulfilledDate: false };
const CONTENT_HEADER_FILTER_COLUMNS = ['title', 'contentType', 'fulfillment'] as const;
const CONTENT_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'publishDate', label: 'Published', initialDirection: 'desc' },
    { id: 'title', label: 'Title' },
];

function SubjectContentMobileCard({ item }: { item: ContentListItem }) {
    return (
        <MobileMediaCard
            to={`/library/${item.id}`}
            previewContentId={item.id}
            posterUrl={item.posterUrl}
            posterAlt={`${item.title} artwork`}
            title={
                <ContentLineageTitle
                    qualifiedName={{ id: item.id, parentId: item.parentId, title: item.title }}
                    fallback={{ contentType: item.contentType, level: item.level }}
                />
            }
            data-content-id={item.id}
            titleAccessory={
                <ContentMonitoringIndicator
                    monitoring={item.monitoring}
                    effectiveMonitoring={item.effectiveMonitoring}
                    className="subject-content-tab__mobile-monitoring"
                    activeClassName="subject-content-tab__mobile-monitoring--active"
                />
            }
        >
            <ContentTypeBadge level={item.level} levelLabel={item.levelLabel} contentType={item.contentType} />
            <ContentFulfillmentBadge fulfillment={item.fulfillment} />
            <span className="subject-content-tab__mobile-published">
                {formatShortDate(item.publishDate, '--', item.publishDatePrecision)}
            </span>
        </MobileMediaCard>
    );
}

export function SubjectContentTab({
    contentItems,
    totalItems,
    isLoading,
    error,
    sorting,
    onSortingChange,
    pagination,
    onPaginationChange,
    headerFilters,
    headerFilterHandlers,
    pageCount,
    currentPage,
    goToPage,
    hasActiveFilters,
}: SubjectContentController) {
    const openPreview = useOpenLocalContentPreview();
    const usesCardLayout = useCardLayout();

    const handleContentClick = useCallback(
        (item: ContentItem) => {
            openPreview(item.id);
        },
        [openPreview],
    );

    const emptyState = hasActiveFilters ? (
        <EmptyState
            icon={<MagnifyingGlassIcon weight="duotone" />}
            title="No results found"
            description="No content matches your current filters."
            size="sm"
        />
    ) : (
        <EmptyState
            icon={<FilmSlateIcon weight="thin" />}
            title="No content"
            description="No content is linked to this subject."
            size="sm"
        />
    );

    const renderBody = () => {
        if (isLoading) {
            return (
                <div className="subject-content-tab__loading" data-testid="subject-content-tab__loading">
                    <Skeleton className="subject-content-tab__skeleton" />
                    <Skeleton className="subject-content-tab__skeleton" />
                    <Skeleton className="subject-content-tab__skeleton" />
                </div>
            );
        }

        if (error) {
            return (
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title="Failed to load content"
                    size="sm"
                    data-testid="subject-content-tab__error"
                />
            );
        }

        return usesCardLayout ? (
            <div className="subject-content-tab__mobile">
                <MobileListToolbar
                    className="subject-content-tab__mobile-toolbar"
                    sorting={sorting}
                    sortOptions={CONTENT_SORT_OPTIONS}
                    onSortingChange={onSortingChange}
                />
                {contentItems.length === 0 ? (
                    emptyState
                ) : (
                    <div className="subject-content-tab__mobile-list">
                        {contentItems.map((item) => (
                            <SubjectContentMobileCard key={item.id} item={item} />
                        ))}
                    </div>
                )}
                {pageCount > 1 && (
                    <Pager
                        className="subject-content-tab__mobile-pager"
                        page={currentPage}
                        pageCount={pageCount}
                        totalItems={totalItems}
                        onFirst={() => goToPage(1)}
                        onPrevious={() => goToPage(currentPage - 1)}
                        onNext={() => goToPage(currentPage + 1)}
                        onLast={() => goToPage(pageCount)}
                    />
                )}
            </div>
        ) : (
            <ContentList
                items={contentItems}
                onContentClick={handleContentClick}
                enableSorting
                manualSorting
                sorting={sorting}
                onSortingChange={onSortingChange}
                enablePagination
                manualPagination
                pagination={pagination}
                onPaginationChange={onPaginationChange}
                rowCount={totalItems}
                pageSize={pagination.pageSize}
                columnVisibility={CONTENT_COLUMN_VISIBILITY}
                headerFilters={headerFilters}
                headerFilterHandlers={headerFilterHandlers}
                headerFilterColumns={CONTENT_HEADER_FILTER_COLUMNS}
                emptyState={emptyState}
                keepHeaderWhenEmpty={hasActiveFilters}
            />
        );
    };

    return (
        <div
            className="subject-content-tab"
            data-component="subject-content-tab"
            data-layout={usesCardLayout ? 'card' : 'table'}
        >
            {renderBody()}
        </div>
    );
}

SubjectContentTab.displayName = 'SubjectContentTab';

export default SubjectContentTab;
