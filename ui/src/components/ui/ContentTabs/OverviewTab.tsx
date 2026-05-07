import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { Star } from '@phosphor-icons/react';

import { Badge } from '../Badge';
import { ExternalIds, type ExternalId } from '../ExternalIds';

import './ContentTabs.css';

export interface ExternalLink {
    label: string;

    url: string;
}

export interface OverviewMetadata {
    overview?: string;

    genres?: string[];

    rating?: number;

    ratingVotes?: number;

    runtime?: number;

    certification?: string;

    network?: string;

    language?: string;

    status?: string;

    externalIds?: ExternalId[];

    externalLinks?: ExternalLink[];

    subjectId?: string;

    subjectName?: string;
}

export interface OverviewTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    metadata: OverviewMetadata;
}

function formatRuntime(minutes: number): string {
    if (minutes < 60) {
        return `${minutes}m`;
    }
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return mins > 0 ? `${hours}h ${mins}m` : `${hours}h`;
}

function formatRating(rating: number, votes?: number): string {
    const formatted = rating.toFixed(1);
    if (votes !== undefined && votes > 0) {
        return `${formatted} (${votes.toLocaleString()} votes)`;
    }
    return formatted;
}

function linksToIds(links: ExternalLink[]): ExternalId[] {
    return links.map((link) => ({
        source: link.label,
        id: link.label,
        url: link.url,
    }));
}

export const OverviewTab = forwardRef<HTMLDivElement, OverviewTabProps>(({ metadata, className, ...props }, ref) => {
    const classes = ['overview-tab', className].filter(Boolean).join(' ');

    const hasMetadata =
        metadata.genres?.length ||
        metadata.rating !== undefined ||
        metadata.runtime !== undefined ||
        metadata.certification ||
        metadata.network ||
        metadata.language ||
        metadata.status;

    const externalIds =
        metadata.externalIds ?? (metadata.externalLinks ? linksToIds(metadata.externalLinks) : undefined);
    const hasExternalIds = externalIds && externalIds.length > 0;

    return (
        <div ref={ref} className={classes} data-component="overview-tab" {...props}>
            {metadata.overview && (
                <section className="overview-tab__section">
                    <p className="overview-tab__description">{metadata.overview}</p>
                </section>
            )}

            {hasMetadata && (
                <section className="overview-tab__section">
                    <h2 className="overview-tab__section-title">Details</h2>
                    <dl className="overview-tab__metadata">
                        {metadata.rating !== undefined && (
                            <div className="overview-tab__metadata-item">
                                <dt className="overview-tab__metadata-label">Rating</dt>
                                <dd className="overview-tab__metadata-value overview-tab__rating">
                                    <Star weight="fill" aria-hidden="true" />
                                    <span>{formatRating(metadata.rating, metadata.ratingVotes)}</span>
                                </dd>
                            </div>
                        )}

                        {metadata.status && (
                            <div className="overview-tab__metadata-item">
                                <dt className="overview-tab__metadata-label">Status</dt>
                                <dd className="overview-tab__metadata-value">{metadata.status}</dd>
                            </div>
                        )}

                        {metadata.runtime !== undefined && (
                            <div className="overview-tab__metadata-item">
                                <dt className="overview-tab__metadata-label">Runtime</dt>
                                <dd className="overview-tab__metadata-value">{formatRuntime(metadata.runtime)}</dd>
                            </div>
                        )}

                        {metadata.certification && (
                            <div className="overview-tab__metadata-item">
                                <dt className="overview-tab__metadata-label">Certification</dt>
                                <dd className="overview-tab__metadata-value">{metadata.certification}</dd>
                            </div>
                        )}

                        {metadata.network && (
                            <div className="overview-tab__metadata-item">
                                <dt className="overview-tab__metadata-label">Network</dt>
                                <dd className="overview-tab__metadata-value">{metadata.network}</dd>
                            </div>
                        )}

                        {metadata.language && (
                            <div className="overview-tab__metadata-item">
                                <dt className="overview-tab__metadata-label">Language</dt>
                                <dd className="overview-tab__metadata-value">{metadata.language}</dd>
                            </div>
                        )}
                    </dl>
                </section>
            )}

            {metadata.genres && metadata.genres.length > 0 && (
                <section className="overview-tab__section">
                    <h2 className="overview-tab__section-title">Genres</h2>
                    <div className="overview-tab__genres">
                        {metadata.genres.map((genre) => (
                            <Badge key={genre} variant="default" size="sm">
                                {genre}
                            </Badge>
                        ))}
                    </div>
                </section>
            )}

            {hasExternalIds && (
                <section className="overview-tab__section">
                    <h2 className="overview-tab__section-title">External IDs</h2>
                    <ExternalIds ids={externalIds} />
                </section>
            )}

            {!metadata.overview && !hasMetadata && !metadata.genres?.length && !hasExternalIds && (
                <div className="overview-tab__empty">
                    <p>No overview information available.</p>
                </div>
            )}
        </div>
    );
});

OverviewTab.displayName = 'OverviewTab';

export default OverviewTab;
