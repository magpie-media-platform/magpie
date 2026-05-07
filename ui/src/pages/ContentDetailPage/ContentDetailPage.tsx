import { useMemo, useCallback } from 'react';
import { Link } from 'react-router-dom';
import {
    ArrowLeft,
    ArrowClockwise,
    MagnifyingGlass,
    ListBullets,
    File,
    ClockCounterClockwise,
    Info,
} from '@phosphor-icons/react';

import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { ErrorBoundary } from '@/components/ui/ErrorBoundary';
import { Button } from '@/components/ui/Button';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    ContentDetailsHeader,
    type ContentDetailItem,
    type ParentContentRef,
} from '@/components/ui/ContentDetailsHeader';
import { ContentDetailsHeaderSlot } from '@/components/ui/Slot/ContentSlots';
import { ExtensionTabTriggers, ExtensionTabContents } from '@/components/ui/Slot/ExtensionTabs';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import {
    OverviewTab,
    ChildrenTab,
    FilesTab,
    HistoryTab,
    type OverviewMetadata,
    type ChildItem,
    type MediaFile,
    type HistoryEvent,
} from '@/components/ui/ContentTabs';
import { useToast } from '@/components/ui/Toast';
import {
    useContentHierarchy,
    useContentChildren,
    useRefreshContent,
    useSearchContentReleases,
} from '@/hooks/useContent';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { isNotFoundError } from '@/lib/errors';
import type { ContentListItem } from '@/types';

import { useContentDetailParams } from './useContentDetailParams';
import './ContentDetailPage.css';

