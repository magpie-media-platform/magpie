import { type ReactNode } from 'react';
import { Link } from 'react-router-dom';

import { Card } from '@/components/ui/Card';
import { Skeleton } from '@/components/ui/Skeleton';

import './LibraryCountCard.css';

interface LibraryCountCardProps {
    to: string;
    icon: ReactNode;
    name: string;
    countLabel: string | undefined;
    dataComponent: string;
    iconTestId: string;
    skeletonTestId: string;
    linkDataAttributes?: Record<string, string>;
}

export function LibraryCountCard({
    to,
    icon,
    name,
    countLabel,
    dataComponent,
    iconTestId,
    skeletonTestId,
    linkDataAttributes,
}: LibraryCountCardProps) {
    return (
        <Link to={to} className="library-count-card" data-component={dataComponent} {...linkDataAttributes}>
            <Card interactive className="library-count-card__card">
                <div className="library-count-card__content">
                    <span className="library-count-card__icon" aria-hidden="true" data-testid={iconTestId}>
                        {icon}
                    </span>
                    <div className="library-count-card__info">
                        <span className="library-count-card__name">{name}</span>
                        {countLabel !== undefined ? (
                            <span className="library-count-card__count">{countLabel}</span>
                        ) : (
                            <Skeleton className="library-count-card__skeleton" data-testid={skeletonTestId} />
                        )}
                    </div>
                </div>
            </Card>
        </Link>
    );
}

LibraryCountCard.displayName = 'LibraryCountCard';
