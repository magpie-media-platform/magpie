import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { ArrowSquareOut, LinkSimple } from '@phosphor-icons/react';

import './ExternalIds.css';

export interface ExternalId {
    source: string;

    id: string;

    url?: string;
}

const SERVICE_COLORS: Record<string, string> = {
    imdb: '#f5c518',
    tmdb: '#01b4e4',
    tvdb: '#6cd491',
    trakt: '#ed1d24',
    tvmaze: '#3c948b',
    anidb: '#2a4766',
    myanimelist: '#2e51a2',
    musicbrainz: '#ba478f',
    discogs: '#333333',
};

function getServiceColor(source: string): string | null {
    const key = source.toLowerCase().replace(/[\s.-]/g, '');
    return SERVICE_COLORS[key] ?? null;
}

export interface ExternalIdsProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    ids: ExternalId[];
}

export const ExternalIds = forwardRef<HTMLDivElement, ExternalIdsProps>(({ ids, className, ...props }, ref) => {
    const classes = ['external-ids', className].filter(Boolean).join(' ');

    if (ids.length === 0) {
        return null;
    }

    return (
        <div ref={ref} className={classes} data-component="external-ids" {...props}>
            {ids.map((extId) => {
                const serviceColor = getServiceColor(extId.source);
                const colorStyle = serviceColor
                    ? ({ '--external-id-accent': serviceColor } as React.CSSProperties)
                    : undefined;

                if (extId.url) {
                    return (
                        <a
                            key={`${extId.source}-${extId.id}`}
                            href={extId.url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className={[
                                'external-ids__item',
                                'external-ids__item--linked',
                                serviceColor ? 'external-ids__item--colored' : '',
                            ]
                                .filter(Boolean)
                                .join(' ')}
                            style={colorStyle}
                            data-source={extId.source.toLowerCase()}
                            data-linked
                            data-colored={serviceColor ? true : undefined}
                        >
                            <LinkSimple weight="bold" className="external-ids__service-icon" aria-hidden="true" />
                            <span className="external-ids__source">{extId.source}</span>
                            <span className="external-ids__value">{extId.id}</span>
                            <ArrowSquareOut weight="bold" className="external-ids__link-icon" aria-hidden="true" />
                        </a>
                    );
                }

                return (
                    <span
                        key={`${extId.source}-${extId.id}`}
                        className={['external-ids__item', serviceColor ? 'external-ids__item--colored' : '']
                            .filter(Boolean)
                            .join(' ')}
                        style={colorStyle}
                        data-source={extId.source.toLowerCase()}
                        data-colored={serviceColor ? true : undefined}
                    >
                        <LinkSimple weight="bold" className="external-ids__service-icon" aria-hidden="true" />
                        <span className="external-ids__source">{extId.source}</span>
                        <span className="external-ids__value">{extId.id}</span>
                    </span>
                );
            })}
        </div>
    );
});

ExternalIds.displayName = 'ExternalIds';

export default ExternalIds;
