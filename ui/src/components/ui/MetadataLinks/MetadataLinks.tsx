import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { ArrowSquareOutIcon } from '@phosphor-icons/react';

import { CollapsibleChipList, type CollapsibleChipItem } from '@/components/ui/CollapsibleChipList';
import type { ContentReference } from '@/lib/metadata';
import { safeHttpUrl } from '@/lib/safeUrl';

import './MetadataLinks.css';

const DEFAULT_INITIAL_VISIBLE = 10;

export interface MetadataLinksProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    references: ContentReference[];

    label?: string | null;

    initialVisible?: number;
}

export const MetadataLinks = forwardRef<HTMLDivElement, MetadataLinksProps>(
    ({ references, label = 'Links', initialVisible = DEFAULT_INITIAL_VISIBLE, className, ...props }, ref) => {
        if (references.length === 0) {
            return null;
        }

        const classes = ['metadata-links', className].filter(Boolean).join(' ');

        const items: CollapsibleChipItem[] = references.map((reference) => {
            const rawBadge = reference.source || reference.kind;
            const displayName = reference.displayName?.trim();
            const badgeKey = rawBadge.trim().toLowerCase();
            const displayNameKey = displayName?.toLowerCase();
            const showName = Boolean(displayName && displayNameKey !== badgeKey);
            const linkUrl = safeHttpUrl(reference.url);
            const content = (
                <>
                    <span className="metadata-links__badge">{rawBadge}</span>
                    {showName && <span className="metadata-links__name">{displayName}</span>}
                </>
            );
            const testId = `metadata-link-${(reference.source || reference.kind).toLowerCase()}`;
            return {
                key: reference.url,
                node: linkUrl ? (
                    <a
                        className="metadata-links__link"
                        href={linkUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        data-source={reference.source.toLowerCase()}
                        data-kind={reference.kind.toLowerCase()}
                        data-testid={testId}
                    >
                        {content}
                        <ArrowSquareOutIcon weight="bold" aria-hidden="true" />
                    </a>
                ) : (
                    <span
                        className="metadata-links__link metadata-links__link--plain"
                        data-source={reference.source.toLowerCase()}
                        data-kind={reference.kind.toLowerCase()}
                        data-testid={testId}
                    >
                        {content}
                    </span>
                ),
            };
        });

        return (
            <div ref={ref} className={classes} data-component="metadata-links" {...props}>
                {label && <span className="metadata-links__label">{label}</span>}
                <CollapsibleChipList
                    items={items}
                    initialVisible={initialVisible}
                    listClassName="metadata-links__list"
                />
            </div>
        );
    },
);

MetadataLinks.displayName = 'MetadataLinks';

export default MetadataLinks;
