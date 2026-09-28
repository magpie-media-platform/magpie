import type { ReactNode } from 'react';

import { safeHttpUrl } from '@/lib/safeUrl';
import type { DetailRow } from '@/types/details';

import './DetailsPanel.css';

export type { DetailRow };

type DetailsPanelVariant = 'default' | 'dialog' | 'facts';

export interface DetailsPanelProps {
    rows: DetailRow[];
    title?: string;
    icon?: ReactNode;
    variant?: DetailsPanelVariant;
    'aria-label'?: string;
    'data-component'?: string;
}

function renderValue(value: ReactNode): ReactNode {
    if (typeof value !== 'string') {
        return value;
    }

    const url = safeHttpUrl(value);
    if (!url) {
        return value;
    }

    return (
        <a href={url} target="_blank" rel="noopener noreferrer" className="details-panel__link">
            {value}
        </a>
    );
}

export function DetailsPanel({
    rows,
    title,
    icon,
    variant = 'default',
    'aria-label': ariaLabel,
    'data-component': dataComponent,
}: DetailsPanelProps) {
    if (rows.length === 0) {
        return null;
    }

    const isDialog = variant === 'dialog';
    const classes = variant === 'default' ? 'details-panel' : `details-panel details-panel--${variant}`;

    return (
        <section className={classes} aria-label={ariaLabel} data-component={dataComponent}>
            {title && (
                <h2 className="details-panel__title">
                    {icon}
                    {title}
                </h2>
            )}
            <dl className="details-panel__list">
                {rows.map((row, index) => (
                    <div key={row.key ?? index} className="details-panel__row">
                        <dt className="details-panel__label">{row.label}</dt>
                        <dd
                            className={[
                                'details-panel__value',
                                row.mono && 'details-panel__value--mono',
                                row.multiline && 'details-panel__value--multiline',
                            ]
                                .filter(Boolean)
                                .join(' ')}
                            data-detail={isDialog && typeof row.label === 'string' ? row.label : undefined}
                            title={isDialog && typeof row.value === 'string' ? row.value : undefined}
                        >
                            {isDialog ? renderValue(row.value) : row.value}
                        </dd>
                    </div>
                ))}
            </dl>
        </section>
    );
}

DetailsPanel.displayName = 'DetailsPanel';

export default DetailsPanel;
