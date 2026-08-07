import { forwardRef, type ComponentPropsWithoutRef } from 'react';

import { Badge } from '@/components/ui/Badge';
import { PreviewPlayer, type PreviewClip } from '@/components/ui/PreviewPlayer';

import './ContentTabs.css';

export interface PreviewsTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    previews: PreviewClip[];

    posterUrl?: string;
}

function kindLabel(kind?: string | null): string | undefined {
    const trimmed = kind?.trim();
    if (!trimmed) {
        return undefined;
    }
    return trimmed.charAt(0).toUpperCase() + trimmed.slice(1).toLowerCase();
}

export const PreviewsTab = forwardRef<HTMLDivElement, PreviewsTabProps>(
    ({ previews, posterUrl, className, ...props }, ref) => {
        const classes = ['previews-tab', className].filter(Boolean).join(' ');

        if (previews.length === 0) {
            return (
                <div ref={ref} className={classes} data-component="previews-tab" {...props}>
                    <div className="previews-tab__empty">
                        <p>No previews available for this item yet.</p>
                    </div>
                </div>
            );
        }

        return (
            <div ref={ref} className={classes} data-component="previews-tab" {...props}>
                {previews.map((preview, index) => {
                    const label = kindLabel(preview.kind);
                    return (
                        <div className="previews-tab__item" key={`${preview.url}-${index}`}>
                            <PreviewPlayer preview={preview} posterUrl={posterUrl} />
                            <div className="previews-tab__caption">
                                {preview.title && <span className="previews-tab__title">{preview.title}</span>}
                                {label && (
                                    <Badge size="sm" variant="default">
                                        {label}
                                    </Badge>
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>
        );
    },
);

PreviewsTab.displayName = 'PreviewsTab';

export default PreviewsTab;
