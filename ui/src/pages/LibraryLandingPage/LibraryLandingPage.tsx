import { Link, useNavigate } from 'react-router-dom';
import { FilmStrip, Cube, ArrowCounterClockwise, Star, Television } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    useLibraryTypes,
    useLibraryCounts,
    type LibraryTypeDefinition,
    type LibraryCounts,
} from '@/hooks/useLibraryTypes';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useAuth, Role } from '@/auth';

import './LibraryLandingPage.css';

function formatCount(count: number | undefined): string | undefined {
    if (count === undefined) {
        return undefined;
    }
    return count === 1 ? '1 item' : `${count.toLocaleString()} items`;
}

interface TypeCardProps {
    type: LibraryTypeDefinition;
    basePath: string;
    isSubject?: boolean;
    counts: LibraryCounts;
}

function TypeCard({ type, basePath, isSubject = false, counts }: TypeCardProps) {
    const IconComponent = isSubject ? Cube : FilmStrip;
    const href = `${basePath}/${type.slug}`;
    const countLabel = formatCount(counts[type.slug]);

    return (
        <Link to={href} className="library-landing__type-link" data-component="type-card" data-type-id={type.id}>
            <Card interactive className="library-landing__type-card">
                <div className="library-landing__type-content">
                    <span
                        className="library-landing__type-icon"
                        aria-hidden="true"
                        data-testid="library-landing__type-icon"
                    >
                        <IconComponent weight="duotone" />
                    </span>
                    <div className="library-landing__type-info">
                        <span className="library-landing__type-name">{type.name}</span>
                        {countLabel !== undefined ? (
                            <span className="library-landing__type-count">{countLabel}</span>
                        ) : (
                            <Skeleton
                                className="library-landing__count-skeleton"
                                data-testid="library-landing__count-skeleton"
                            />
                        )}
                    </div>
                </div>
            </Card>
        </Link>
    );
}

function TypeCardSkeleton() {
    return (
        <div
            className="library-landing__type-card library-landing__type-card--skeleton"
            data-testid="library-landing__type-card--skeleton"
        >
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
    const navigate = useNavigate();
    const { hasRole } = useAuth();
    const canAddContent = hasRole(Role.Requester);
    const isAdmin = hasRole(Role.Admin);

    const { contentTypes, subjectTypes, isLoading, error, refetch } = useLibraryTypes();
    const { counts } = useLibraryCounts(contentTypes, subjectTypes);

    const hasContentTypes = contentTypes.length > 0;
    const hasSubjectTypes = subjectTypes.length > 0;

    const contentCountsLoaded = contentTypes.length > 0 && contentTypes.every((t) => counts[t.slug] !== undefined);
    const isContentEmpty = !isLoading && contentCountsLoaded && contentTypes.every((t) => (counts[t.slug] ?? 0) === 0);

    return (
        <div className="library-landing" data-page="library-landing">
            <header className="library-landing__header">
                <FilmStrip
                    weight="duotone"
                    className="library-landing__icon"
                    aria-hidden="true"
                    data-testid="library-landing__icon"
                />
                <h1 className="library-landing__title">Library</h1>
            </header>

            {error && (
                <div className="library-landing__error" role="alert">
                    <p>Failed to load library types. Please try again.</p>
                    <Button variant="secondary" onClick={() => refetch()} data-action="retry">
                        <ArrowCounterClockwise size={16} weight="bold" aria-hidden="true" />
                        Try Again
                    </Button>
                </div>
            )}

            <section className="library-landing__section" aria-labelledby="content-types-heading">
                <h2 id="content-types-heading" className="library-landing__section-title">
                    Content
                </h2>
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
            </section>

            {isContentEmpty && (
                <EmptyState
                    icon={<FilmStrip weight="duotone" />}
                    title="Your library is empty"
                    description="Add movies and TV shows to a wishlist to start monitoring them, or connect a media server to import your existing collection."
                    action={
                        (canAddContent || isAdmin) && (
                            <div className="library-landing__onboarding-actions">
                                {canAddContent && (
                                    <Button
                                        variant="primary"
                                        size="sm"
                                        onClick={() => navigate('/wishlists')}
                                        data-action="go-to-wishlists"
                                    >
                                        <Star weight="duotone" aria-hidden="true" />
                                        Go to Wishlists
                                    </Button>
                                )}
                                {isAdmin && (
                                    <Button
                                        variant="secondary"
                                        size="sm"
                                        onClick={() => navigate('/admin/media-servers')}
                                        data-action="connect-media-server"
                                    >
                                        <Television weight="duotone" aria-hidden="true" />
                                        Connect a Media Server
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
                            No subject types registered. Install extensions to add subject types.
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
