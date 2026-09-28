import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';
import { Link, type To } from 'react-router-dom';
import { CaretRightIcon, ArrowLeftIcon, HouseIcon } from '@phosphor-icons/react';

import { Skeleton } from '@/components/ui/Skeleton';

import './Breadcrumb.css';

export interface BreadcrumbItem {
    id: string;

    label: string;

    to: To;

    icon?: ReactNode;

    loading?: boolean;
}

export interface BreadcrumbProps extends HTMLAttributes<HTMLElement> {
    items: BreadcrumbItem[];

    showHomeIcon?: boolean;

    maxItems?: number;

    separator?: ReactNode;
}

export const Breadcrumb = forwardRef<HTMLElement, BreadcrumbProps>(
    ({ items, showHomeIcon = false, maxItems = 0, separator, className, ...props }, ref) => {
        const classes = ['breadcrumb', className].filter(Boolean).join(' ');

        const visibleItems = maxItems > 0 && items.length > maxItems ? items.slice(-maxItems) : items;

        const hasCollapsedItems = maxItems > 0 && items.length > maxItems;

        const parentItem = items.length > 1 ? items[items.length - 2] : null;
        const currentItem = items.length > 0 ? items[items.length - 1] : null;

        const separatorElement = separator ?? (
            <CaretRightIcon weight="bold" className="breadcrumb__separator" aria-hidden="true" />
        );

        if (items.length === 0) {
            return null;
        }

        return (
            <nav ref={ref} className={classes} aria-label="Breadcrumb" data-component="breadcrumb" {...props}>
                <ol className="breadcrumb__list" role="list">
                    {hasCollapsedItems && (
                        <li className="breadcrumb__item">
                            <span className="breadcrumb__ellipsis" aria-hidden="true">
                                ...
                            </span>
                            {separatorElement}
                        </li>
                    )}

                    {visibleItems.map((item, index) => {
                        const isLast = index === visibleItems.length - 1;
                        const isFirst = index === 0 && !hasCollapsedItems;

                        return (
                            <li key={item.id} className="breadcrumb__item">
                                {!isLast ? (
                                    <>
                                        <Link to={item.to} className="breadcrumb__link">
                                            {showHomeIcon && isFirst && (
                                                <HouseIcon
                                                    weight="bold"
                                                    className="breadcrumb__icon"
                                                    aria-hidden="true"
                                                />
                                            )}
                                            {item.icon && (
                                                <span className="breadcrumb__icon" aria-hidden="true">
                                                    {item.icon}
                                                </span>
                                            )}
                                            <span className="breadcrumb__label">
                                                {item.loading ? <Skeleton variant="text" width="6em" /> : item.label}
                                            </span>
                                        </Link>
                                        {separatorElement}
                                    </>
                                ) : (
                                    <span className="breadcrumb__current" aria-current="page">
                                        {item.icon && (
                                            <span className="breadcrumb__icon" aria-hidden="true">
                                                {item.icon}
                                            </span>
                                        )}
                                        <span className="breadcrumb__label">
                                            {item.loading ? <Skeleton variant="text" width="6em" /> : item.label}
                                        </span>
                                    </span>
                                )}
                            </li>
                        );
                    })}
                </ol>

                {parentItem && (
                    <Link to={parentItem.to} className="breadcrumb__back-link">
                        <ArrowLeftIcon weight="bold" className="breadcrumb__back-icon" aria-hidden="true" />
                        <span className="breadcrumb__back-label">
                            {parentItem.loading ? <Skeleton variant="text" width="6em" /> : parentItem.label}
                        </span>
                    </Link>
                )}

                {!parentItem && currentItem && (
                    <span className="breadcrumb__back-link breadcrumb__back-link--no-link">
                        <span className="breadcrumb__back-label">
                            {currentItem.loading ? <Skeleton variant="text" width="6em" /> : currentItem.label}
                        </span>
                    </span>
                )}
            </nav>
        );
    },
);

Breadcrumb.displayName = 'Breadcrumb';

export default Breadcrumb;
