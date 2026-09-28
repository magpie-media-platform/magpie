import { useLayoutEffect, useRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { CheckSquareIcon, SquareIcon, XIcon } from '@phosphor-icons/react';

import type { BulkSelectionState } from '@/types/bulkSelection';
import { SelectAllBanner } from '../SelectAllBanner';

import './MobileSelectionBar.css';

const mountedBars = new WeakMap<HTMLElement, Set<HTMLElement>>();

export type MobileSelectionState = BulkSelectionState;

export interface MobileSelectionBarProps extends ComponentPropsWithoutRef<'div'> {
    selection: MobileSelectionState;
    pageSelectedCount?: number;
    totalCount?: number;
    displaySelectedCount?: number;
    onClear?: () => void;
    itemNounPlural: string;
    actionsClassName?: string;
    children?: ReactNode;
}

export function MobileSelectionBar({
    selection,
    pageSelectedCount = selection.pageSelectedCount,
    totalCount = selection.totalCount,
    displaySelectedCount = selection.selectedCount,
    onClear = selection.clear,
    itemNounPlural,
    actionsClassName,
    children,
    className,
    ...props
}: MobileSelectionBarProps) {
    const barRef = useRef<HTMLDivElement>(null);
    const allPageChecked = selection.headerChecked === true;
    const classes = ['mobile-selection-bar', className].filter(Boolean).join(' ');
    const actionClasses = ['mobile-selection-bar__actions', actionsClassName].filter(Boolean).join(' ');

    useLayoutEffect(() => {
        const bar = barRef.current;
        const main = bar?.closest<HTMLElement>('[data-scroll-container]');
        if (!bar || !main) return;

        const bars = mountedBars.get(main) ?? new Set<HTMLElement>();
        bars.add(bar);
        mountedBars.set(main, bars);

        const updateClearance = () => {
            const currentBars = mountedBars.get(main);
            if (!currentBars) {
                main.style.removeProperty('--mobile-selection-bar-clearance');
                return;
            }

            const mainBottom = main.getBoundingClientRect().bottom;
            let clearance = 0;
            for (const currentBar of currentBars) {
                const barRect = currentBar.getBoundingClientRect();
                if (barRect.height > 0) clearance = Math.max(clearance, Math.ceil(mainBottom - barRect.top));
            }
            main.style.setProperty('--mobile-selection-bar-clearance', `${clearance}px`);
        };

        updateClearance();
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(updateClearance);
        observer?.observe(bar);
        observer?.observe(main);
        window.addEventListener('resize', updateClearance);
        window.visualViewport?.addEventListener('resize', updateClearance);

        return () => {
            observer?.disconnect();
            window.removeEventListener('resize', updateClearance);
            window.visualViewport?.removeEventListener('resize', updateClearance);
            bars.delete(bar);
            if (bars.size === 0) mountedBars.delete(main);
            updateClearance();
        };
    }, []);

    return (
        <div ref={barRef} className={classes} role="region" aria-label="Selection actions" {...props}>
            {(selection.showAcrossPagesPrompt || selection.allPages) && (
                <SelectAllBanner
                    allPages={selection.allPages}
                    pageSelectedCount={pageSelectedCount}
                    totalCount={totalCount}
                    selectedCount={selection.selectedCount}
                    onSelectAllAcrossPages={selection.selectAllAcrossPages}
                    onClear={selection.clear}
                    itemNounPlural={itemNounPlural}
                />
            )}
            <div className="mobile-selection-bar__head">
                <button
                    type="button"
                    className="mobile-selection-bar__clear"
                    onClick={onClear}
                    disabled={!selection.hasSelection}
                    aria-label="Clear selection"
                    data-action="clear-selection"
                >
                    <XIcon weight="bold" aria-hidden="true" />
                    Clear
                </button>
                <span className="mobile-selection-bar__count">{displaySelectedCount} selected</span>
                <button
                    type="button"
                    className="mobile-selection-bar__select-all"
                    onClick={() => selection.toggleHeader(!allPageChecked)}
                    aria-pressed={allPageChecked}
                    data-action="select-all-page"
                >
                    {allPageChecked ? (
                        <CheckSquareIcon weight="fill" aria-hidden="true" />
                    ) : (
                        <SquareIcon weight="bold" aria-hidden="true" />
                    )}
                    Select all
                </button>
            </div>
            {selection.hasSelection && children && <div className={actionClasses}>{children}</div>}
        </div>
    );
}

MobileSelectionBar.displayName = 'MobileSelectionBar';

export default MobileSelectionBar;
