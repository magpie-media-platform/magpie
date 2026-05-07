import { useCallback, useMemo } from 'react';
import { useParams, Link, useLocation, useSearchParams } from 'react-router-dom';
import { ArrowLeft, CircleNotch, ImageBroken, Info, Images, Users, LinkSimple } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { ExternalIds, type ExternalId } from '@/components/ui/ExternalIds';
import { ImagesGallery } from '@/components/ui/ImagesGallery';
import {
    RelatedContentList,
    type RelatedContentItem as RelatedContentItemType,
} from '@/components/ui/RelatedContentList';
import { SubjectDetailsHeaderSlot, SubjectDetailsTabsSlot } from '@/components/ui/Slot/SubjectSlots';
import { ExtensionTabTriggers, ExtensionTabContents } from '@/components/ui/Slot/ExtensionTabs';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { extractImageUrl, extractTitle, extractOverview, extractYear } from '@/lib/metadata';
import {
    useSubject,
    extractLanguage,
    extractStatus,
    extractGenres,
    extractContentRating,
    extractExternalIds,
    extractAwards,
    extractImages,
    extractRelationships,
} from '@/hooks/useSubjects';

import './SubjectDetailPage.css';

export interface SubjectImage {
    id: string;
    url: string;
    type: 'poster' | 'backdrop' | 'banner' | 'screenshot' | 'logo';
    width?: number;
    height?: number;
}

export type SubjectExternalId = ExternalId;

export interface SubjectRelationship {
    id: string;
    kind: string;
    qualifier?: string;
}

export interface SubjectData {
    id: string;
    title: string;
    kind: string;
    imageUrl?: string;
    description?: string;
    genres?: string[];
    contentRating?: string;
    year?: number;
    status?: string;
    language?: string;
    awards?: string[];
    externalIds?: SubjectExternalId[];
    images?: SubjectImage[];
    relationships?: SubjectRelationship[];
    relatedContent?: RelatedContentItemType[];
}

interface SubjectDetailLocationState {
    fromContent?: {
        id: string;
        title: string;
    };
}

