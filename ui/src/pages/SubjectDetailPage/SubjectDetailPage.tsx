import { useCallback, useEffect, useMemo, type CSSProperties, type ReactNode } from 'react';
import { useParams, Link, useLocation, useSearchParams } from 'react-router-dom';
import {
    ArrowLeftIcon,
    ArrowClockwiseIcon,
    BinocularsIcon,
    CircleNotchIcon,
    FilmSlateIcon,
    FunnelIcon,
    ImageBrokenIcon,
    InfoIcon,
    ImagesIcon,
    UsersIcon,
    FileTextIcon,
    PuzzlePieceIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { DetailsPanel, type DetailRow } from '@/components/ui/DetailsPanel';
import { AwardsPanel } from '@/components/ui/AwardsPanel';
import { DetailInfoGrid, DetailInfoRow } from '@/components/ui/DetailInfoGrid';
import { CollapsibleChipList } from '@/components/ui/CollapsibleChipList';
import { ExternalIdDetails, ExternalIds, type ExternalId } from '@/components/ui/ExternalIds';
import { ImagesGallery, type GalleryImage } from '@/components/ui/ImagesGallery';
import { MetadataLinks } from '@/components/ui/MetadataLinks';
import { MetadataInspector } from '@/components/metadata-inspector/MetadataInspector';
import { PreviewsTab } from '@/components/content/ContentTabs';
import type { PreviewClip } from '@/components/ui/PreviewPlayer';
import { SubjectDetailsHeaderSlot } from '@/components/ui/Slot/SubjectSlots';
import { ExtensionTabContents, useTabSlotExtensions } from '@/components/ui/Slot/ExtensionTabs';
import { Tabs, TabsContent, DetailTabTrigger, DetailTabsList } from '@/components/ui/Tabs';
import { useToast } from '@/components/ui/Toast';
import { useAuth } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { getStandardMetadataField, useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useSubjectMetadataInspector } from '@/hooks/useSubjectMetadataInspector';
import { resolveSubjectKindLabel, resolveSubjectKindPluralLabel } from '@/types/subjects';
import { getErrorMessage, isNotFoundError } from '@/lib/errors';
import { escapeQueryString } from '@/lib/simpleFilterQuery';
import { extractTitle, isInternalSource, type ContentReference } from '@/lib/metadata';
import { extractExternalIds, useSubject, useRefreshSubject, useSetPrimaryImage } from '@/hooks/useSubjects';
import {
    buildAwardGroups,
    type AwardGroup,
    type AlternateTextRow,
    buildAlternateTextRows,
    buildMaterializedDetailRows,
    nonEmptyMetadataText,
    sortByOrdinal,
} from '@/lib/materializedMetadataDisplay';
import { safeHttpUrl } from '@/lib/safeUrl';
import type { LibraryAddPreloadState } from '@/types/navigation';

import { SubjectContentTab } from './SubjectContentTab';
import { useSubjectContent } from './useSubjectContent';
import { SubjectTagsChipStrip } from './SubjectTagsChipStrip';
import './SubjectDetailPage.css';
import '../DetailPageStates.css';

interface SubjectImage extends GalleryImage {
    imageId?: string;
}

type SubjectExternalId = ExternalId;

interface SubjectRelationship {
    id: string;
    kind: string;
    qualifier?: string;
    subjectId?: string;
    name?: string;
    imageUrl?: string;
}

interface SubjectData {
    id: string;
    title: string;
    kind: string;
    imageUrl?: string;
    description?: string;
    genres?: string[];
    awards?: AwardGroup[];
    externalIds?: SubjectExternalId[];
    references?: ContentReference[];
    images?: SubjectImage[];
    previews?: PreviewClip[];
    relationships?: SubjectRelationship[];
    detailRows?: DetailRow[];
    alternateTexts?: AlternateTextRow[];
}

interface SubjectDetailLocationState {
    fromContent?: {
        id: string;
        title: string;
    };
}

const DEFAULT_TAB = 'related';

interface TabDef {
    value: string;
    label: string;
    icon?: ReactNode;
    count?: number;
}

export function SubjectDetailPage() {
    const { subjectId } = useParams<{ subjectId: string }>();
    const location = useLocation();
    const [searchParams, setSearchParams] = useSearchParams();
    const rawTab = searchParams.get('tab');
    const activeTab = rawTab || DEFAULT_TAB;
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === DEFAULT_TAB) {
                        next.delete('tab');
                    } else {
                        next.set('tab', tab);
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const locationState = location.state as SubjectDetailLocationState | null;
    const fromContent = locationState?.fromContent;

    const {
        subject: apiSubject,
        isLoading,
        error: subjectError,
        refetch: refetchSubject,
    } = useSubject(subjectId ?? '');
    const subjectMetadataQuery = useSubjectMetadataInspector(subjectId);
    const subjectContent = useSubjectContent(apiSubject?.metadataContainerId);
    const { subjectTypes, isLoading: isLibraryTypesLoading, error: libraryTypesError } = useLibraryTypes();

    const { tabs: extensionTabs, isReady: areExtensionTabsReady } = useTabSlotExtensions('subject.details.tabs');
    const { toast } = useToast();
    const { hasPermission } = useAuth();
    const canViewLibrary = hasPermission('content.view');
    const canAddContent = hasPermission('content.add');
    const canEditSubject = hasPermission('subjects.edit');
    const refreshMutation = useRefreshSubject();
    const setPrimaryMutation = useSetPrimaryImage(subjectId ?? '');

    const kind = apiSubject?.kind;
    const kindType = useMemo(
        () => (kind ? subjectTypes.find((type) => type.slug === kind) : undefined),
        [kind, subjectTypes],
    );
    const kindLabel = resolveSubjectKindLabel(kind, kindType?.name);
    const kindPluralLabel = resolveSubjectKindPluralLabel(kind, kindType?.pluralName, kindType?.name);
    const subjectListPath = kind ? `/library/subjects/${kind}` : '/library/subjects/all';
    const subjectListLabel = kind ? kindPluralLabel : 'Subjects';

    useDocumentTitle(kind ? kindLabel : 'Subject');

    const handleRefresh = useCallback(() => {
        if (!subjectId) {
            return;
        }

        refreshMutation.mutate(subjectId, {
            onSuccess: () => {
                toast({
                    title: 'Refresh started',
                    description: `${kindLabel} metadata refresh queued.`,
                    variant: 'success',
                });
            },
            onError: () => {
                toast({
                    title: 'Refresh failed',
                    description: `Failed to start ${kindLabel.toLowerCase()} metadata refresh. Please try again.`,
                    variant: 'error',
                });
            },
        });
    }, [subjectId, refreshMutation, toast, kindLabel]);

    const handleSetPrimary = useCallback(
        (image: GalleryImage) => {
            const imageId = (image as SubjectImage).imageId;
            if (!imageId) {
                return;
            }
            setPrimaryMutation.mutate(imageId, {
                onSuccess: () => {
                    toast({
                        title: 'Primary image updated',
                        description: 'This image is now the primary image.',
                        variant: 'success',
                    });
                },
                onError: () => {
                    toast({
                        title: 'Update failed',
                        description: 'Failed to set the primary image. Please try again.',
                        variant: 'error',
                    });
                },
            });
        },
        [setPrimaryMutation, toast],
    );

    const subject: SubjectData | null = useMemo(() => {
        if (!apiSubject) return null;

        const container = apiSubject.metadataContainer;
        const metadata = subjectMetadataQuery.data?.materialized;
        const supportsGenres = !!getStandardMetadataField(kindType, 'Genres');
        const supportsPreviews = !!getStandardMetadataField(kindType, 'Previews');
        const images: SubjectImage[] = sortByOrdinal(metadata?.images).flatMap((image) => {
            const url = nonEmptyMetadataText(image.url);
            const type = nonEmptyMetadataText(image.type);
            return url && type
                ? [
                      {
                          id: image.id,
                          imageId: image.id,
                          url,
                          type,
                          isPrimary: image.isPrimary,
                          width: image.width ?? undefined,
                          height: image.height ?? undefined,
                      },
                  ]
                : [];
        });
        const externalIdLinks = extractExternalIds(container);

        return {
            id: apiSubject.id,
            title: nonEmptyMetadataText(metadata?.title) ?? extractTitle(container) ?? 'Unknown Subject',
            kind: apiSubject.kind,
            imageUrl: images.find((image) => image.isPrimary)?.url ?? images[0]?.url,
            description: nonEmptyMetadataText(metadata?.overview),
            genres: supportsGenres
                ? sortByOrdinal(metadata?.genres).flatMap((genre) => nonEmptyMetadataText(genre.name) ?? [])
                : [],
            awards: buildAwardGroups(metadata),
            externalIds: sortByOrdinal(metadata?.externalIds).flatMap((externalId) => {
                const id = nonEmptyMetadataText(externalId.value);
                const source = nonEmptyMetadataText(externalId.source) ?? 'unknown';
                return id && !isInternalSource(source)
                    ? [
                          {
                              source,
                              id,
                              url: externalIdLinks.find(
                                  (candidate) => candidate.source === source && candidate.id === id,
                              )?.url,
                          },
                      ]
                    : [];
            }),
            references: sortByOrdinal(metadata?.references).flatMap((reference) => {
                const url = nonEmptyMetadataText(reference.url);
                return url
                    ? [
                          {
                              url,
                              displayName: nonEmptyMetadataText(reference.displayName) ?? url,
                              source: reference.source,
                              kind: reference.kind,
                          },
                      ]
                    : [];
            }),
            images,
            previews: supportsPreviews
                ? sortByOrdinal(metadata?.previews).flatMap((preview) => {
                      const url = nonEmptyMetadataText(preview.url);
                      const previewKind = nonEmptyMetadataText(preview.kind);
                      return url && previewKind
                          ? [
                                {
                                    url,
                                    kind: previewKind,
                                    title: nonEmptyMetadataText(preview.title),
                                    mimeType: nonEmptyMetadataText(preview.mimeType),
                                    embedKind: nonEmptyMetadataText(preview.embedKind),
                                    language: nonEmptyMetadataText(preview.language),
                                },
                            ]
                          : [];
                  })
                : [],
            relationships: sortByOrdinal(metadata?.relationships).map((relationship) => {
                const related = relationship.relatedMetadataContainerId
                    ? subjectMetadataQuery.data?.subjects[relationship.relatedMetadataContainerId]
                    : undefined;
                return {
                    id: relationship.id,
                    kind: relationship.kind,
                    qualifier: nonEmptyMetadataText(relationship.qualifier),
                    subjectId: related?.subjectId,
                    name: nonEmptyMetadataText(related?.name),
                    imageUrl: nonEmptyMetadataText(related?.imageUrl),
                };
            }),
            alternateTexts: buildAlternateTextRows(metadata),
            detailRows: buildMaterializedDetailRows(
                metadata,
                kindType?.standardMetadataFields,
                kindType?.customMetadataFields,
            ),
        };
    }, [apiSubject, kindType, subjectMetadataQuery.data]);

    const subjectFilterQuery = subject
        ? `Relationships has (Name = "${escapeQueryString(subject.title)}"` +
          ` and Type = "${escapeQueryString(subject.kind)}")`
        : '';
    const librarySearch = new URLSearchParams({ filter: subjectFilterQuery }).toString();
    const addContentState: LibraryAddPreloadState = { filterQuery: subjectFilterQuery };

    const breadcrumbs: BreadcrumbItem[] = useMemo(() => {
        const items: BreadcrumbItem[] = [
            { id: 'library', label: 'Library', to: '/library' },
            {
                id: 'subjects',
                label: kind ? kindPluralLabel : 'Subjects',
                to: kind ? `/library/subjects/${kind}` : '/library/subjects/all',
            },
        ];

        if (subject) {
            items.push({
                id: subject.id,
                label: subject.title,
                to: `/subjects/${subject.id}`,
            });
        }

        return items;
    }, [subject, kind, kindPluralLabel]);

    const imageCount = subject?.images?.length || 0;
    const previewCount = subject?.previews?.length || 0;
    const relationshipCount = subject?.relationships?.length || 0;
    const relatedCount = subjectContent.totalItems;

    const tabDefs: TabDef[] = useMemo(() => {
        const defs: TabDef[] = [
            {
                value: 'related',
                label: 'Content',
                icon: <FilmSlateIcon weight="bold" aria-hidden="true" />,
                count: relatedCount > 0 ? relatedCount : undefined,
            },
            {
                value: 'details',
                label: 'Details',
                icon: <InfoIcon weight="bold" aria-hidden="true" />,
            },
        ];
        if (imageCount > 0) {
            defs.push({
                value: 'images',
                label: 'Images',
                icon: <ImagesIcon weight="bold" aria-hidden="true" />,
                count: imageCount,
            });
        }
        if (previewCount > 0) {
            defs.push({
                value: 'previews',
                label: 'Previews',
                icon: <FilmSlateIcon weight="bold" aria-hidden="true" />,
                count: previewCount,
            });
        }
        if (relationshipCount > 0) {
            defs.push({
                value: 'relationships',
                label: 'Relationships',
                icon: <UsersIcon weight="bold" aria-hidden="true" />,
                count: relationshipCount,
            });
        }
        defs.push({ value: 'metadata', label: 'Metadata', icon: <FileTextIcon weight="bold" aria-hidden="true" /> });
        for (const ext of extensionTabs) {
            defs.push({
                value: `ext-${ext.id}`,
                label: ext.attributes?.['tab-label'] ?? ext.name,
                icon: <PuzzlePieceIcon weight="bold" aria-hidden="true" />,
            });
        }
        return defs;
    }, [relatedCount, imageCount, previewCount, relationshipCount, extensionTabs]);

    const effectiveTab = tabDefs.some((t) => t.value === activeTab) ? activeTab : DEFAULT_TAB;

    useEffect(() => {
        if (
            !apiSubject ||
            subjectError ||
            isLoading ||
            subjectMetadataQuery.error ||
            subjectMetadataQuery.isLoading ||
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
        apiSubject,
        areExtensionTabsReady,
        effectiveTab,
        isLibraryTypesLoading,
        isLoading,
        libraryTypesError,
        rawTab,
        setActiveTab,
        subjectMetadataQuery.error,
        subjectMetadataQuery.isLoading,
        subjectError,
    ]);

    if (isLoading || subjectMetadataQuery.isLoading || isLibraryTypesLoading) {
        return (
            <div className="subject-detail-page" data-page="subject-detail">
                <div className="subject-detail-page__not-found">
                    <CircleNotchIcon weight="bold" className="subject-detail-page__spinner" aria-hidden="true" />
                    <p>Loading...</p>
                </div>
            </div>
        );
    }

    if (!subject) {
        const notFound = !subjectError || isNotFoundError(subjectError);

        if (!notFound) {
            return (
                <div className="subject-detail-page" data-page="subject-detail">
                    <div className="subject-detail-page__not-found">
                        <WarningCircleIcon weight="duotone" aria-hidden="true" />
                        <h1>Failed to Load Subject</h1>
                        <p>{getErrorMessage(subjectError)}</p>
                        <Button variant="secondary" onClick={() => refetchSubject()}>
                            <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                            Retry
                        </Button>
                        <Link to={subjectListPath} className="subject-detail-page__back-link">
                            <ArrowLeftIcon weight="bold" aria-hidden="true" />
                            Back to {subjectListLabel}
                        </Link>
                    </div>
                </div>
            );
        }

        return (
            <div className="subject-detail-page" data-page="subject-detail">
                <div className="subject-detail-page__not-found">
                    <h1>Subject Not Found</h1>
                    <p>The subject you are looking for does not exist.</p>
                    <Link to={subjectListPath} className="subject-detail-page__back-link">
                        <ArrowLeftIcon weight="bold" aria-hidden="true" />
                        Back to {subjectListLabel}
                    </Link>
                </div>
            </div>
        );
    }

    const subjectSlotEntityType = 'subject';
    const detailSlotProps = {
        entityId: subject.id,
        entityType: subjectSlotEntityType,
    };

    return (
        <div className="subject-detail-page" data-page="subject-detail">
            <Breadcrumb items={breadcrumbs} className="subject-detail-page__breadcrumb" />

            {subjectMetadataQuery.error && (
                <Alert
                    variant="error"
                    icon={<WarningCircleIcon weight="duotone" aria-hidden="true" />}
                    title="Failed to Load Subject Metadata"
                    data-testid="subject-metadata-error"
                >
                    <p>{getErrorMessage(subjectMetadataQuery.error)}</p>
                    <Button variant="secondary" size="sm" onClick={() => subjectMetadataQuery.refetch()}>
                        <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                        Retry
                    </Button>
                </Alert>
            )}

            {fromContent && (
                <Link to={`/library/${fromContent.id}`} className="subject-detail-page__content-back-link">
                    <ArrowLeftIcon weight="bold" aria-hidden="true" />
                    Back to {fromContent.title}
                </Link>
            )}

            <SubjectDetailsHeaderSlot {...detailSlotProps}>
                <header
                    className="subject-detail-page__header"
                    data-component="subject-detail-header"
                    data-subject-id={subject.id}
                    data-testid="subject-detail-page__header"
                >
                    <div className="subject-detail-page__image-container">
                        {subject.imageUrl ? (
                            <div
                                className="subject-detail-page__image subject-detail-page__image--media"
                                style={{ '--hero-image': `url(${JSON.stringify(subject.imageUrl)})` } as CSSProperties}
                            >
                                <img
                                    src={subject.imageUrl}
                                    alt={`${subject.title} image`}
                                    className="subject-detail-page__image-img"
                                    loading="eager"
                                />
                            </div>
                        ) : (
                            <div className="subject-detail-page__image subject-detail-page__image--placeholder">
                                <ImageBrokenIcon weight="thin" aria-hidden="true" />
                            </div>
                        )}
                    </div>

                    <div className="subject-detail-page__info">
                        <div className="subject-detail-page__title-row">
                            <h1 className="subject-detail-page__title">{subject.title}</h1>
                            <Badge variant="default" size="md">
                                {kindLabel}
                            </Badge>
                        </div>

                        {subject.description && <p className="subject-detail-page__bio">{subject.description}</p>}

                        <AwardsPanel groups={subject.awards ?? []} data-component="subject-awards-panel" />

                        {(canViewLibrary || canAddContent || canEditSubject) && (
                            <div className="subject-detail-page__actions">
                                {canViewLibrary && (
                                    <Button asChild variant="secondary" size="sm">
                                        <Link to={`/library/all?${librarySearch}`} data-action="search-library">
                                            <FunnelIcon weight="bold" aria-hidden="true" />
                                            Search library
                                        </Link>
                                    </Button>
                                )}
                                {canAddContent && (
                                    <Button asChild variant="secondary" size="sm">
                                        <Link to="/library/add" state={addContentState} data-action="add-content">
                                            <BinocularsIcon weight="bold" aria-hidden="true" />
                                            Add content
                                        </Link>
                                    </Button>
                                )}
                                {canEditSubject && (
                                    <Button
                                        variant="secondary"
                                        size="sm"
                                        onClick={handleRefresh}
                                        loading={refreshMutation.isPending}
                                        data-action="refresh"
                                    >
                                        <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                                        Refresh
                                    </Button>
                                )}
                            </div>
                        )}
                    </div>
                </header>
            </SubjectDetailsHeaderSlot>

            <DetailInfoGrid aria-label="Subject supporting information" className="detail-info-grid--under-header">
                <SubjectTagsChipStrip
                    subjectId={subject.id}
                    subjectListPath={subjectListPath}
                    canEdit={canEditSubject}
                />
                {((subject.externalIds ?? []).some((extId) => safeHttpUrl(extId.url) !== null) ||
                    (subject.references?.length ?? 0) > 0) && (
                    <DetailInfoRow
                        label="Links"
                        data-testid="subject-links-row"
                        value={
                            <div className="detail-info-links">
                                <ExternalIds
                                    variant="chips"
                                    data-component="view-on-links"
                                    ids={(subject.externalIds ?? []).map((extId) => ({
                                        source: extId.source,
                                        id: extId.id,
                                        url: extId.url,
                                    }))}
                                    itemTestId={(extId) => `external-source-link-${extId.source.toLowerCase()}`}
                                />
                                <MetadataLinks references={subject.references ?? []} label={null} />
                            </div>
                        }
                    />
                )}
                {subject.genres && subject.genres.length > 0 && (
                    <DetailInfoRow
                        label="Genres"
                        data-component="subject-genres"
                        value={
                            <CollapsibleChipList
                                aria-label="Genres"
                                items={subject.genres.map((genre) => ({
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

            <Tabs value={effectiveTab} onValueChange={setActiveTab} className="subject-detail-page__tabs">
                <DetailTabsList className="subject-detail-page__tab-strip">
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
                    <div className="subject-detail-page__facts details-panel-group" data-component="subject-facts">
                        <DetailsPanel
                            rows={subject.detailRows ?? []}
                            variant="facts"
                            aria-label="Details"
                            data-component="subject-details-panel"
                        />

                        {subject.alternateTexts && subject.alternateTexts.length > 0 && (
                            <DetailsPanel
                                title="Alternate texts"
                                data-component="subject-alternate-texts"
                                rows={subject.alternateTexts.map((alternate) => ({
                                    key: alternate.id,
                                    label: (
                                        <>
                                            {alternate.kind}
                                            {alternate.language && (
                                                <span className="details-panel__code">{alternate.language}</span>
                                            )}
                                        </>
                                    ),
                                    value: alternate.text,
                                }))}
                            />
                        )}

                        {subject.externalIds && subject.externalIds.length > 0 && (
                            <ExternalIdDetails ids={subject.externalIds} />
                        )}
                    </div>
                </TabsContent>

                <TabsContent value="related">
                    <SubjectContentTab {...subjectContent} />
                </TabsContent>

                {imageCount > 0 && (
                    <TabsContent value="images">
                        <ImagesGallery
                            images={subject.images ?? []}
                            altPrefix={subject.title}
                            onSetPrimary={canEditSubject ? handleSetPrimary : undefined}
                            settingPrimary={canEditSubject ? setPrimaryMutation.isPending : false}
                        />
                    </TabsContent>
                )}

                {previewCount > 0 && (
                    <TabsContent value="previews">
                        <PreviewsTab previews={subject.previews ?? []} posterUrl={subject.imageUrl} />
                    </TabsContent>
                )}

                {relationshipCount > 0 && (
                    <TabsContent value="relationships">
                        <div className="subject-detail-page__relationships">
                            <section className="subject-detail-page__section">
                                <div className="subject-detail-page__people-grid">
                                    {subject.relationships?.map((relationship) => (
                                        <div
                                            key={relationship.id}
                                            className="subject-detail-page__person-card"
                                            data-relationship-id={relationship.id}
                                        >
                                            <div className="subject-detail-page__person-avatar">
                                                {relationship.imageUrl ? (
                                                    <img
                                                        className="subject-detail-page__person-image"
                                                        src={relationship.imageUrl}
                                                        alt=""
                                                    />
                                                ) : (
                                                    <div
                                                        className="subject-detail-page__person-image subject-detail-page__person-image--placeholder"
                                                        data-testid="subject-detail-page__person-image--placeholder"
                                                    >
                                                        <UsersIcon weight="thin" aria-hidden="true" />
                                                    </div>
                                                )}
                                            </div>
                                            <div className="subject-detail-page__person-info">
                                                {relationship.name &&
                                                    (relationship.subjectId ? (
                                                        <Link
                                                            className="subject-detail-page__person-name"
                                                            to={`/subjects/${relationship.subjectId}`}
                                                        >
                                                            {relationship.name}
                                                        </Link>
                                                    ) : (
                                                        <span className="subject-detail-page__person-name">
                                                            {relationship.name}
                                                        </span>
                                                    ))}
                                                <span className="subject-detail-page__person-role">
                                                    {relationship.kind}
                                                </span>
                                                {relationship.qualifier && (
                                                    <span className="subject-detail-page__person-character">
                                                        {relationship.qualifier}
                                                    </span>
                                                )}
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </section>
                        </div>
                    </TabsContent>
                )}

                <TabsContent value="metadata">
                    <MetadataInspector owner={{ kind: 'subject', id: subject.id }} />
                </TabsContent>

                <ExtensionTabContents
                    slot="subject.details.tabs"
                    entityId={subject.id}
                    entityType={subjectSlotEntityType}
                />
            </Tabs>
        </div>
    );
}

SubjectDetailPage.displayName = 'SubjectDetailPage';

export default SubjectDetailPage;
