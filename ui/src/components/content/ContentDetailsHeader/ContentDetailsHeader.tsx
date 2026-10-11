import {
    forwardRef,
    useId,
    useLayoutEffect,
    useRef,
    useState,
    type ComponentPropsWithoutRef,
    type CSSProperties,
} from 'react';
import { Link } from 'react-router-dom';
import { CaretLeftIcon } from '@phosphor-icons/react';

import {
    ContentFulfillmentBadge,
    ContentMonitoringIndicator,
    ContentPoster,
} from '@/components/content/ContentCardPrimitives';
import { AcquisitionBadge } from '@/components/ui/AcquisitionBadge';
import { Badge } from '@/components/ui/Badge';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { AwardsPanel } from '@/components/ui/AwardsPanel';
import { CollapsibleChipList } from '@/components/ui/CollapsibleChipList';
import type { AwardGroup } from '@/lib/materializedMetadataDisplay';
import type { TemporalPrecision } from '@/types/contentMetadata';
import { PreviewPlayer, type PreviewClip } from '@/components/ui/PreviewPlayer';
import { useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { isUnrecognizedContentLevel, resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { toPlainTextSummary } from '@/lib/plainTextSummary';
import type { AcquisitionStatus, MonitoringMode } from '@/types';
import type { FulfillmentStatus } from '@/components/ui/ContentList';

import './ContentDetailsHeader.css';

const INITIAL_RATINGS_VISIBLE = 4;

export interface ParentContentRef {
    id: string;

    title: string;

    path: string;
}

export interface ContentDetailItem {
    id: string;

    title: string;

    posterUrl?: string;

    levelLabel?: string;

    level?: string;

    contentType?: string;

    effectiveMonitoring: MonitoringMode;

    fulfillment: FulfillmentStatus;
    importPending?: boolean;

    acquisitionStatus?: AcquisitionStatus;

    year?: number;

    publishDate?: string;

    publishDatePrecision?: TemporalPrecision;

    overview?: string;
}

interface ContentRating {
    id: string;

    body: string;

    rating: string;
}

export interface ContentDetailsHeaderProps extends Omit<ComponentPropsWithoutRef<'header'>, 'children' | 'content'> {
    content: ContentDetailItem;

    parent?: ParentContentRef;

    heroPreview?: PreviewClip;

    contentRatings?: ContentRating[];

    awards?: AwardGroup[];

    actions?: React.ReactNode;
}

function sortRatingsLocaleFirst(ratings: ContentRating[]): ContentRating[] {
    const region = new Intl.Locale(navigator.language).maximize().region;
    const isLocal = (rating: ContentRating) => rating.body.toUpperCase() === region;

    return [...ratings].sort((a, b) => Number(isLocal(b)) - Number(isLocal(a)));
}

function ExpandableOverview({ text }: { text: string }) {
    const overviewRef = useRef<HTMLParagraphElement>(null);
    const overviewId = useId();
    const [expanded, setExpanded] = useState(false);
    const [clamped, setClamped] = useState(false);

    useLayoutEffect(() => {
        const overview = overviewRef.current;
        if (!overview || expanded) {
            return;
        }

        const measure = () => setClamped(overview.scrollHeight > overview.clientHeight);
        measure();
        const observer = new ResizeObserver(measure);
        observer.observe(overview);
        return () => observer.disconnect();
    }, [text, expanded]);

    return (
        <>
            <p
                ref={overviewRef}
                id={overviewId}
                className={[
                    'content-details-header__overview',
                    expanded && 'content-details-header__overview--expanded',
                ]
                    .filter(Boolean)
                    .join(' ')}
            >
                {text}
            </p>
            {clamped && (
                <button
                    type="button"
                    className="collapsible-chip-list__toggle content-details-header__overview-toggle"
                    aria-controls={overviewId}
                    aria-expanded={expanded}
                    data-action="toggle-collapsible"
                    onClick={() => setExpanded((value) => !value)}
                >
                    {expanded ? 'Show less' : 'Show more'}
                </button>
            )}
        </>
    );
}

export const ContentDetailsHeader = forwardRef<HTMLElement, ContentDetailsHeaderProps>(
    ({ content, parent, heroPreview, contentRatings, awards, actions, className, ...props }, ref) => {
        const classes = ['content-details-header', className].filter(Boolean).join(' ');
        const { contentTypes } = useLibraryTypes();
        const typeIconName = useTypeIconName();
        const contentTypeLabel =
            resolveContentTypeLabel(
                {
                    level: content.level,
                    levelLabel: content.levelLabel,
                    contentType: content.contentType,
                },
                contentTypes,
            ) || 'Content';
        const needsClassification = isUnrecognizedContentLevel(
            { level: content.level, contentType: content.contentType },
            contentTypes,
        );
        const overview = toPlainTextSummary(content.overview);
        return (
            <header
                ref={ref}
                className={classes}
                data-component="content-details-header"
                data-content-id={content.id}
                {...props}
            >
                <div className="content-details-header__poster-container">
                    {heroPreview ? (
                        <div
                            className="content-details-header__poster content-details-header__poster--preview"
                            data-testid="content-details-header__poster--preview"
                        >
                            <PreviewPlayer preview={heroPreview} posterUrl={content.posterUrl} fit="poster" />
                        </div>
                    ) : content.posterUrl ? (
                        <div
                            className="content-details-header__poster content-details-header__poster--image"
                            style={{ '--hero-image': `url(${JSON.stringify(content.posterUrl)})` } as CSSProperties}
                        >
                            <ContentPoster
                                posterUrl={content.posterUrl}
                                placeholderClassName="content-details-header__poster--placeholder"
                                alt={`${content.title} artwork`}
                                className="content-details-header__poster-img"
                                loading="eager"
                            />
                        </div>
                    ) : (
                        <ContentPoster
                            className="content-details-header__poster"
                            placeholderClassName="content-details-header__poster--placeholder"
                        />
                    )}
                </div>

                <div className="content-details-header__info">
                    {parent && (
                        <Link
                            to={parent.path}
                            className="content-details-header__parent-link"
                            data-testid="content-details-header__parent-link"
                            data-action="navigate-parent"
                            data-parent-id={parent.id}
                        >
                            <CaretLeftIcon weight="bold" aria-hidden="true" />
                            <span>{parent.title}</span>
                        </Link>
                    )}

                    <div className="content-details-header__title-row">
                        <h1 className="content-details-header__title">
                            {content.title}
                            {content.year && <span className="content-details-header__year">({content.year})</span>}
                        </h1>
                    </div>

                    <div className="content-details-header__badges">
                        <Badge variant={needsClassification ? 'warning' : 'default'} size="md">
                            <CdnPhosphorIcon
                                name={typeIconName(content.contentType)}
                                weight="duotone"
                                aria-hidden="true"
                            />
                            {contentTypeLabel}
                        </Badge>

                        <ContentMonitoringIndicator
                            effectiveMonitoring={content.effectiveMonitoring}
                            className="content-details-header__monitoring"
                            activeClassName="content-details-header__monitoring--active"
                            labelClassName="content-details-header__monitoring-label"
                        />

                        <ContentFulfillmentBadge
                            fulfillment={content.fulfillment}
                            importPending={content.importPending}
                            publishDate={content.publishDate}
                            publishDatePrecision={content.publishDatePrecision}
                            size="md"
                        />

                        {content.acquisitionStatus && <AcquisitionBadge status={content.acquisitionStatus} />}

                        <CollapsibleChipList
                            aria-label="Content ratings"
                            className="content-details-header__ratings"
                            listClassName="content-details-header__ratings-list"
                            initialVisible={INITIAL_RATINGS_VISIBLE}
                            items={sortRatingsLocaleFirst(contentRatings ?? []).map((rating) => ({
                                key: rating.id,
                                node: (
                                    <span className="content-details-header__rating">
                                        <span className="content-details-header__rating-body">{rating.body}</span>
                                        {rating.rating}
                                    </span>
                                ),
                            }))}
                        />
                    </div>

                    {overview && <ExpandableOverview key={content.id} text={overview} />}

                    <AwardsPanel groups={awards ?? []} data-component="content-awards-panel" />

                    {actions && <div className="content-details-header__actions">{actions}</div>}
                </div>
            </header>
        );
    },
);

ContentDetailsHeader.displayName = 'ContentDetailsHeader';

export default ContentDetailsHeader;
