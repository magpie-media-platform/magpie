import { Link } from 'react-router-dom';
import { Skeleton } from '../Skeleton';

import './StatusRow.css';

interface StatusRowProps {
    label?: string;
    status?: string;
    pulse?: boolean;
    count?: number;
    url?: string;
    loading?: boolean;
}

export function StatusRow({ label = '', status, pulse, count = 0, url, loading }: StatusRowProps) {
    const content = loading ? (
        <Skeleton width="100%" height={20} />
    ) : (
        <>
            <span className="status-row__indicator" aria-hidden="true" />
            <span className="status-row__label">{label}</span>
            <span className="status-row__count">{count}</span>
        </>
    );
    const className = `status-row${pulse ? ' status-row--pulse' : ''}`;
    return url && count > 0 && !loading ? (
        <Link
            to={url}
            className={className}
            aria-label={`View ${count} ${label.toLowerCase()} items`}
            data-component="status-row"
            data-status={status}
            data-clickable
        >
            {content}
        </Link>
    ) : (
        <div className={className} data-component="status-row" data-status={status}>
            {content}
        </div>
    );
}
