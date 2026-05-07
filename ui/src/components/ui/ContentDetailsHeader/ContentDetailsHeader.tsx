import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { Link } from 'react-router-dom';
import { Eye, EyeSlash, ImageBroken, CaretLeft, Tag, DotsThreeVertical } from '@phosphor-icons/react';

import { Badge, type BadgeVariant } from '../Badge';
import { Button } from '../Button';
import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
} from '../DropdownMenu';
import type { ContentKind, MonitoringStatus, FulfillmentStatus } from '../ContentList';

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

    kind: ContentKind;

    monitoring: MonitoringStatus;

    fulfillment: FulfillmentStatus;

    year?: number;

    overview?: string;

    subjectId?: string;

    subjectName?: string;
}

export interface ContentDetailsHeaderProps extends Omit<ComponentPropsWithoutRef<'header'>, 'children' | 'content'> {
    content: ContentDetailItem;

    parent?: ParentContentRef;

    actions?: React.ReactNode;
}

function getFulfillmentVariant(status: FulfillmentStatus): BadgeVariant {
    const variants: Record<FulfillmentStatus, BadgeVariant> = {
        missing: 'missing',
        upgradable: 'upgradable',
        fulfilled: 'fulfilled',
        fulfilledExternally: 'fulfilledExternally',
    };
    return variants[status];
}

function formatFulfillmentLabel(status: FulfillmentStatus): string {
    const labels: Record<FulfillmentStatus, string> = {
        missing: 'Missing',
        upgradable: 'Upgradable',
        fulfilled: 'Fulfilled',
        fulfilledExternally: 'External',
    };
    return labels[status];
}

function formatKindLabel(kind: ContentKind): string {
    const labels: Record<ContentKind, string> = {
        episode: 'Episode',
        movie: 'Movie',
        music: 'Music',
        book: 'Book',
        unknown: 'Unknown',
    };
    return labels[kind];
}

export const ContentDetailsHeader = forwardRef<HTMLElement, ContentDetailsHeaderProps>(
    ({ content, parent, actions, className, ...props }, ref) => {
        const classes = ['content-details-header', className].filter(Boolean).join(' ');

        return (
            <header
                ref={ref}
                className={classes}
                data-component="content-details-header"
                data-content-id={content.id}
                {...props}
            >
                <div className="content-details-header__poster-container">
                    {content.posterUrl ? (
                        <img
                            src={content.posterUrl}
                            alt={`${content.title} poster`}
                            className="content-details-header__poster"
                            loading="eager"
                        />
                    ) : (
                        <div
                            className="content-details-header__poster content-details-header__poster--placeholder"
                            data-testid="content-details-header__poster--placeholder"
                        >
                            <ImageBroken weight="thin" aria-hidden="true" />
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
                            <CaretLeft weight="bold" aria-hidden="true" />
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
                        <Badge variant="default" size="md">
                            {formatKindLabel(content.kind)}
                        </Badge>

                        <div
                            className={`content-details-header__monitoring ${
                                content.monitoring === 'active'
                                    ? 'content-details-header__monitoring--active'
                                    : 'content-details-header__monitoring--inactive'
                            }`}
                            title={content.monitoring === 'active' ? 'Monitored' : 'Not monitored'}
                        >
                            {content.monitoring === 'active' ? (
                                <>
                                    <Eye weight="fill" aria-hidden="true" />
                                    <span>Monitored</span>
                                </>
                            ) : (
                                <>
                                    <EyeSlash weight="regular" aria-hidden="true" />
                                    <span>Not Monitored</span>
                                </>
                            )}
                        </div>

                        <Badge variant={getFulfillmentVariant(content.fulfillment)} size="md" dot>
                            {formatFulfillmentLabel(content.fulfillment)}
                        </Badge>

                        {content.subjectId && (
                            <Link
                                to={`/subjects/${content.subjectId}`}
                                state={{ fromContent: { id: content.id, title: content.title } }}
                                className="content-details-header__subject-link"
                                data-action="navigate-subject"
                                data-subject-id={content.subjectId}
                            >
                                <Tag weight="bold" aria-hidden="true" />
                                <span>{content.subjectName || 'View Metadata'}</span>
                            </Link>
                        )}
                    </div>

                    {content.overview && <p className="content-details-header__overview">{content.overview}</p>}

                    {actions && <div className="content-details-header__actions">{actions}</div>}

                    {actions && (
                        <div className="content-details-header__mobile-actions">
                            <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                    <Button
                                        variant="secondary"
                                        size="sm"
                                        aria-label="Actions"
                                        data-testid="content-details-header__mobile-menu"
                                    >
                                        <DotsThreeVertical weight="bold" aria-hidden="true" />
                                    </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end" sideOffset={4}>
                                    <div className="content-details-header__mobile-actions-content">
                                        {actions}
                                    </div>
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
