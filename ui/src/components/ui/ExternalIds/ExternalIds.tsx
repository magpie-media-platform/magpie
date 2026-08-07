import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { ArrowSquareOutIcon, LinkSimpleIcon } from '@phosphor-icons/react';

import { DetailsPanel } from '@/components/ui/DetailsPanel';
import { safeHttpUrl } from '@/lib/safeUrl';

import './ExternalIds.css';

export interface ExternalId {
    source: string;

    id: string;

    url?: string;
}

export interface ExternalIdsLeadingChip {
    key: string;

    label: string;

    url: string;

    icon?: ReactNode;

    source?: string;

    testId?: string;

    dataMediaServerId?: string;
}

export interface ExternalIdsProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    ids: ExternalId[];

    variant?: 'ids' | 'chips';

    label?: string;

    leadingChips?: ExternalIdsLeadingChip[];

    itemTestId?: (extId: ExternalId) => string;
}

export const ExternalIds = forwardRef<HTMLDivElement, ExternalIdsProps>(
    ({ ids, variant = 'ids', label, leadingChips, itemTestId, className, ...props }, ref) => {
        const linkedLeadingChips = (leadingChips ?? [])
            .map((chip) => ({ chip, url: safeHttpUrl(chip.url) }))
            .filter((item): item is { chip: ExternalIdsLeadingChip; url: string } => item.url !== null);
        const hasLeading = linkedLeadingChips.length > 0;

        const linkedChipIds =
            variant === 'chips'
                ? ids
                      .map((extId) => ({ extId, url: safeHttpUrl(extId.url) }))
                      .filter((item): item is { extId: ExternalId; url: string } => item.url !== null)
                : [];

        if (variant === 'chips' && linkedChipIds.length === 0 && !hasLeading) {
            return null;
        }

        if (variant !== 'chips' && ids.length === 0) {
            return null;
        }

        if (variant === 'chips') {
            const classes = ['external-ids', 'external-ids--chips', className].filter(Boolean).join(' ');
            return (
                <div ref={ref} className={classes} data-component="external-ids" data-variant="chips" {...props}>
                    {label && <span className="external-ids__label">{label}</span>}
                    <ul className="external-ids__chip-list" role="list">
                        {hasLeading &&
                            linkedLeadingChips.map(({ chip, url }) => (
                                <li key={chip.key}>
                                    <a
                                        className="external-ids__chip external-ids__chip--media-server"
                                        href={url}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        data-source={chip.source}
                                        data-testid={chip.testId}
                                        data-media-server-id={chip.dataMediaServerId}
                                    >
                                        {chip.icon}
                                        <span>{chip.label}</span>
                                        <ArrowSquareOutIcon weight="bold" aria-hidden="true" />
                                    </a>
                                </li>
                            ))}
                        {linkedChipIds.map(({ extId, url }) => {
                            const sourceSlug = extId.source.toLowerCase();
                            const key = `${sourceSlug}:${extId.id}`;
                            const testId = itemTestId?.(extId);
                            return (
                                <li key={key}>
                                    <a
                                        className="external-ids__chip"
                                        href={url}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        data-source={sourceSlug}
                                        data-testid={testId}
                                    >
                                        <span className="external-ids__chip-source">{extId.source}</span>
                                        <ArrowSquareOutIcon weight="bold" aria-hidden="true" />
                                    </a>
                                </li>
                            );
                        })}
                    </ul>
                </div>
            );
        }

        const classes = ['external-ids', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-component="external-ids" {...props}>
                {ids.map((extId) => {
                    const linkUrl = safeHttpUrl(extId.url);
                    if (linkUrl) {
                        return (
                            <a
                                key={`${extId.source}-${extId.id}`}
                                href={linkUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="external-ids__item external-ids__item--linked"
                                data-source={extId.source.toLowerCase()}
                                data-linked
                            >
                                <LinkSimpleIcon
                                    weight="bold"
                                    className="external-ids__service-icon"
                                    aria-hidden="true"
                                />
                                <span className="external-ids__source">{extId.source}</span>
                                <span className="external-ids__value">{extId.id}</span>
                                <ArrowSquareOutIcon
                                    weight="bold"
                                    className="external-ids__link-icon"
                                    aria-hidden="true"
                                />
                            </a>
                        );
                    }

                    return (
                        <span
                            key={`${extId.source}-${extId.id}`}
                            className="external-ids__item"
                            data-source={extId.source.toLowerCase()}
                        >
                            <LinkSimpleIcon weight="bold" className="external-ids__service-icon" aria-hidden="true" />
                            <span className="external-ids__source">{extId.source}</span>
                            <span className="external-ids__value">{extId.id}</span>
                        </span>
                    );
                })}
            </div>
        );
    },
);

ExternalIds.displayName = 'ExternalIds';

export interface ExternalIdDetailsProps {
    ids: ExternalId[];
}

export function ExternalIdDetails({ ids }: ExternalIdDetailsProps) {
    return (
        <DetailsPanel
            title="External IDs"
            data-component="external-id-details"
            rows={ids.map((extId, index) => {
                const linkUrl = safeHttpUrl(extId.url);
                return {
                    key: `${extId.source}:${extId.id}:${index}`,
                    label: extId.source,
                    value: linkUrl ? (
                        <a
                            className="external-id-details__value external-id-details__value--linked"
                            href={linkUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            aria-label={`${extId.source} ${extId.id}`}
                            data-source={extId.source.toLowerCase()}
                        >
                            {extId.id}
                            <ArrowSquareOutIcon weight="bold" aria-hidden="true" />
                        </a>
                    ) : (
                        <span className="external-id-details__value" data-source={extId.source.toLowerCase()}>
                            {extId.id}
                        </span>
                    ),
                };
            })}
        />
    );
}

ExternalIdDetails.displayName = 'ExternalIdDetails';

export default ExternalIds;
