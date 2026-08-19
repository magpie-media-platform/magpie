import { forwardRef, type ComponentPropsWithoutRef, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { ImageBrokenIcon, CaretLeftIcon, DotsThreeVerticalIcon } from '@phosphor-icons/react';

import { AcquisitionBadge } from '@/components/ui/AcquisitionBadge';
import { Badge } from '@/components/ui/Badge';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { Button } from '@/components/ui/Button';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent } from '@/components/ui/DropdownMenu';
import { AwardsPanel } from '@/components/ui/AwardsPanel';
import type { AwardGroup } from '@/lib/materializedMetadataDisplay';
import { PreviewPlayer, type PreviewClip } from '@/components/ui/PreviewPlayer';
import { useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { isUnrecognizedContentLevel, resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { getFulfillmentBadge } from '@/types/content';
import type { AcquisitionStatus } from '@/types';
import type { FulfillmentStatus } from '@/components/ui/ContentList';

import './ContentDetailsHeader.css';

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

    monitoring: boolean | null;

    effectiveMonitoring: boolean;

    fulfillment: FulfillmentStatus;

    acquisitionStatus?: AcquisitionStatus;

    year?: number;

    publishDate?: string;

    overview?: string;
}

export interface ContentDetailsHeaderProps extends Omit<ComponentPropsWithoutRef<'header'>, 'children' | 'content'> {
    content: ContentDetailItem;

    parent?: ParentContentRef;

    heroPreview?: PreviewClip;

    contentRatings?: { id: string; body: string; rating: string }[];

    awards?: AwardGroup[];

    actions?: React.ReactNode;

    hideMobileActions?: boolean;
}

export const ContentDetailsHeader = forwardRef<HTMLElement, ContentDetailsHeaderProps>(
    (
        {
            content,
            parent,
            heroPreview,
            contentRatings,
            awards,
            actions,
            hideMobileActions = false,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['content-details-header', className].filter(Boolean).join(' ');
        const isMonitored = content.effectiveMonitoring;
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
                            <img
                                src={content.posterUrl}
                                alt={`${content.title} artwork`}
                                className="content-details-header__poster-img"
                                loading="eager"
                            />
                        </div>
                    ) : (
                        <div
                            className="content-details-header__poster content-details-header__poster--placeholder"
                            data-testid="content-details-header__poster--placeholder"
                        >
                            <ImageBrokenIcon weight="thin" aria-hidden="true" />
                        </div>
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

                        {isMonitored && (
                            <Badge variant={getFulfillmentBadge(content.fulfillment).variant} size="md" dot>
                                {getFulfillmentBadge(content.fulfillment).label}
                            </Badge>
                        )}

                        {content.acquisitionStatus && <AcquisitionBadge status={content.acquisitionStatus} />}

                        {contentRatings?.map((rating) => (
                            <span key={rating.id} className="content-details-header__rating">
                                <span className="content-details-header__rating-body">{rating.body}</span>
                                {rating.rating}
                            </span>
                        ))}
                    </div>

                    {content.overview && <p className="content-details-header__overview">{content.overview}</p>}

                    <AwardsPanel groups={awards ?? []} data-component="content-awards-panel" />

                    {actions && <div className="content-details-header__actions">{actions}</div>}

                    {actions && !hideMobileActions && (
                        <div className="content-details-header__mobile-actions">
                            <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                    <Button
                                        variant="secondary"
                                        size="sm"
                                        aria-label="Actions"
                                        data-testid="content-details-header__mobile-menu"
                                    >
                                        <DotsThreeVerticalIcon weight="bold" aria-hidden="true" />
                                    </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end" sideOffset={4}>
                                    <div className="content-details-header__mobile-actions-content">{actions}</div>
                                </DropdownMenuContent>
                            </DropdownMenu>
                        </div>
                    )}
                </div>
            </header>
        );
    },
);

ContentDetailsHeader.displayName = 'ContentDetailsHeader';

export default ContentDetailsHeader;
