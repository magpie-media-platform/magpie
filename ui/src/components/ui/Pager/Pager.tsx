import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { CaretDoubleLeftIcon, CaretDoubleRightIcon, CaretLeftIcon, CaretRightIcon } from '@phosphor-icons/react';

import './Pager.css';

export interface PagerProps extends Omit<ComponentPropsWithoutRef<'nav'>, 'onChange'> {
    page: number;

    pageCount?: number;

    totalItems?: number;

    onFirst: () => void;

    onPrevious: () => void;

    onNext: () => void;

    onLast: () => void;

    disabled?: boolean;

    canPrevious?: boolean;

    canNext?: boolean;
}

export const Pager = forwardRef<HTMLElement, PagerProps>(
    (
        {
            page,
            pageCount,
            totalItems,
            onFirst,
            onPrevious,
            onNext,
            onLast,
            disabled = false,
            canPrevious,
            canNext,
            className,
            ...props
        },
        ref,
    ) => {
        const prevEnabled = !disabled && (canPrevious ?? page > 1);
        const nextEnabled = !disabled && (canNext ?? (pageCount !== undefined && page < pageCount));
        const lastEnabled = nextEnabled && pageCount !== undefined;

        const classes = ['pager', className].filter(Boolean).join(' ');

        return (
            <nav ref={ref} className={classes} data-component="pager" aria-label="Pagination" {...props}>
                <button
                    type="button"
                    className="pager__button"
                    onClick={onFirst}
                    disabled={!prevEnabled}
                    aria-label="Go to first page"
                    data-action="first-page"
                >
                    <CaretDoubleLeftIcon weight="bold" aria-hidden="true" />
                </button>
                <button
                    type="button"
                    className="pager__button"
                    onClick={onPrevious}
                    disabled={!prevEnabled}
                    aria-label="Go to previous page"
                    data-action="prev-page"
                >
                    <CaretLeftIcon weight="bold" aria-hidden="true" />
                </button>
                <span className="pager__page" aria-live="polite">
                    {pageCount !== undefined ? `Page ${page} of ${pageCount}` : `Page ${page}`}
                </span>
                <button
                    type="button"
                    className="pager__button"
                    onClick={onNext}
                    disabled={!nextEnabled}
                    aria-label="Go to next page"
                    data-action="next-page"
                >
                    <CaretRightIcon weight="bold" aria-hidden="true" />
                </button>
                <button
                    type="button"
                    className="pager__button"
                    onClick={onLast}
                    disabled={!lastEnabled}
                    aria-label="Go to last page"
                    data-action="last-page"
                >
                    <CaretDoubleRightIcon weight="bold" aria-hidden="true" />
                </button>
                {totalItems !== undefined && (
                    <span className="pager__results">{`${totalItems} ${totalItems === 1 ? 'result' : 'results'}`}</span>
                )}
            </nav>
        );
    },
);

Pager.displayName = 'Pager';

export default Pager;
