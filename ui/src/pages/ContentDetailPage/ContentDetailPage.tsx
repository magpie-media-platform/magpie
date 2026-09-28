import { useMemo, useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
    ArrowLeftIcon,
    ArrowClockwiseIcon,
    MagnifyingGlassIcon,
    FileMagnifyingGlassIcon,
    ListMagnifyingGlassIcon,
    ListBulletsIcon,
    FileIcon,
    PulseIcon,
    DownloadSimpleIcon,
    InfoIcon,
    TagIcon,
    TrashIcon,
    UsersThreeIcon,
    FilmSlateIcon,
    ImagesIcon,
    PuzzlePieceIcon,
    LinkBreakIcon,
    WarningCircleIcon,
    PencilSimpleLineIcon,
    PlusIcon,
} from '@phosphor-icons/react';

import { useAuth, Role } from '@/auth';
import { useContentOrigin, resolveContentOrigin } from '@/hooks/useContentOrigin';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog';
import { FileBrowser } from '@/components/configuration/FileBrowser';
import { FileRenameDialog } from '@/components/content/FileRenameDialog';
import { PendingFileAssociations, PendingFilesIndicator } from '@/components/content/PendingFileAssociations';
import { FileReassignDialog } from '@/components/content/FileReassignDialog';
import { ReleaseDetailsDialog } from '@/components/releases/ReleaseDetailsDialog';
import { ErrorBoundary } from '@/components/ui/ErrorBoundary';
import { ImagesGallery } from '@/components/ui/ImagesGallery';
import { DetailInfoGrid, DetailInfoRow } from '@/components/ui/DetailInfoGrid';
import { CollapsibleChipList } from '@/components/ui/CollapsibleChipList';
import { Button } from '@/components/ui/Button';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    ContentDetailsHeader,
    type ContentDetailItem,
    type ParentContentRef,
} from '@/components/content/ContentDetailsHeader';
import {
    ContentDetailsActionsSlot,
    ContentDetailsFilesSlot,
    ContentDetailsHeaderSlot,
    ContentDetailsMetadataSlot,
    ContentDetailsOverviewSlot,
} from '@/components/ui/Slot/ContentSlots';
import { ExtensionTabContents, useTabSlotExtensions } from '@/components/ui/Slot/ExtensionTabs';
import { useSlotExtensions } from '@/extensions/useExtensions';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Tabs, TabsContent, DetailTabTrigger, DetailTabsList } from '@/components/ui/Tabs';
import {
    ContentFactsPanel,
    ChildrenTab,
    FilesTab,
    ReleasesTab,
    PreviewsTab,
    type ContentFacts,
    type ChildItem,
    type MediaFile,
    type ReleaseRow,
} from '@/components/content/ContentTabs';
import type { PreviewClip } from '@/components/ui/PreviewPlayer';
import { useToast } from '@/components/ui/Toast';
import {
    useContentHierarchy,
    useContentAncestors,
    useContentList,
    useRefreshContent,
    useSearchContentReleases,
    useRescanContentMediaInfo,
    useMetadataInspector,
} from '@/hooks/useContent';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useGrabRelease } from '@/hooks/useReleases';
import { useReleaseGrabGuard } from '@/hooks/useReleaseGrabGuard';
import { useContentReleases, useSetContentReleaseUnmatched } from '@/hooks/useContentReleases';
import { useDeleteContentFile } from '@/hooks/useDeleteContentFile';
import {
    useAttachContentFile,
    useContentFileBrowserLocation,
    useUnlinkContentFile,
} from '@/hooks/useContentFileMutations';
import { useRemoveContent } from '@/hooks/useRemoveContent';
import { canRegrab, useLatestDownloadForRelease } from '@/hooks/useDownloads';
import { getRootLevels, getStandardMetadataField, useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { buildUrl } from '@/lib/api';
import { getErrorMessage, isNotFoundError } from '@/lib/errors';
import { formatPublishDate } from '@/lib/dateFormatting';
import { humanizeSlug, resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { isInternalSource, referenceDisplayName } from '@/lib/metadata';
import {
    buildAlternateTextRows,
    buildAwardGroups,
    buildMaterializedDetailRows,
    nonEmptyMetadataText,
    sortByOrdinal,
} from '@/lib/materializedMetadataDisplay';
import type { ContentListItem, ReleaseScopeStatus } from '@/types';

import { RelatedSubjectsTab } from './RelatedSubjectsTab';
import { ActivityTab } from './ActivityTab';
import { ContentViewOnLinks } from './ContentViewOnLinks';
import { RootFolderControl } from './RootFolderControl';
import { QualityTierControl } from './QualityTierControl';
import { MetadataInspector } from '@/components/metadata-inspector/MetadataInspector';
import { MonitoringControl } from './MonitoringControl';
import { PathControl } from './PathControl';
import { RefreshStatusBadge } from './RefreshStatusBadge';
import { TagsChipStrip } from './TagsChipStrip';
import { useContentDetailParams } from './useContentDetailParams';
import './ContentDetailPage.css';
import '../DetailPageStates.css';

const RELATED_TAB = 'related';
const CHILDREN_TAB = 'children';
const CHILDREN_FETCH_LIMIT = 1000;
interface TabDef {
    value: string;
    label: string;
    icon?: ReactNode;
    count?: number;
}

function pluralizeLevelLabel(label: string): string {
    const name = label.trim();
    if (!name) {
        return 'Items';
    }
    if (/[^aeiou]y$/i.test(name)) {
        return `${name.slice(0, -1)}ies`;
    }
    return /s$/i.test(name) ? name : `${name}s`;
}

export function ContentDetailPage() {
    useDocumentTitle('Content');
    const { contentId, childId, grandchildId } = useParams<{
        contentId: string;
        childId?: string;
        grandchildId?: string;
    }>();
    const activeContentId = grandchildId || childId || contentId;
    const contentOrigin = useContentOrigin(activeContentId);
    const parentId = grandchildId ? childId : childId ? contentId : undefined;
    const rootId = grandchildId ? contentId : undefined;

    const { data: childrenResult, isLoading: isChildrenLoading } = useContentList(
        activeContentId
            ? {
                  parentId: activeContentId,
                  sortBy: 'SequenceNumber',
                  sortDirection: 'Ascending',
                  pageSize: CHILDREN_FETCH_LIMIT,
              }
            : undefined,
        { enabled: !!activeContentId },
    );
    const { content: contentQuery, parent: parentQuery } = useContentHierarchy(activeContentId, parentId, rootId);
    const metadataQuery = useMetadataInspector(activeContentId);

    const {
        data: contentData,
        isLoading: isContentLoading,
        error: contentError,
        refetch: refetchContent,
    } = contentQuery;
    const { data: parentData } = parentQuery;

    const { contentTypes, isLoading: isLibraryTypesLoading, error: libraryTypesError } = useLibraryTypes();
    const typeDef = contentTypes.find((type) => type.slug === contentData?.contentType);
    const levelDef = typeDef?.levels.find((level) => level.slug === contentData?.level);
    const metadata = metadataQuery.data?.materialized;

    const showChildrenTab = useMemo(() => {
        if (!contentData?.level || !contentData.contentType) {
            return false;
        }
        return typeDef?.levels.some((l) => l.parentSlug === contentData.level) ?? false;
    }, [contentData?.level, contentData?.contentType, typeDef]);

    const hasRelationships = (metadata?.relationships?.length ?? 0) > 0;
    const defaultTab = showChildrenTab ? CHILDREN_TAB : hasRelationships ? RELATED_TAB : 'details';

    const { activeTab, setActiveTab, navigate, childrenBasePath, rawTab } = useContentDetailParams(defaultTab);

    const { toast } = useToast();
    const { hasPermission, hasRole } = useAuth();
    const canEditContent = hasPermission('content.edit');
    const canRequestContent = hasPermission('content.request');
    const canManageContent = hasPermission('content.delete');
    const { ancestors } = useContentAncestors(contentData?.parentId);
    const correctionShowId = useMemo(() => {
        if (contentData?.contentType !== 'tv') {
            return undefined;
        }
        if (contentData.level === 'show') {
            return contentData.id;
        }
        if (!contentData.parentId || ancestors[ancestors.length - 1]?.id !== contentData.parentId) {
            return undefined;
        }
        return ancestors.find((ancestor) => ancestor.contentType === 'tv' && ancestor.level === 'show')?.id;
    }, [ancestors, contentData]);

    const typeIconName = useTypeIconName();

    const { tabs: extensionTabs, isReady: areExtensionTabsReady } = useTabSlotExtensions('content.details.tabs');

    const refreshMutation = useRefreshContent();
    const searchMutation = useSearchContentReleases();
    const rescanMediaInfoMutation = useRescanContentMediaInfo();
    const grabMutation = useGrabRelease();
    const {
        pendingReleaseIds: grabbingReleaseIds,
        succeededReleaseIds,
        tryBeginGrab,
        markGrabSucceeded,
        markGrabFailed,
        clearGrab,
    } = useReleaseGrabGuard();
    const [pendingGrab, setPendingGrab] = useState<ReleaseRow | null>(null);
    const deleteFileMutation = useDeleteContentFile();
    const [filePendingDelete, setFilePendingDelete] = useState<MediaFile | null>(null);
    const attachFileMutation = useAttachContentFile();
    const unlinkFileMutation = useUnlinkContentFile();
    const fileBrowserLocationQuery = useContentFileBrowserLocation(activeContentId);
    const [importDialogOpen, setImportDialogOpen] = useState(false);
    const [renameSelection, setRenameSelection] = useState<{ filePath?: string; mediaFileIds?: string[] } | null>(null);
    const [filePendingUnlink, setFilePendingUnlink] = useState<MediaFile | null>(null);
    const [filePendingReassignment, setFilePendingReassignment] = useState<MediaFile | null>(null);
    // The control that opened a file or remove dialog (a menu, a button or an actions sheet). Focus returns to it on
    // close, or to the content actions trigger when an actions sheet item opened the dialog and is gone.
    const actionReturnFocusRef = useRef<HTMLElement | null>(null);
    const rememberActionFocus = useCallback(() => {
        actionReturnFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    }, []);
    const filesTabPanelRef = useRef<HTMLDivElement | null>(null);
    const contentActionsTriggerRef = useRef<HTMLElement | null>(null);
    const removeContentMutation = useRemoveContent();
    const [removeDialogOpen, setRemoveDialogOpen] = useState(false);
    const [removeDeleteFiles, setRemoveDeleteFiles] = useState(false);

    const releasesQuery = useContentReleases(activeContentId, { enabled: !!activeContentId });
    const unmatchMutation = useSetContentReleaseUnmatched();
    const setReleaseUnmatched = unmatchMutation.mutate;

    const [detailsRelease, setDetailsRelease] = useState<ReleaseRow | null>(null);
    const { download: detailsDownload, isLoading: detailsDownloadLoading } = useLatestDownloadForRelease(
        detailsRelease?.acquisitionStatus ? detailsRelease.id : undefined,
    );

    const content: ContentDetailItem | null = useMemo(() => {
        if (!contentData) {
            return null;
        }
        const year = metadata?.publishDate ? new Date(metadata.publishDate).getUTCFullYear() : undefined;
        const posterUrl = sortByOrdinal(metadata?.images).flatMap((image) => {
            const url = nonEmptyMetadataText(image.url);
            return url ? [{ url, isPrimary: image.isPrimary }] : [];
        });
        return {
            id: contentData.id,
            title: nonEmptyMetadataText(metadata?.title) ?? contentData.title,
            posterUrl: posterUrl.find((image) => image.isPrimary)?.url ?? posterUrl[0]?.url ?? contentData.posterUrl,
            level: contentData.level,
            levelLabel: contentData.levelLabel,
            contentType: contentData.contentType,
            monitoring: contentData.monitoring,
            effectiveMonitoring: contentData.effectiveMonitoring,
            fulfillment: contentData.fulfillment,
            importPending: contentData.importPending,
            acquisitionStatus: contentData.acquisitionStatus,
            year: year !== undefined && !Number.isNaN(year) ? year : undefined,
            publishDate: nonEmptyMetadataText(metadata?.publishDate),
            overview: nonEmptyMetadataText(metadata?.overview),
        };
    }, [contentData, metadata]);

    const breadcrumbs = useMemo((): BreadcrumbItem[] => {
        const items: BreadcrumbItem[] = [{ id: 'library', label: 'Library', to: '/library' }];

        const rootItem = ancestors[0] ?? contentData;
        const contentType = contentData?.contentType ?? rootItem?.contentType;

        if (contentType) {
            const typeDef = contentTypes.find((t) => t.slug === contentType);
            const typeName = typeDef?.name;
            const iconName = typeIconName(contentType);

            items.push({
                id: `content-type:${contentType}`,
                label: typeName ?? humanizeSlug(contentType),
                loading: !typeName,
                to: `/library/content/${contentType}`,
                icon: <CdnPhosphorIcon name={iconName} weight="regular" />,
            });

            const rootLevels = getRootLevels(typeDef);
            if (rootLevels.length > 1 && rootItem?.level) {
                items.push({
                    id: `content-level:${contentType}:${rootItem.level}`,
                    label: pluralizeLevelLabel(
                        resolveContentTypeLabel({ level: rootItem.level, contentType }, contentTypes),
                    ),
                    to: `/library/content/${contentType}/${rootItem.level}`,
                });
            }
        }

        for (const ancestor of ancestors) {
            items.push({
                id: ancestor.id,
                label: ancestor.title,
                to: `/library/${ancestor.id}`,
            });
        }

        if (contentData) {
            items.push({
                id: contentData.id,
                label: contentData.title,
                to: `/library/${contentData.id}`,
            });
        }

        return items;
    }, [contentData, ancestors, contentTypes, typeIconName]);

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

    const previews: PreviewClip[] = useMemo(() => {
        if (!getStandardMetadataField(levelDef, 'Previews')) {
            return [];
        }
        return sortByOrdinal(metadata?.previews).flatMap((preview) => {
            const url = nonEmptyMetadataText(preview.url);
            const kind = nonEmptyMetadataText(preview.kind);
            return url && kind
                ? [
                      {
                          url,
                          kind,
                          title: nonEmptyMetadataText(preview.title),
                          mimeType: nonEmptyMetadataText(preview.mimeType),
                          embedKind: nonEmptyMetadataText(preview.embedKind),
                          language: nonEmptyMetadataText(preview.language),
                      },
                  ]
                : [];
        });
    }, [levelDef, metadata?.previews]);

    const hasPreviews = previews.length > 0;

    const images = useMemo(
        () =>
            sortByOrdinal(metadata?.images).flatMap((image) => {
                const url = nonEmptyMetadataText(image.url);
                const type = nonEmptyMetadataText(image.type);
                return url && type
                    ? [
                          {
                              id: image.id,
                              url,
                              type,
                              isPrimary: image.isPrimary,
                              width: image.width ?? undefined,
                              height: image.height ?? undefined,
                          },
                      ]
                    : [];
            }),
        [metadata?.images],
    );
    const imageCount = images.length;

    const heroPreview = useMemo<PreviewClip | undefined>(() => {
        if (previews.length === 0) {
            return undefined;
        }
        return previews[0];
    }, [previews]);

    const genres = useMemo(
        () =>
            getStandardMetadataField(levelDef, 'Genres')
                ? sortByOrdinal(metadata?.genres).flatMap((genre) => nonEmptyMetadataText(genre.name) ?? [])
                : [],
        [levelDef, metadata?.genres],
    );

    const awards = useMemo(() => buildAwardGroups(metadata), [metadata]);
    const contentRatings = useMemo(
        () =>
            getStandardMetadataField(levelDef, 'ContentRatings')
                ? sortByOrdinal(metadata?.contentRatings).flatMap((rating) => {
                      const value = nonEmptyMetadataText(rating.rating);
                      return value
                          ? [
                                {
                                    id: rating.id,
                                    body: nonEmptyMetadataText(rating.body) ?? 'Rating',
                                    rating: value,
                                },
                            ]
                          : [];
                  })
                : [],
        [levelDef, metadata?.contentRatings],
    );

    const externalIds = useMemo(
        () =>
            sortByOrdinal(metadata?.externalIds).flatMap((externalId) => {
                const value = nonEmptyMetadataText(externalId.value);
                const source = nonEmptyMetadataText(externalId.source) ?? 'unknown';
                return value && !isInternalSource(source)
                    ? [
                          {
                              source,
                              value,
                              url: contentData?.externalIds?.find(
                                  (candidate) => candidate.source === source && candidate.value === value,
                              )?.url,
                          },
                      ]
                    : [];
            }),
        [contentData?.externalIds, metadata?.externalIds],
    );
    const references = useMemo(
        () =>
            sortByOrdinal(metadata?.references).flatMap((reference) => {
                const url = nonEmptyMetadataText(reference.url);
                return url
                    ? [
                          {
                              url,
                              displayName: referenceDisplayName(url, reference.displayName),
                              source: reference.source,
                              kind: reference.kind,
                          },
                      ]
                    : [];
            }),
        [metadata?.references],
    );

    const metadataRows = useMemo(
        () => buildMaterializedDetailRows(metadata, levelDef?.standardMetadataFields, levelDef?.customMetadataFields),
        [levelDef, metadata],
    );
    const contentFacts: ContentFacts = useMemo(() => {
        return {
            detailRows: metadataRows,
            alternateTexts: buildAlternateTextRows(metadata),
            externalIds: externalIds.map((externalId) => ({
                source: externalId.source,
                id: externalId.value,
            })),
        };
    }, [externalIds, metadata, metadataRows]);

    const childrenData: ChildItem[] = useMemo(() => {
        if (!childrenResult?.items) {
            return [];
        }
        return childrenResult.items.map((item: ContentListItem) => ({
            id: item.id,
            title: item.title,
            number: item.sequenceNumber,
            posterUrl: item.posterUrl,
            monitoring: item.monitoring,
            effectiveMonitoring: item.effectiveMonitoring,
            fulfillment: item.fulfillment,
            importPending: item.importPending,
            acquisitionStatus: item.acquisitionStatus,
            date: item.publishDate,
            fulfilledAt: item.fulfilledAt,
            primaryMediaSize: item.primaryMediaSize,
            primaryMediaFileCount: item.primaryMediaFileCount,
        }));
    }, [childrenResult?.items]);

    const filesData: MediaFile[] = useMemo(() => {
        const apiFiles = contentData?.mediaFiles ?? [];
        return apiFiles.map((f): MediaFile => {
            const lastSep = Math.max(f.filePath.lastIndexOf('/'), f.filePath.lastIndexOf('\\'));
            return {
                id: f.id,
                relativePath: f.filePath,
                fileName: lastSep >= 0 ? f.filePath.slice(lastSep + 1) : f.filePath,
                size: f.fileSize,
                dateAdded: f.createdAt,
                mediaInfo: f.mediaInfo ?? undefined,
            };
        });
    }, [contentData?.mediaFiles]);

    useEffect(() => {
        for (const release of releasesQuery.rows) {
            if (succeededReleaseIds.has(release.id) && !canRegrab(release.acquisitionStatus)) {
                clearGrab(release.id);
            }
        }
    }, [clearGrab, releasesQuery.rows, succeededReleaseIds]);

    const releasesData: ReleaseRow[] = useMemo(() => {
        return releasesQuery.rows.map((release) => ({
            id: release.id,
            title: release.title,
            indexerName: release.indexerName ?? null,
            protocol: release.protocol,
            publishDate: release.publishDate,
            size: release.size ?? null,
            infoUrl: release.infoUrl ?? null,
            externalId: release.externalId ?? null,
            categories: release.categories ?? [],
            languages: release.languages ?? [],
            properties: release.properties ?? null,
            isMatch: release.isMatch ?? null,
            rejectionReason: release.rejectionReason ?? null,
            acquisitionStatus: succeededReleaseIds.has(release.id)
                ? canRegrab(release.acquisitionStatus)
                    ? 'Queued'
                    : release.acquisitionStatus
                : (release.acquisitionStatus ?? null),
            acquisitionWaitingReason: release.acquisitionWaitingReason ?? null,
            failureReason: release.failureReason ?? null,
            status: release.status ?? 'Matched',
        }));
    }, [releasesQuery.rows, succeededReleaseIds]);

    const childLevel = typeDef?.levels.find((level) => level.parentSlug === contentData?.level);
    const childrenTabLabel = pluralizeLevelLabel(childLevel?.displayName ?? '');
    const childrenDateHeader =
        childLevel?.standardFactFields
            ?.find((field) => field.dataType?.toLowerCase() === 'date')
            ?.displayName?.trim() || 'Date';

    const tabDefs: TabDef[] = useMemo(() => {
        const defs: TabDef[] = [];
        if (showChildrenTab) {
            defs.push({
                value: 'children',
                label: childrenTabLabel,
                icon: <ListBulletsIcon weight="bold" aria-hidden="true" />,
                count: childrenResult?.totalItems,
            });
        }
        if (hasRelationships) {
            defs.push({
                value: 'related',
                label: 'Related',
                icon: <UsersThreeIcon weight="bold" aria-hidden="true" />,
            });
        }
        defs.push({ value: 'details', label: 'Details', icon: <InfoIcon weight="bold" aria-hidden="true" /> });
        if (hasPreviews) {
            defs.push({
                value: 'previews',
                label: 'Previews',
                icon: <FilmSlateIcon weight="bold" aria-hidden="true" />,
                count: previews.length,
            });
        }
        if (imageCount > 0) {
            defs.push({
                value: 'images',
                label: 'Images',
                icon: <ImagesIcon weight="bold" aria-hidden="true" />,
                count: imageCount,
            });
        }
        defs.push(
            {
                value: 'releases',
                label: 'Releases',
                icon: <DownloadSimpleIcon weight="bold" aria-hidden="true" />,
                count: releasesData.length > 0 ? releasesData.length : undefined,
            },
            {
                value: 'files',
                label: 'Files',
                icon: <FileIcon weight="bold" aria-hidden="true" />,
                count: filesData.length > 0 ? filesData.length : undefined,
            },
            { value: 'metadata', label: 'Metadata', icon: <TagIcon weight="bold" aria-hidden="true" /> },
            { value: 'activity', label: 'Activity', icon: <PulseIcon weight="bold" aria-hidden="true" /> },
        );
        for (const ext of extensionTabs) {
            defs.push({
                value: `ext-${ext.id}`,
                label: ext.attributes?.['tab-label'] ?? ext.name,
                icon: <PuzzlePieceIcon weight="bold" aria-hidden="true" />,
            });
        }
        return defs;
    }, [
        showChildrenTab,
        hasRelationships,
        childrenTabLabel,
        childrenResult?.totalItems,
        hasPreviews,
        previews.length,
        imageCount,
        releasesData.length,
        filesData.length,
        extensionTabs,
    ]);

    const effectiveTab = tabDefs.some((tab) => tab.value === activeTab) ? activeTab : defaultTab;

    useEffect(() => {
        if (
            !contentData ||
            contentError ||
            isContentLoading ||
            metadataQuery.error ||
            metadataQuery.isLoading ||
            isLibraryTypesLoading ||
            libraryTypesError ||
            !areExtensionTabsReady ||
            (activeTab === effectiveTab && rawTab !== '')
        ) {
            return;
        }

        setActiveTab(effectiveTab);
    }, [
        activeTab,
        areExtensionTabsReady,
        contentData,
        contentError,
        effectiveTab,
        isContentLoading,
        isLibraryTypesLoading,
        libraryTypesError,
        metadataQuery.error,
        metadataQuery.isLoading,
        rawTab,
        setActiveTab,
    ]);

    const handleChildClick = (child: ChildItem) => {
        void navigate(`${childrenBasePath}/${child.id}`);
    };

    const handleRefresh = useCallback(() => {
        if (!activeContentId) {
            return;
        }

        refreshMutation.mutate(activeContentId, {
            onSuccess: () => {
                toast({
                    title: 'Refresh complete',
                    description: 'Metadata refresh finished.',
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

        searchMutation.mutate(
            { id: activeContentId },
            {
                onSuccess: (disposition) => {
                    toast({
                        title: 'Search complete',
                        description: disposition.statusMessage ?? 'Release search complete.',
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
            },
        );
    }, [activeContentId, searchMutation, toast]);

    const handleInteractiveSearch = useCallback(() => {
        if (!activeContentId) {
            return;
        }

        void navigate(`/interactive-search/${activeContentId}`);
    }, [activeContentId, navigate]);

    const handleRescanMediaInfo = useCallback(() => {
        if (!activeContentId) {
            return;
        }

        rescanMediaInfoMutation.mutate(activeContentId, {
            onSuccess: () => {
                toast({
                    title: 'Media info re-scan complete',
                    description: "Media info was re-probed for this item's files.",
                    variant: 'success',
                });
            },
            onError: () => {
                toast({
                    title: 'Media info re-scan failed',
                    description: 'Failed to start the media info re-scan. Please try again.',
                    variant: 'error',
                });
            },
        });
    }, [activeContentId, rescanMediaInfoMutation, toast]);

    const performGrabRelease = useCallback(
        (release: ReleaseRow) => {
            if (!activeContentId || !tryBeginGrab(release.id)) {
                return;
            }

            grabMutation.mutate(
                {
                    id: release.id,
                    contentHierarchyItemId: activeContentId,
                },
                {
                    onSuccess: () => {
                        markGrabSucceeded(release.id);
                        toast({
                            title: 'Release grabbed',
                            description: 'Release acquisition finished.',
                            variant: 'success',
                        });
                    },
                    onError: (error) => {
                        markGrabFailed(release.id);
                        toast({
                            title: 'Grab failed',
                            description: getErrorMessage(error, 'Failed to queue release acquisition.'),
                            variant: 'error',
                        });
                    },
                },
            );
        },
        [activeContentId, grabMutation, markGrabFailed, markGrabSucceeded, toast, tryBeginGrab],
    );

    const handleUnmatchChange = useCallback(
        (release: ReleaseRow, status: ReleaseScopeStatus) => {
            if (!activeContentId) {
                return;
            }

            setReleaseUnmatched(
                { contentId: activeContentId, releaseId: release.id, status },
                {
                    onError: (error) => {
                        toast({
                            title: status === 'Unmatched' ? 'Unmatch failed' : 'Undo unmatch failed',
                            description: getErrorMessage(error, 'Failed to change the release match.'),
                            variant: 'error',
                        });
                    },
                },
            );
        },
        [activeContentId, setReleaseUnmatched, toast],
    );
    const unmatchingReleaseIds = useMemo(
        () =>
            new Set(
                unmatchMutation.isPending && unmatchMutation.variables ? [unmatchMutation.variables.releaseId] : [],
            ),
        [unmatchMutation.isPending, unmatchMutation.variables],
    );

    const handleGrabRelease = useCallback(
        (release: ReleaseRow) => {
            if (release.isMatch === false) {
                setPendingGrab(release);
                return;
            }

            performGrabRelease(release);
        },
        [performGrabRelease],
    );

    const handleRequestDeleteFile = useCallback(
        (file: MediaFile) => {
            rememberActionFocus();
            setFilePendingDelete(file);
        },
        [rememberActionFocus],
    );

    const handleRequestFileReassignment = useCallback(
        (file: MediaFile) => {
            rememberActionFocus();
            setFilePendingReassignment(file);
        },
        [rememberActionFocus],
    );

    const handleRequestRename = useCallback(
        (selection: { filePath?: string; mediaFileIds?: string[] }) => {
            rememberActionFocus();
            setRenameSelection(selection);
        },
        [rememberActionFocus],
    );

    const handleOpenImportFile = useCallback(() => {
        if (attachFileMutation.isPending) {
            return;
        }
        rememberActionFocus();

        void fileBrowserLocationQuery.refetch().then((result) => {
            if (result.isError || !result.data) {
                toast({
                    title: 'File browser unavailable',
                    description: getErrorMessage(result.error),
                    variant: 'error',
                });
                return;
            }

            setImportDialogOpen(true);
        });
    }, [attachFileMutation.isPending, fileBrowserLocationQuery, rememberActionFocus, toast]);

    const handleImportFile = useCallback(
        (filePath: string) => {
            if (!activeContentId || attachFileMutation.isPending) {
                return;
            }

            setImportDialogOpen(false);
            setRenameSelection({ filePath });
        },
        [activeContentId, attachFileMutation],
    );

    const handleRequestUnlinkFile = useCallback(
        (file: MediaFile) => {
            rememberActionFocus();
            setFilePendingUnlink(file);
        },
        [rememberActionFocus],
    );

    const handleConfirmUnlinkFile = useCallback(() => {
        if (!activeContentId || !filePendingUnlink || unlinkFileMutation.isPending) {
            return;
        }

        const file = filePendingUnlink;
        unlinkFileMutation.mutate(
            { contentHierarchyItemId: activeContentId, mediaFileId: file.id },
            {
                onSuccess: () => {
                    toast({
                        title: 'File unlinked',
                        description: `Removed the Magpie association for "${file.fileName}". The host file remains.`,
                        variant: 'success',
                    });
                    setFilePendingUnlink(null);
                },
                onError: (error) => {
                    toast({ title: 'Unlink failed', description: getErrorMessage(error), variant: 'error' });
                },
            },
        );
    }, [activeContentId, filePendingUnlink, unlinkFileMutation, toast]);

    const handleConfirmDeleteFile = useCallback(() => {
        if (!activeContentId || !filePendingDelete) {
            return;
        }
        const file = filePendingDelete;
        deleteFileMutation.mutate(
            { contentHierarchyItemId: activeContentId, mediaFileId: file.id },
            {
                onSuccess: () => {
                    toast({
                        title: 'File deleted',
                        description: `Deleted "${file.fileName}".`,
                        variant: 'success',
                    });
                    setFilePendingDelete(null);
                },
                onError: () => {
                    toast({
                        title: 'Delete failed',
                        description: `Failed to delete "${file.fileName}". The file remains in place.`,
                        variant: 'error',
                    });
                },
            },
        );
    }, [activeContentId, filePendingDelete, deleteFileMutation, toast]);

    const handleOpenRemoveDialog = useCallback(() => {
        rememberActionFocus();
        setRemoveDeleteFiles(false);
        setRemoveDialogOpen(true);
    }, [rememberActionFocus]);

    const handleConfirmRemove = useCallback(() => {
        if (!activeContentId) {
            return;
        }
        removeContentMutation.mutate(
            { contentHierarchyItemId: activeContentId, deleteFiles: removeDeleteFiles },
            {
                onSuccess: () => {
                    toast({
                        title: 'Removed from library',
                        description: removeDeleteFiles
                            ? 'Magpie removed the item and its library files.'
                            : 'Magpie removed the item from the library. The files remain on disk.',
                        variant: 'success',
                    });
                    setRemoveDialogOpen(false);
                    void resolveContentOrigin(
                        contentOrigin,
                        activeContentId,
                        hasRole(Role.Manager),
                        hasRole(Role.Admin),
                    ).then((origin) =>
                        navigate(`${origin.pathname}${origin.search}${origin.hash}`, {
                            replace: true,
                            state: origin.state,
                        }),
                    );
                },
                onError: () => {
                    toast({
                        title: 'Remove failed',
                        description: removeDeleteFiles
                            ? 'Failed to remove this item because Magpie failed to delete a file. The library remains unchanged.'
                            : 'Failed to remove this item from the library. Try again.',
                        variant: 'error',
                    });
                },
            },
        );
    }, [activeContentId, removeContentMutation, removeDeleteFiles, toast, navigate, contentOrigin, hasRole]);

    const quickActions: QuickAction[] = useMemo(
        () => [
            ...(canEditContent
                ? [
                      {
                          id: 'refresh',
                          label: 'Refresh Metadata',
                          icon: <ArrowClockwiseIcon weight="bold" />,
                          onClick: handleRefresh,
                      },
                      {
                          id: 'rescan-media-info',
                          label: 'Rescan media info',
                          icon: <FileMagnifyingGlassIcon weight="bold" />,
                          onClick: handleRescanMediaInfo,
                      },
                  ]
                : []),
            ...(canRequestContent
                ? [
                      {
                          id: 'search',
                          label: 'Search Now',
                          icon: <MagnifyingGlassIcon weight="bold" />,
                          onClick: handleSearchNow,
                          variant: 'primary' as const,
                      },
                      ...(activeContentId
                          ? [
                                {
                                    id: 'interactive-search',
                                    label: 'Interactive Search',
                                    icon: <ListMagnifyingGlassIcon weight="bold" />,
                                    onClick: handleInteractiveSearch,
                                },
                            ]
                          : []),
                  ]
                : []),
            ...(canManageContent
                ? [
                      {
                          id: 'remove-from-library',
                          label: 'Remove from library',
                          icon: <TrashIcon weight="bold" />,
                          onClick: handleOpenRemoveDialog,
                          variant: 'destructive' as const,
                      },
                  ]
                : []),
        ],
        [
            canEditContent,
            handleRefresh,
            handleRescanMediaInfo,
            canRequestContent,
            handleSearchNow,
            activeContentId,
            handleInteractiveSearch,
            canManageContent,
            handleOpenRemoveDialog,
        ],
    );

    const extensionActions = useSlotExtensions('content.details.actions');

    const fabActions: QuickAction[] = useMemo(
        () =>
            effectiveTab === 'files' && canManageContent
                ? [
                      ...quickActions,
                      {
                          id: 'rename-all-files',
                          label: 'Apply rename mask to descendant files',
                          icon: <PencilSimpleLineIcon weight="bold" />,
                          onClick: () => handleRequestRename({}),
                      },
                      {
                          id: 'import-file',
                          label: 'Import file',
                          icon: <PlusIcon weight="bold" />,
                          onClick: handleOpenImportFile,
                          disabled: attachFileMutation.isPending,
                      },
                  ]
                : quickActions,
        [
            effectiveTab,
            canManageContent,
            quickActions,
            handleRequestRename,
            handleOpenImportFile,
            attachFileMutation.isPending,
        ],
    );

    const releasesLoading = releasesQuery.isLoading;
    const isRefreshing = refreshMutation.isPending;
    const isRescanningMediaInfo = rescanMediaInfoMutation.isPending;
    const isSearching = searchMutation.isPending;
    const isDeletingFile = deleteFileMutation.isPending;
    const isUnlinkingFile = unlinkFileMutation.isPending;
    const isRemoving = removeContentMutation.isPending;

    if (isContentLoading || metadataQuery.isLoading) {
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
                            <ArrowLeftIcon weight="bold" aria-hidden="true" />
                            Back to Library
                        </Link>
                    ) : (
                        <Button variant="secondary" onClick={() => refetchContent()}>
                            <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                            Retry
                        </Button>
                    )}
                </div>
            </div>
        );
    }

    const contentSlotEntityType = 'content';

    const headerActions = (
        <ContentDetailsActionsSlot entityId={activeContentId} entityType={contentSlotEntityType}>
            {canEditContent && (
                <Button
                    variant="secondary"
                    size="sm"
                    onClick={handleRefresh}
                    loading={isRefreshing}
                    data-action="refresh"
                >
                    <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                    Refresh
                </Button>
            )}
            {canEditContent && (
                <Button
                    variant="secondary"
                    size="sm"
                    onClick={handleRescanMediaInfo}
                    loading={isRescanningMediaInfo}
                    data-action="rescan-media-info"
                    title="Re-probe and refresh media info for this item's files"
                >
                    <FileMagnifyingGlassIcon weight="bold" aria-hidden="true" />
                    Rescan media info
                </Button>
            )}
            {canRequestContent && (
                <Button
                    variant="secondary"
                    size="sm"
                    onClick={handleSearchNow}
                    disabled={isSearching}
                    data-action="search"
                    title="Automatic: search indexers and grab the best release for you"
                >
                    <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                    Search Now
                </Button>
            )}
            {canRequestContent && activeContentId && (
                <Button variant="secondary" size="sm" asChild>
                    <Link
                        to={`/interactive-search/${activeContentId}`}
                        data-action="interactive-search"
                        title="Interactive: run a live search and pick a release yourself"
                    >
                        <ListMagnifyingGlassIcon weight="bold" aria-hidden="true" />
                        Interactive Search
                    </Link>
                </Button>
            )}
            {canManageContent && (
                <Button
                    variant="destructive"
                    size="sm"
                    onClick={handleOpenRemoveDialog}
                    data-action="remove-from-library"
                    title="Remove this item and everything nested under it from the library"
                >
                    <TrashIcon weight="bold" aria-hidden="true" />
                    Remove
                </Button>
            )}
        </ContentDetailsActionsSlot>
    );

    const detailSlotProps = {
        entityId: activeContentId,
        entityType: contentSlotEntityType,
    };

    return (
        <div className="content-detail-page" data-page="content-detail">
            <Breadcrumb items={breadcrumbs} className="content-detail-page__breadcrumb" />

            {metadataQuery.error && (
                <Alert
                    variant="error"
                    icon={<WarningCircleIcon weight="duotone" aria-hidden="true" />}
                    title="Failed to Load Content Metadata"
                    data-testid="content-metadata-error"
                >
                    <p>{getErrorMessage(metadataQuery.error)}</p>
                    <Button variant="secondary" size="sm" onClick={() => metadataQuery.refetch()}>
                        <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                        Retry
                    </Button>
                </Alert>
            )}

            <ContentDetailsHeaderSlot {...detailSlotProps}>
                <ContentDetailsHeader
                    content={content}
                    parent={parentRef}
                    heroPreview={heroPreview}
                    contentRatings={contentRatings}
                    awards={awards}
                    actions={headerActions}
                />
            </ContentDetailsHeaderSlot>

            <ContentDetailsMetadataSlot className="content-detail-page__metadata-slot" {...detailSlotProps} />

            <RefreshStatusBadge contentId={activeContentId} />
            <PendingFilesIndicator contentHierarchyItemId={activeContentId} onOpen={() => setActiveTab('files')} />

            {activeContentId && contentData && (
                <DetailInfoGrid
                    aria-label="Content supporting information"
                    className="detail-info-grid--under-header"
                    data-testid="content-policy-settings"
                >
                    <MonitoringControl
                        contentId={activeContentId}
                        monitoring={contentData.monitoring}
                        effectiveMonitoring={contentData.effectiveMonitoring}
                        canEdit={canEditContent}
                    />
                    <PathControl
                        contentHierarchyItemId={activeContentId}
                        rawPath={contentData.path}
                        canEdit={canEditContent}
                    />
                    <RootFolderControl
                        contentHierarchyItemId={activeContentId}
                        contentType={contentData.contentType}
                        canEdit={canEditContent}
                    />
                    <QualityTierControl
                        contentHierarchyItemId={activeContentId}
                        contentType={contentData.contentType}
                        level={contentData.level}
                        canEdit={canEditContent}
                    />
                    <TagsChipStrip contentId={activeContentId} canEdit={canEditContent} />
                    {content.publishDate && (
                        <DetailInfoRow
                            label="Publish Date"
                            value={formatPublishDate(content.publishDate, 'Unknown')}
                            data-testid="content-publish-date-row"
                        />
                    )}
                    <ContentViewOnLinks contentId={activeContentId} externalIds={externalIds} references={references} />
                    {genres.length > 0 && (
                        <DetailInfoRow
                            label="Genres"
                            data-component="content-genres"
                            value={
                                <CollapsibleChipList
                                    aria-label="Genres"
                                    items={genres.map((genre) => ({
                                        key: genre,
                                        node: (
                                            <Badge variant="default" size="sm">
                                                {genre}
                                            </Badge>
                                        ),
                                    }))}
                                />
                            }
                        />
                    )}
                </DetailInfoGrid>
            )}

            <Tabs
                value={effectiveTab}
                onValueChange={setActiveTab}
                holdHeightOnValueChange
                className="content-detail-page__tabs"
            >
                <DetailTabsList className="content-detail-page__tab-strip">
                    {tabDefs.map((tab, i) => (
                        <DetailTabTrigger
                            key={`${tab.value}:${i}`}
                            value={tab.value}
                            label={tab.label}
                            icon={tab.icon}
                            count={tab.count}
                        />
                    ))}
                </DetailTabsList>

                <TabsContent value="details">
                    <ContentDetailsOverviewSlot {...detailSlotProps}>
                        <ErrorBoundary>
                            <ContentFactsPanel facts={contentFacts} />
                        </ErrorBoundary>
                    </ContentDetailsOverviewSlot>
                </TabsContent>

                {hasRelationships && (
                    <TabsContent value="related">
                        <ErrorBoundary>
                            <RelatedSubjectsTab contentId={activeContentId} />
                        </ErrorBoundary>
                    </TabsContent>
                )}

                {hasPreviews && (
                    <TabsContent value="previews">
                        <ErrorBoundary>
                            <PreviewsTab previews={previews} posterUrl={content.posterUrl} />
                        </ErrorBoundary>
                    </TabsContent>
                )}

                {imageCount > 0 && (
                    <TabsContent value="images">
                        <ErrorBoundary>
                            <ImagesGallery images={images} altPrefix={content.title} />
                        </ErrorBoundary>
                    </TabsContent>
                )}

                {showChildrenTab && (
                    <TabsContent value="children">
                        {childrenResult && childrenResult.items.length < childrenResult.totalItems && (
                            <Alert variant="info" title="Child list limited">
                                This tab shows {childrenResult.items.length} of {childrenResult.totalItems} items.
                            </Alert>
                        )}
                        <ErrorBoundary>
                            <ChildrenTab
                                items={childrenData}
                                parentId={activeContentId!}
                                onChildClick={handleChildClick}
                                basePath={childrenBasePath}
                                dateColumnHeader={childrenDateHeader}
                                loading={isChildrenLoading}
                                emptyMessage={`No ${childrenTabLabel.toLowerCase()} found`}
                            />
                        </ErrorBoundary>
                    </TabsContent>
                )}

                <TabsContent value="releases">
                    <ErrorBoundary>
                        <ReleasesTab
                            releases={releasesData}
                            loading={releasesLoading}
                            grabbingReleaseIds={grabbingReleaseIds}
                            canGrab={canRequestContent}
                            onGrab={handleGrabRelease}
                            onUnmatchChange={hasRole(Role.Manager) ? handleUnmatchChange : undefined}
                            unmatchingReleaseIds={unmatchingReleaseIds}
                            onTitleClick={setDetailsRelease}
                        />
                    </ErrorBoundary>
                </TabsContent>

                <TabsContent ref={filesTabPanelRef} value="files">
                    <ErrorBoundary>
                        <ContentDetailsFilesSlot {...detailSlotProps}>
                            {activeContentId && (
                                <PendingFileAssociations
                                    contentHierarchyItemId={activeContentId}
                                    rootFolderId={contentData?.effectiveRootFolderId}
                                    title={content.title}
                                    canManage={canManageContent}
                                />
                            )}
                            <FilesTab
                                files={filesData}
                                emptyMessage="No files attached to this content"
                                onImportFile={canManageContent ? handleOpenImportFile : undefined}
                                onRenameFile={
                                    canManageContent
                                        ? (file) => handleRequestRename({ mediaFileIds: [file.id] })
                                        : undefined
                                }
                                onRenameFiles={
                                    canManageContent
                                        ? (mediaFileIds) => handleRequestRename({ mediaFileIds })
                                        : undefined
                                }
                                onRenameAllFiles={canManageContent ? () => handleRequestRename({}) : undefined}
                                importing={attachFileMutation.isPending}
                                onUnlinkFile={canManageContent ? handleRequestUnlinkFile : undefined}
                                onReassignFile={
                                    canManageContent && correctionShowId ? handleRequestFileReassignment : undefined
                                }
                                unlinkingFileId={isUnlinkingFile ? (filePendingUnlink?.id ?? null) : null}
                                onDeleteFile={canManageContent ? handleRequestDeleteFile : undefined}
                                deletingFileId={isDeletingFile ? (filePendingDelete?.id ?? null) : null}
                            />
                        </ContentDetailsFilesSlot>
                    </ErrorBoundary>
                </TabsContent>

                <TabsContent value="metadata">
                    <ErrorBoundary>
                        <MetadataInspector contentId={activeContentId} />
                    </ErrorBoundary>
                </TabsContent>

                <TabsContent value="activity">
                    <ErrorBoundary>
                        {activeContentId ? <ActivityTab contentHierarchyItemId={activeContentId} /> : null}
                    </ErrorBoundary>
                </TabsContent>

                <ExtensionTabContents
                    slot="content.details.tabs"
                    entityId={activeContentId}
                    entityType={contentSlotEntityType}
                />
            </Tabs>

            <QuickActionsButton
                actions={fabActions}
                title="Content Actions"
                triggerRef={contentActionsTriggerRef}
                sheetContent={
                    extensionActions.extensions.length > 0 ? (
                        <ContentDetailsActionsSlot entityId={activeContentId} entityType={contentSlotEntityType} />
                    ) : undefined
                }
                data-content-id={activeContentId}
            />

            <ReleaseDetailsDialog
                release={detailsRelease}
                download={detailsDownload}
                downloadLoading={detailsDownloadLoading}
                open={detailsRelease !== null}
                onOpenChange={(open) => {
                    if (!open) {
                        setDetailsRelease(null);
                    }
                }}
            />

            <DeleteConfirmDialog
                open={filePendingUnlink !== null}
                title="Unlink file"
                itemType="file"
                itemName={filePendingUnlink?.fileName ?? ''}
                description={
                    <>
                        Unlink &quot;
                        <span className="content-detail-page__file-confirm-name">{filePendingUnlink?.fileName}</span>
                        &quot; from Magpie? This removes only the association. The host file stays on disk.
                    </>
                }
                confirmVariant="warning"
                confirmLabel="Unlink"
                confirmBusyLabel="Unlinking..."
                confirmIcon={<LinkBreakIcon weight="bold" aria-hidden="true" />}
                returnFocusRef={actionReturnFocusRef}
                fallbackFocusRef={filesTabPanelRef}
                isDeleting={isUnlinkingFile}
                onConfirm={handleConfirmUnlinkFile}
                onCancel={() => {
                    if (!isUnlinkingFile) {
                        setFilePendingUnlink(null);
                    }
                }}
            />

            <DeleteConfirmDialog
                open={filePendingDelete !== null}
                itemType="file"
                itemName={filePendingDelete?.fileName ?? ''}
                description={
                    <>
                        Delete &quot;
                        <span className="content-detail-page__file-confirm-name">{filePendingDelete?.fileName}</span>
                        &quot; from disk? This removes the file from disk and the library. This cannot be undone.
                    </>
                }
                returnFocusRef={actionReturnFocusRef}
                fallbackFocusRef={filesTabPanelRef}
                isDeleting={isDeletingFile}
                onConfirm={handleConfirmDeleteFile}
                onCancel={() => {
                    if (!isDeletingFile) {
                        setFilePendingDelete(null);
                    }
                }}
            />

            {renameSelection && activeContentId && (
                <FileRenameDialog
                    key={`${activeContentId}:${renameSelection.filePath ?? renameSelection.mediaFileIds?.join(',') ?? 'all'}`}
                    contentId={activeContentId}
                    {...renameSelection}
                    onAssociate={async (filePath) => {
                        const result = await attachFileMutation.mutateAsync({
                            contentHierarchyItemId: activeContentId,
                            filePath,
                        });
                        toast({
                            title: 'File associated',
                            description: result.isPrimary
                                ? 'The file is attached as primary content.'
                                : 'The file is attached as a sidecar.',
                            variant: 'success',
                        });
                        return result;
                    }}
                    onClose={() => setRenameSelection(null)}
                    returnFocusRef={actionReturnFocusRef}
                    fallbackFocusRef={filesTabPanelRef}
                />
            )}
            {filePendingReassignment && activeContentId && correctionShowId && (
                <FileReassignDialog
                    contentId={activeContentId}
                    showId={correctionShowId}
                    file={filePendingReassignment}
                    onClose={() => setFilePendingReassignment(null)}
                    returnFocusRef={actionReturnFocusRef}
                    fallbackFocusRef={filesTabPanelRef}
                />
            )}

            {fileBrowserLocationQuery.data && (
                <FileBrowser
                    open={importDialogOpen}
                    onOpenChange={setImportDialogOpen}
                    onSelect={handleImportFile}
                    mode="file"
                    initialPath={fileBrowserLocationQuery.data.initialPath}
                    rootPath={fileBrowserLocationQuery.data.rootPath}
                    endpoint={buildUrl('/system/files', { scope: activeContentId })}
                    title="Import file"
                    description="Select an existing host file to attach to this content."
                    returnFocusRef={actionReturnFocusRef}
                    fallbackFocusRef={contentActionsTriggerRef}
                />
            )}

            <DeleteConfirmDialog
                open={removeDialogOpen}
                itemType="item"
                itemName={content.title}
                description={
                    <>
                        Remove &quot;{content.title}&quot; from the library? Everything nested under it will be removed
                        too. Download history is kept. By default the media files stay on disk; tick the box below to
                        also delete them. This cannot be undone.
                    </>
                }
                extraToggle={{
                    label: 'Also delete files from disk',
                    checked: removeDeleteFiles,
                    onChange: setRemoveDeleteFiles,
                }}
                returnFocusRef={actionReturnFocusRef}
                fallbackFocusRef={contentActionsTriggerRef}
                isDeleting={isRemoving}
                onConfirm={handleConfirmRemove}
                onCancel={() => {
                    if (!isRemoving) {
                        setRemoveDialogOpen(false);
                    }
                }}
            />

            <DeleteConfirmDialog
                open={pendingGrab !== null}
                title="Grab anyway?"
                itemType="release"
                itemName={pendingGrab?.title ?? ''}
                description={
                    pendingGrab
                        ? `This release does not match this item: ${pendingGrab.rejectionReason?.trim() || 'no reason was reported'}. Grab anyway?`
                        : ''
                }
                confirmVariant="warning"
                confirmLabel="Grab anyway"
                confirmIcon={<DownloadSimpleIcon weight="bold" aria-hidden="true" />}
                onConfirm={() => {
                    if (pendingGrab) {
                        performGrabRelease(pendingGrab);
                    }
                    setPendingGrab(null);
                }}
                onCancel={() => setPendingGrab(null)}
            />
        </div>
    );
}

ContentDetailPage.displayName = 'ContentDetailPage';

export default ContentDetailPage;
