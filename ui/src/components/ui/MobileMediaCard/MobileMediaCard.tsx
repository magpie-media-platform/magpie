import { type KeyboardEvent, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { LocalContentLink } from '@/components/content/LocalContentPreview';
import { CheckCircleIcon, CircleIcon } from '@phosphor-icons/react';

import { ContentPoster } from '@/components/content/ContentCardPrimitives';
import { useLongPress } from '@/hooks/ui/useLongPress';

import './MobileMediaCard.css';

export interface MobileMediaCardProps {
    posterUrl?: string;

    posterAlt: string;

    posterTo?: string;

    posterLinkLabel?: string;

    title: ReactNode;

    titleAccessory?: ReactNode;

    description?: ReactNode;

    children?: ReactNode;

    trailing?: ReactNode;

    to?: string;
    previewContentId?: string;

    className?: string;

    role?: string;

    selectable?: boolean;

    selected?: boolean;

    onToggleSelected?: () => void;

    onLongPress?: () => void;

    selectionLabel?: string;

    [dataAttr: `data-${string}`]: string | undefined;
}

export function MobileMediaCard({
    posterUrl,
    posterAlt,
    posterTo,
    posterLinkLabel,
    title,
    titleAccessory,
    description,
    children,
    trailing,
    to,
    previewContentId,
    className,
    role,
    selectable = false,
    selected = false,
    onToggleSelected,
    onLongPress,
    selectionLabel,
    ...dataAttrs
}: MobileMediaCardProps) {
    const longPress = useLongPress({ onLongPress: onLongPress ?? (() => {}) });

    const poster = (
        <ContentPoster
            posterUrl={posterUrl}
            alt={posterAlt}
            className="mobile-media-card__poster"
            placeholderClassName="mobile-media-card__poster--placeholder"
        />
    );

    const posterNode =
        !selectable && previewContentId && !to ? (
            <LocalContentLink
                contentId={previewContentId}
                className="mobile-media-card__poster-link"
                aria-label={posterLinkLabel}
            >
                {poster}
            </LocalContentLink>
        ) : !selectable && posterTo && !to ? (
            <Link to={posterTo} className="mobile-media-card__poster-link" aria-label={posterLinkLabel}>
                {poster}
            </Link>
        ) : (
            poster
        );

    const selectIndicator = selectable ? (
        <span className="mobile-media-card__select-indicator" aria-hidden="true">
            {selected ? <CheckCircleIcon weight="fill" /> : <CircleIcon weight="bold" />}
        </span>
    ) : null;

    const body = (
        <div className="mobile-media-card__body">
            <div className="mobile-media-card__head">
                {typeof title === 'string' ? (
                    <span className="mobile-media-card__title" title={title}>
                        {title}
                    </span>
                ) : (
                    <div className="mobile-media-card__title-slot">{title}</div>
                )}
                {titleAccessory}
            </div>
            {children && <div className="mobile-media-card__meta">{children}</div>}
            {description && <p className="mobile-media-card__description line-clamp-2">{description}</p>}
            {trailing && <div className="mobile-media-card__trailing">{trailing}</div>}
        </div>
    );

    const classes = [
        'mobile-media-card',
        selectable && 'mobile-media-card--selectable',
        selected && 'mobile-media-card--selected',
        className,
    ]
        .filter(Boolean)
        .join(' ');

    if (selectable) {
        const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                onToggleSelected?.();
            }
        };

        return (
            <div
                className={classes}
                role="button"
                tabIndex={0}
                aria-pressed={selected}
                aria-label={selectionLabel}
                onClick={onToggleSelected}
                onKeyDown={handleKeyDown}
                {...dataAttrs}
            >
                {selectIndicator}
                {posterNode}
                {body}
            </div>
        );
    }

    if (to) {
        if (previewContentId)
            return (
                <LocalContentLink contentId={previewContentId} className={classes} role={role} {...dataAttrs}>
                    {posterNode}
                    {body}
                </LocalContentLink>
            );
        const linkProps = onLongPress
            ? { ...longPress.handlers, onClick: (event: React.MouseEvent) => longPress.consumeClick(event) }
            : {};

        return (
            <Link to={to} className={classes} role={role} {...linkProps} {...dataAttrs}>
                {posterNode}
                {body}
            </Link>
        );
    }

    return (
        <div className={classes} role={role} {...dataAttrs}>
            {posterNode}
            {body}
        </div>
    );
}

MobileMediaCard.displayName = 'MobileMediaCard';

export default MobileMediaCard;
