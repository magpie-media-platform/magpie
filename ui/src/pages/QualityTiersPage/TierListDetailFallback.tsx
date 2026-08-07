import { type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeftIcon, SlidersHorizontalIcon } from '@phosphor-icons/react';

import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { Button } from '@/components/ui/Button';
import { PullToRefresh } from '@/components/ui/PullToRefresh';

interface TierListDetailFallbackProps {
    breadcrumbs: BreadcrumbItem[];
    onRefresh: () => Promise<void>;
    icon: ReactNode;
    title: string;
    message: string;
    onRetry?: () => void;
}

export function TierListDetailFallback({
    breadcrumbs,
    onRefresh,
    icon,
    title,
    message,
    onRetry,
}: TierListDetailFallbackProps) {
    const backLink = (
        <Link to="/admin/quality-tiers" className="tier-list-detail-page__back-link">
            <ArrowLeftIcon weight="bold" aria-hidden="true" />
            Back to Quality Tiers
        </Link>
    );

    return (
        <div className="tier-list-detail-page" data-page="tier-list-detail">
            <PullToRefresh onRefresh={onRefresh} className="tier-list-detail-page__pull-to-refresh">
                <Breadcrumb items={breadcrumbs} className="tier-list-detail-page__breadcrumb" />
                <header className="tier-list-detail-page__header" data-component="tier-list-detail-header">
                    <div className="tier-list-detail-page__header-main">
                        <SlidersHorizontalIcon
                            weight="duotone"
                            className="tier-list-detail-page__icon"
                            aria-hidden="true"
                        />
                        <div className="tier-list-detail-page__title-row">
                            <h1 className="tier-list-detail-page__title">Tier List</h1>
                        </div>
                    </div>
                </header>
                <div className="tier-list-detail-page__not-found">
                    {icon}
                    <h2 className="tier-list-detail-page__not-found-title">{title}</h2>
                    <p>{message}</p>
                    {onRetry ? (
                        <div className="tier-list-detail-page__error-actions">
                            <Button variant="secondary" size="sm" onClick={onRetry}>
                                Retry
                            </Button>
                            {backLink}
                        </div>
                    ) : (
                        backLink
                    )}
                </div>
            </PullToRefresh>
        </div>
    );
}

TierListDetailFallback.displayName = 'TierListDetailFallback';
