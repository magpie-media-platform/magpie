import { type ReactNode } from 'react';
import { Link } from 'react-router-dom';

import { Card } from '@/components/ui/Card';
import { Skeleton } from '@/components/ui/Skeleton';

interface LibraryCountCardClassNames {
    link: string;
    card: string;
    content: string;
    icon: string;
    info: string;
    name: string;
    count: string;
    skeleton: string;
}

interface LibraryCountCardProps {
    to: string;
    icon: ReactNode;
    name: string;
    countLabel: string | undefined;
    dataComponent: string;
    classNames: LibraryCountCardClassNames;
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
    classNames,
    iconTestId,
    skeletonTestId,
    linkDataAttributes,
}: LibraryCountCardProps) {
    return (
        <Link to={to} className={classNames.link} data-component={dataComponent} {...linkDataAttributes}>
            <Card interactive className={classNames.card}>
                <div className={classNames.content}>
                    <span className={classNames.icon} aria-hidden="true" data-testid={iconTestId}>
                        {icon}
                    </span>
                    <div className={classNames.info}>
                        <span className={classNames.name}>{name}</span>
                        {countLabel !== undefined ? (
                            <span className={classNames.count}>{countLabel}</span>
                        ) : (
                            <Skeleton className={classNames.skeleton} data-testid={skeletonTestId} />
                        )}
                    </div>
                </div>
            </Card>
        </Link>
    );
}

LibraryCountCard.displayName = 'LibraryCountCard';
