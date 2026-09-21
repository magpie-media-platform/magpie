import { type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { CheckSquareIcon, SquareIcon, XIcon } from '@phosphor-icons/react';

import type { BulkSelectionState } from '@/types/bulkSelection';
import { SelectAllBanner } from '../SelectAllBanner';

import './MobileSelectionBar.css';

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
    const allPageChecked = selection.headerChecked === true;
    const classes = ['mobile-selection-bar', className].filter(Boolean).join(' ');
    const actionClasses = ['mobile-selection-bar__actions', actionsClassName].filter(Boolean).join(' ');

    return (
        <div className={classes} role="region" aria-label="Selection actions" {...props}>
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