export function ContentDetailPage() {
    useDocumentTitle('Content');
    const {
        contentId,
        childId,
        grandchildId,
        activeContentId,
        parentId,
        rootId,
        activeTab,
        setActiveTab,
        navigate,
        childrenBasePath,
    } = useContentDetailParams();
    const { toast } = useToast();

    const {
        content: contentQuery,
        parent: parentQuery,
        root: rootQuery,
    } = useContentHierarchy(activeContentId, parentId, rootId);

    const {
        data: contentData,
        isLoading: isContentLoading,
        error: contentError,
        refetch: refetchContent,
    } = contentQuery;
    const { data: parentData } = parentQuery;
    const { data: rootData } = rootQuery;

    const { data: childrenResult } = useContentChildren(activeContentId);

    const refreshMutation = useRefreshContent();
    const searchMutation = useSearchContentReleases();

    const content: ContentDetailItem | null = useMemo(() => {
        if (!contentData) {
            return null;
        }
        return {
            id: contentData.id,
            title: contentData.title,
            posterUrl: contentData.posterUrl,
            kind: contentData.kind,
            monitoring: contentData.monitoring,
            fulfillment: contentData.fulfillment,
            year: contentData.year,
            overview: contentData.overview,
            subjectId: contentData.subjectId,
            subjectName: contentData.subjectName,
        };
    }, [contentData]);

    const breadcrumbs = useMemo((): BreadcrumbItem[] => {
        const items: BreadcrumbItem[] = [{ id: 'library', label: 'Library', to: '/library' }];

        if (contentId) {
            const rootTitle = grandchildId ? rootData?.title : childId ? parentData?.title : contentData?.title;

            items.push({
                id: contentId,
                label: rootTitle ?? '',
                loading: !rootTitle,
                to: `/library/${contentId}`,
            });
        }

        if (childId) {
            const childTitle = grandchildId ? parentData?.title : contentData?.title;

            items.push({
                id: childId,
                label: childTitle ?? '',
                loading: !childTitle,
                to: `/library/${contentId}/${childId}`,
            });
        }

        if (grandchildId && contentData) {
            items.push({
                id: grandchildId,
                label: contentData.title,
                to: `/library/${contentId}/${childId}/${grandchildId}`,
            });
        }

        return items;
    }, [contentId, childId, grandchildId, contentData, parentData, rootData]);

    const parentRef = useMemo((): ParentContentRef | undefined => {
        if (!parentData) {
            return undefined;
        }

        if (grandchildId && childId) {
            return {
                id: childId,
                title: parentData.title,
                path: `/library/${contentId}/${childId}`,
            };
        } else if (childId && contentId) {
            return {
                id: contentId,
                title: parentData.title,
                path: `/library/${contentId}`,
            };
        }

        return undefined;
    }, [contentId, childId, grandchildId, parentData]);

    const overviewData: OverviewMetadata = useMemo(() => {
        return {
            overview: contentData?.overview,
        };
    }, [contentData]);

    const childrenData: ChildItem[] = useMemo(() => {
        if (!childrenResult?.items) {
            return [];
        }
        return childrenResult.items.map((item: ContentListItem, index: number) => ({
            id: item.id,
            title: item.title,
            number: index + 1,
            monitoring: item.monitoring,
            fulfillment: item.fulfillment,
        }));
    }, [childrenResult?.items]);

    const filesData: MediaFile[] = useMemo(() => [], []);
    const historyData: HistoryEvent[] = useMemo(() => [], []);

    const hasChildren = childrenData.length > 0;

    const childrenTabLabel = useMemo(() => {
        if (childId) {
            return 'Episodes';
        }
        return 'Seasons';
    }, [childId]);

    const handleChildClick = (child: ChildItem) => {
        navigate(`${childrenBasePath}/${child.id}`);
    };

    const handleRefresh = useCallback(() => {
        if (!activeContentId) {
            return;
        }

        refreshMutation.mutate(activeContentId, {
            onSuccess: () => {
                toast({
                    title: 'Refresh started',
                    description: 'Metadata refresh has been queued.',
                    variant: 'success',
                });
            },
            onError: () => {
                toast({
                    title: 'Refresh failed',
                    description: 'Failed to start metadata refresh. Please try again.',
                    variant: 'error',
                });
            },
        });
    }, [activeContentId, refreshMutation, toast]);

    const handleSearchNow = useCallback(() => {
        if (!activeContentId) {
            return;
        }

        searchMutation.mutate(activeContentId, {
            onSuccess: () => {
                toast({
                    title: 'Search started',
                    description: 'Release search has been queued.',
                    variant: 'success',
                });
            },
            onError: () => {
                toast({
                    title: 'Search failed',
                    description: 'Failed to start release search. Please try again.',
                    variant: 'error',
                });
            },
        });
    }, [activeContentId, searchMutation, toast]);

    const quickActions: QuickAction[] = useMemo(
        () => [
            {
                id: 'refresh',
                label: 'Refresh Metadata',
                icon: <ArrowClockwise weight="bold" />,
                onClick: handleRefresh,
            },
            {
                id: 'search',
                label: 'Search Now',
                icon: <MagnifyingGlass weight="bold" />,
                onClick: handleSearchNow,
                variant: 'primary' as const,
            },
        ],
        [handleRefresh, handleSearchNow],
    );

    if (isContentLoading) {
        return (
            <div className="content-detail-page" data-page="content-detail">
                <div className="content-detail-page__loading" data-testid="content-detail-page__loading">
                    <Skeleton className="content-detail-page__skeleton content-detail-page__skeleton--breadcrumb" />
                    <div className="content-detail-page__loading-header">
                        <Skeleton className="content-detail-page__skeleton content-detail-page__skeleton--poster" />
                        <div className="content-detail-page__loading-info">
                            <Skeleton className="content-detail-page__skeleton content-detail-page__skeleton--title" />
                            <Skeleton className="content-detail-page__skeleton content-detail-page__skeleton--meta" />
                        </div>
                    </div>
                    <Skeleton className="content-detail-page__skeleton content-detail-page__skeleton--tabs" />
                </div>
            </div>
        );
    }

    if (contentError || !content) {
        const notFound = !contentError || isNotFoundError(contentError);
        return (
            <div className="content-detail-page" data-page="content-detail">
                <div className="content-detail-page__not-found">
                    <h1>{notFound ? 'Content Not Found' : 'Failed to Load Content'}</h1>
                    <p>
                        {notFound
                            ? 'The content you are looking for does not exist.'
                            : 'An error occurred while loading this content.'}
                    </p>
                    {notFound ? (
                        <Link to="/library" className="content-detail-page__back-link">
                            <ArrowLeft weight="bold" aria-hidden="true" />
                            Back to Library
                        </Link>
                    ) : (
                        <Button variant="secondary" onClick={() => refetchContent()}>
                            <ArrowClockwise weight="bold" aria-hidden="true" />
                            Retry
                        </Button>
                    )}
                </div>
            </div>
        );
    }

    const headerActions = (
        <>
            <Button
                variant="secondary"
                size="sm"
                onClick={handleRefresh}
                loading={refreshMutation.isPending}
                data-action="refresh"
            >
                <ArrowClockwise weight="bold" aria-hidden="true" />
                Refresh
            </Button>
            <Button
                variant="secondary"
                size="sm"
                onClick={handleSearchNow}
                disabled={searchMutation.isPending}
                data-action="search"
            >
                <MagnifyingGlass weight="bold" aria-hidden="true" />
                Search Now
            </Button>
        </>
    );

    return (
        <div className="content-detail-page" data-page="content-detail">
            <Breadcrumb items={breadcrumbs} className="content-detail-page__breadcrumb" />

            <ContentDetailsHeaderSlot>
                <ContentDetailsHeader content={content} parent={parentRef} actions={headerActions} />
            </ContentDetailsHeaderSlot>

            <Tabs value={activeTab} onValueChange={setActiveTab} className="content-detail-page__tabs">
                <TabsList>
                    <TabsTrigger value="overview">
                        <Info weight="bold" aria-hidden="true" />
                        <span>Overview</span>
                    </TabsTrigger>
                    {hasChildren && (
                        <TabsTrigger value="children">
                            <ListBullets weight="bold" aria-hidden="true" />
                            <span>{childrenTabLabel}</span>
                            <span className="tab-count">{childrenData.length}</span>
                        </TabsTrigger>
                    )}
                    <TabsTrigger value="files">
                        <File weight="bold" aria-hidden="true" />
                        <span>Files</span>
                        {filesData.length > 0 && <span className="tab-count">{filesData.length}</span>}
                    </TabsTrigger>
                    <TabsTrigger value="history">
                        <ClockCounterClockwise weight="bold" aria-hidden="true" />
                        <span>History</span>
                    </TabsTrigger>
                    <ExtensionTabTriggers slot="content.details.tabs" />
                </TabsList>

                <TabsContent value="overview">
                    <ErrorBoundary>
                        <OverviewTab metadata={overviewData} />
                    </ErrorBoundary>
                </TabsContent>

                {hasChildren && (
                    <TabsContent value="children">
                        <ErrorBoundary>
                            <ChildrenTab
                                items={childrenData}
                                parentId={activeContentId!}
                                onChildClick={handleChildClick}
                                basePath={childrenBasePath}
                                emptyMessage={`No ${childrenTabLabel.toLowerCase()} found`}
                            />
                        </ErrorBoundary>
                    </TabsContent>
                )}

                <TabsContent value="files">
                    <ErrorBoundary>
                        <FilesTab files={filesData} emptyMessage="No files attached to this content" />
                    </ErrorBoundary>
                </TabsContent>

                <TabsContent value="history">
                    <ErrorBoundary>
                        <HistoryTab events={historyData} emptyMessage="No history for this content" />
                    </ErrorBoundary>
                </TabsContent>

                <ExtensionTabContents slot="content.details.tabs" />
            </Tabs>

            <QuickActionsButton actions={quickActions} title="Content Actions" data-content-id={activeContentId} />
        </div>
    );
}

ContentDetailPage.displayName = 'ContentDetailPage';

export default ContentDetailPage;
