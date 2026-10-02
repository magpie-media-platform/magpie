import { Link } from 'react-router-dom';
import { FilmStripIcon, ArrowCounterClockwiseIcon, PlusIcon, TelevisionIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { LibraryCountCard, formatLibraryItemCount } from '@/components/ui/LibraryCountCard';
import {
    useLibraryTypes,
    useLibraryCounts,
    useTypeIconName,
    type LibraryTypeDefinition,
    type LibraryCounts,
} from '@/hooks/useLibraryTypes';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useAuth, Role } from '@/auth';

import './LibraryLandingPage.css';

interface TypeCardProps {
    type: LibraryTypeDefinition;
    basePath: string;
    isSubject?: boolean;
    counts: LibraryCounts;
}

function TypeCard({ type, basePath, isSubject = false, counts }: TypeCardProps) {
    const typeIconName = useTypeIconName();

    return (
        <LibraryCountCard
            to={`${basePath}/${type.slug}`}
            icon={<CdnPhosphorIcon name={typeIconName(type.slug, { subject: isSubject })} weight="duotone" />}
            name={type.pluralName}
            countLabel={formatLibraryItemCount(counts[type.slug])}
            dataComponent="type-card"
            linkDataAttributes={{ 'data-type-id': type.id }}
            iconTestId="library-landing__type-icon"
            skeletonTestId="library-landing__count-skeleton"
        />
    );
}

function TypeCardSkeleton() {
    return (
        <div className="library-landing__type-card--skeleton" data-testid="library-landing__type-card--skeleton">
            <Skeleton className="library-landing__skeleton-icon" />
            <div className="library-landing__skeleton-lines">
                <Skeleton className="library-landing__skeleton-text" />
                <Skeleton className="library-landing__skeleton-count" />
            </div>
        </div>
    );
}

export function LibraryLandingPage() {
    useDocumentTitle('Library');
    const { hasPermission, hasRole } = useAuth();
    const canAddContent = hasPermission('content.add');
    const isAdmin = hasRole(Role.Admin);

    const { contentTypes, subjectTypes, isLoading, error, refetch } = useLibraryTypes();
    const { counts } = useLibraryCounts(contentTypes, subjectTypes);

    const hasContentTypes = contentTypes.length > 0;
    const hasSubjectTypes = subjectTypes.length > 0;

    const contentCountsLoaded = contentTypes.length > 0 && contentTypes.every((t) => counts[t.slug] !== undefined);
    const isContentEmpty = !isLoading && contentCountsLoaded && contentTypes.every((t) => (counts[t.slug] ?? 0) === 0);

    return (
        <div className="library-landing" data-page="library-landing">
            <PageHeader
                className="library-landing__header"
                title="Library"
                description="Browse what's in the library, grouped by content and the subjects it relates to."
                icon={<FilmStripIcon weight="duotone" aria-hidden="true" data-testid="library-landing__icon" />}
            />

            {error && (
                <div className="library-landing__error" role="alert">
                    <p>Failed to load library types. Please try again.</p>
                    <Button variant="secondary" onClick={() => refetch()} data-action="retry">
                        <ArrowCounterClockwiseIcon size={16} weight="bold" aria-hidden="true" />
                        Try Again
                    </Button>
                </div>
            )}

            <section className="library-landing__section" aria-labelledby="content-types-heading">
                <h2 id="content-types-heading" className="library-landing__section-title">
                    Content
                </h2>

                {!isContentEmpty && (
                    <>
                        <div className="library-landing__grid">
                            {isLoading ? (
                                <>
                                    <TypeCardSkeleton />
                                    <TypeCardSkeleton />
                                    <TypeCardSkeleton />
                                </>
                            ) : hasContentTypes ? (
                                contentTypes.map((type) => (
                                    <TypeCard key={type.id} type={type} basePath="/library/content" counts={counts} />
                                ))
                            ) : (
                                <p className="library-landing__empty">
                                    No content types registered. Install extensions to add content types.
                                </p>
                            )}
                        </div>
                        {!isLoading && (
                            <Link to="/library/all" className="library-landing__view-all">
                                View all content
                            </Link>
                        )}
                    </>
                )}
            </section>

            {isContentEmpty && (
                <EmptyState
                    icon={<FilmStripIcon weight="duotone" />}
                    title="Your library is empty"
                    description="Search every provider and add content to start tracking it, or connect a media server to import your existing collection."
                    action={
                        (canAddContent || isAdmin) && (
                            <div className="library-landing__onboarding-actions">
                                {canAddContent && (
                                    <Button asChild variant="primary" size="sm">
                                        <Link to="/library/add" data-action="add-content">
                                            <PlusIcon weight="bold" aria-hidden="true" />
                                            Add content
                                        </Link>
                                    </Button>
                                )}
                                {isAdmin && (
                                    <Button asChild variant="secondary" size="sm">
                                        <Link to="/admin/media-servers" data-action="connect-media-server">
                                            <TelevisionIcon weight="duotone" aria-hidden="true" />
                                            Connect a Media Server
                                        </Link>
                                    </Button>
                                )}
                            </div>
                        )
                    }
                    data-component="onboarding-prompt"
                />
            )}

            <hr className="library-landing__separator" />

            <section className="library-landing__section" aria-labelledby="subject-types-heading">
                <h2 id="subject-types-heading" className="library-landing__section-title">
                    Subjects
                </h2>
                <div className="library-landing__grid">
                    {isLoading ? (
                        <>
                            <TypeCardSkeleton />
                            <TypeCardSkeleton />
                        </>
                    ) : hasSubjectTypes ? (
                        subjectTypes.map((type) => (
                            <TypeCard
                                key={type.id}
                                type={type}
                                basePath="/library/subjects"
                                isSubject
                                counts={counts}
                            />
                        ))
                    ) : (
                        <p className="library-landing__empty">
                            No subject types registered. Install extensions to add them.
                        </p>
                    )}
                </div>
                {!isLoading && (
                    <Link to="/library/subjects/all" className="library-landing__view-all">
                        View all subjects
                    </Link>
                )}
            </section>
        </div>
    );
}

LibraryLandingPage.displayName = 'LibraryLandingPage';

export default LibraryLandingPage;