export function SubjectDetailPage() {
    useDocumentTitle('Subject');
    const { subjectId } = useParams<{ subjectId: string }>();
    const location = useLocation();
    const [searchParams, setSearchParams] = useSearchParams();
    const activeTab = searchParams.get('tab') || 'overview';
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === 'overview') {
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

    const { subject: apiSubject, isLoading } = useSubject(subjectId ?? '');

    const subject: SubjectData | null = useMemo(() => {
        if (!apiSubject) return null;

        const container = apiSubject.metadataContainer;

        return {
            id: apiSubject.id,
            title: extractTitle(container) ?? 'Unknown Subject',
            kind: apiSubject.kind,
            imageUrl: extractImageUrl(container),
            description: extractOverview(container),
            genres: extractGenres(container),
            contentRating: extractContentRating(container),
            year: extractYear(container),
            status: extractStatus(container),
            language: extractLanguage(container),
            awards: extractAwards(container),
            externalIds: extractExternalIds(container),
            images: extractImages(container) as SubjectImage[],
            relationships: extractRelationships(container),

            relatedContent: [],
        };
    }, [apiSubject]);

    const breadcrumbs: BreadcrumbItem[] = useMemo(() => {
        const items: BreadcrumbItem[] = [{ id: 'subjects', label: 'Subjects', to: '/library/subjects/all' }];

        if (subject) {
            items.push({
                id: subject.id,
                label: subject.title,
                to: `/subjects/${subject.id}`,
            });
        }

        return items;
    }, [subject]);

    const imageCount = subject?.images?.length || 0;
    const relationshipCount = subject?.relationships?.length || 0;

    if (isLoading) {
        return (
            <div className="subject-detail-page" data-page="subject-detail">
                <div className="subject-detail-page__not-found">
                    <CircleNotch weight="bold" className="subject-detail-page__spinner" aria-hidden="true" />
                    <p>Loading subject...</p>
                </div>
            </div>
        );
    }

    if (!subject) {
        return (
            <div className="subject-detail-page" data-page="subject-detail">
                <div className="subject-detail-page__not-found">
                    <h1>Subject Not Found</h1>
                    <p>The subject you are looking for does not exist.</p>
                    <Link to="/library/subjects/all" className="subject-detail-page__back-link">
                        <ArrowLeft weight="bold" aria-hidden="true" />
                        Back to Subjects
                    </Link>
                </div>
            </div>
        );
    }

    return (
        <div className="subject-detail-page" data-page="subject-detail">
            <Breadcrumb items={breadcrumbs} className="subject-detail-page__breadcrumb" />

            {fromContent && (
                <Link to={`/library/${fromContent.id}`} className="subject-detail-page__content-back-link">
                    <ArrowLeft weight="bold" aria-hidden="true" />
                    Back to {fromContent.title}
                </Link>
            )}

            <SubjectDetailsHeaderSlot>
                <header
                    className="subject-detail-page__header"
                    data-component="subject-detail-header"
                    data-subject-id={subject.id}
                    data-testid="subject-detail-page__header"
                >
                    <div className="subject-detail-page__image-container">
                        {subject.imageUrl ? (
                            <img
                                src={subject.imageUrl}
                                alt={`${subject.title} image`}
                                className="subject-detail-page__image"
                                loading="eager"
                            />
                        ) : (
                            <div className="subject-detail-page__image subject-detail-page__image--placeholder">
                                <ImageBroken weight="thin" aria-hidden="true" />
                            </div>
                        )}
                    </div>

                    <div className="subject-detail-page__info">
                        <div className="subject-detail-page__title-row">
                            <h1 className="subject-detail-page__title">{subject.title}</h1>
                            <Badge variant="default" size="md">
                                {subject.kind}
                            </Badge>
                            {subject.year && <span className="subject-detail-page__year">({subject.year})</span>}
                        </div>

                        <div className="subject-detail-page__meta-row">
                            {subject.contentRating && (
                                <Badge variant="default" size="sm">
                                    {subject.contentRating}
                                </Badge>
                            )}
                            {subject.status && (
                                <Badge variant={subject.status === 'Ended' ? 'default' : 'healthy'} size="sm">
                                    {subject.status}
                                </Badge>
                            )}
                            {subject.language && (
                                <span className="subject-detail-page__meta-label">{subject.language}</span>
                            )}
                        </div>

                        {subject.genres && subject.genres.length > 0 && (
                            <div className="subject-detail-page__genres">
                                {subject.genres.map((genre) => (
                                    <Badge key={genre} variant="default" size="sm">
                                        {genre}
                                    </Badge>
                                ))}
                            </div>
                        )}

                        {subject.description && (
                            <p className="subject-detail-page__description">{subject.description}</p>
                        )}
                    </div>
                </header>
            </SubjectDetailsHeaderSlot>

            <SubjectDetailsTabsSlot>
                <Tabs value={activeTab} onValueChange={setActiveTab} className="subject-detail-page__tabs">
                    <TabsList>
                        <TabsTrigger value="overview">
                            <Info weight="bold" aria-hidden="true" />
                            <span>Overview</span>
                        </TabsTrigger>
                        {imageCount > 0 && (
                            <TabsTrigger value="images">
                                <Images weight="bold" aria-hidden="true" />
                                <span>Images</span>
                                <span className="tab-count">{imageCount}</span>
                            </TabsTrigger>
                        )}
                        {relationshipCount > 0 && (
                            <TabsTrigger value="relationships">
                                <Users weight="bold" aria-hidden="true" />
                                <span>Relationships</span>
                                <span className="tab-count">{relationshipCount}</span>
                            </TabsTrigger>
                        )}
                        <ExtensionTabTriggers slot="subject.details.tabs" />
                    </TabsList>

                    <TabsContent value="overview">
                        <div className="subject-detail-page__overview">
                            {subject.externalIds && subject.externalIds.length > 0 && (
                                <section className="subject-detail-page__section">
                                    <h2 className="subject-detail-page__section-title">
                                        <LinkSimple weight="bold" aria-hidden="true" />
                                        External IDs
                                    </h2>
                                    <ExternalIds ids={subject.externalIds} />
                                </section>
                            )}

                            {subject.awards && subject.awards.length > 0 && (
                                <section className="subject-detail-page__section">
                                    <h2 className="subject-detail-page__section-title">Awards</h2>
                                    <ul className="subject-detail-page__awards">
                                        {subject.awards.map((award, index) => (
                                            <li key={`${award}-${index}`} className="subject-detail-page__award-item">
                                                {award}
                                            </li>
                                        ))}
                                    </ul>
                                </section>
                            )}

                            <section className="subject-detail-page__section">
                                <h2 className="subject-detail-page__section-title">Related Content</h2>
                                <RelatedContentList items={subject.relatedContent ?? []} />
                            </section>
                        </div>
                    </TabsContent>

                    {imageCount > 0 && (
                        <TabsContent value="images">
                            <ImagesGallery images={subject.images ?? []} altPrefix={subject.title} />
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
                                                    <div
                                                        className="subject-detail-page__person-image subject-detail-page__person-image--placeholder"
                                                        data-testid="subject-detail-page__person-image--placeholder"
                                                    >
                                                        <Users weight="thin" aria-hidden="true" />
                                                    </div>
                                                </div>
                                                <div className="subject-detail-page__person-info">
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

                    <ExtensionTabContents slot="subject.details.tabs" />
                </Tabs>
            </SubjectDetailsTabsSlot>
        </div>
    );
}

SubjectDetailPage.displayName = 'SubjectDetailPage';

export default SubjectDetailPage;
