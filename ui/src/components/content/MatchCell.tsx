import { Badge } from '@/components/ui/Badge';

import './MatchCell.css';

export interface MatchCellInfo {
    title: string;

    subtitle?: string | null;

    providerType: string;
}

type MatchCellProps =
    | { match: MatchCellInfo; emptyLabel?: never; emptyDetail?: never; testId?: string }
    | { match: null; emptyLabel: string; emptyDetail?: string | null; testId?: string };

export function MatchCell({ match, emptyLabel, emptyDetail, testId }: MatchCellProps) {
    if (!match) {
        return (
            <div className="match-cell match-cell--empty" data-testid={testId}>
                <span className="match-cell__dash">{emptyLabel}</span>
                {emptyDetail ? <span className="match-cell__detail">{emptyDetail}</span> : null}
            </div>
        );
    }

    return (
        <div className="match-cell" data-testid={testId}>
            <span className="match-cell__title">
                {match.title}
                {match.subtitle ? <span className="match-cell__subtitle"> {match.subtitle}</span> : null}
            </span>
            <Badge size="sm" variant="info" badgeStyle="outline">
                {match.providerType}
            </Badge>
        </div>
    );
}

MatchCell.displayName = 'MatchCell';

export default MatchCell;
