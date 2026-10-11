import { type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { CheckSquareIcon, SquareIcon, XIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { SelectAllBanner } from '@/components/ui/SelectAllBanner';
import type { BulkSelectionState } from '@/types/bulkSelection';

import '@/styles/bulk-actions-toolbar.css';

export interface BulkActionsToolbarProps extends ComponentPropsWithoutRef<'div'> {
    selection: BulkSelectionState;

    label: string;

    itemNounPlural: string;

    displaySelectedCount?: number;

    onClear?: () => void;

    /** Whether a narrow viewport shows a `MobileSelectionBar` instead, which hides this toolbar. */
    desktopOnly?: boolean;

    /** Whether the toolbar shows. */
    visible?: boolean;

    /** Whether the toolbar offers Select all. */
    selectAll?: boolean;

    children: ReactNode;
}

/** The desktop bulk-actions bar: the selected count, a clear button, the caller's actions, and the across-pages banner. */
export function BulkActionsToolbar({
    selection,
    label,
    itemNounPlural,
    displaySelectedCount = selection.selectedCount,
    onClear = selection.clear,
    desktopOnly = true,
    visible = selection.hasSelection,
    selectAll = false,
    children,
    className,
    ...props
}: BulkActionsToolbarProps) {
    const allChecked = selection.headerChecked === true;
    const classes = [
        'bulk-actions-toolbar-presence',
        desktopOnly && 'bulk-actions-toolbar-presence--desktop',
        className,
    ]
        .filter(Boolean)
        .join(' ');

    return (
        <div
            className={classes}
            data-component="bulk-actions-toolbar"
            data-visible={visible ? 'true' : 'false'}
            aria-hidden={!visible}
            inert={!visible}
            {...props}
        >
            <div className="bulk-actions-toolbar-presence__inner">
                {visible && (
                    <div className="bulk-actions-toolbar" role="region" aria-label={label}>
                        {(selection.showAcrossPagesPrompt || selection.allPages) && (
                            <SelectAllBanner
                                allPages={selection.allPages}
                                pageSelectedCount={selection.pageSelectedCount}
                                totalCount={selection.totalCount}
                                selectedCount={selection.selectedCount}
                                onSelectAllAcrossPages={selection.selectAllAcrossPages}
                                onClear={onClear}
                                itemNounPlural={itemNounPlural}
                            />
                        )}
                        <div className="bulk-actions-toolbar__info">
                            <span className="bulk-actions-toolbar__count">{displaySelectedCount} selected</span>
                            <button
                                type="button"
                                className="bulk-actions-toolbar__clear"
                                onClick={onClear}
                                disabled={!selection.hasSelection}
                                aria-label="Clear selection"
                                data-action="clear-selection"
                            >
                                <XIcon weight="bold" aria-hidden="true" />
                                Clear
                            </button>
                            {selectAll && (
                                <button
                                    type="button"
                                    className="bulk-actions-toolbar__select-all"
                                    onClick={() => selection.toggleHeader(!allChecked)}
                                    aria-pressed={allChecked}
                                    data-action="select-all-page"
                                >
                                    {allChecked ? (
                                        <CheckSquareIcon weight="fill" aria-hidden="true" />
                                    ) : (
                                        <SquareIcon weight="bold" aria-hidden="true" />
                                    )}
                                    Select all
                                </button>
                            )}
                        </div>
                        <div className="bulk-actions-toolbar__buttons">{children}</div>
                    </div>
                )}
            </div>
        </div>
    );
}

BulkActionsToolbar.displayName = 'BulkActionsToolbar';

export interface SelectModeToggleProps {
    active: boolean;

    onToggle: () => void;

    /** Whether the toggle belongs to the narrow-viewport layout only. */
    mobileOnly?: boolean;

    disabled?: boolean;

    /** The text of the button. */
    label?: string;

    className?: string;
}

/**
 * The button that enters and leaves selection mode. It keeps one name and one width in both states, so a toolbar
 * does not reflow when selection mode changes. The pressed state shows that selection mode is on.
 */
export function SelectModeToggle({
    active,
    onToggle,
    mobileOnly = true,
    disabled,
    label = 'Select',
    className,
}: SelectModeToggleProps) {
    const button = (
        <Button
            variant={active ? 'primary' : 'default'}
            size="sm"
            className={className}
            onClick={onToggle}
            disabled={disabled}
            aria-pressed={active}
            data-action={active ? 'exit-select-mode' : 'enter-select-mode'}
        >
            {active ? <XIcon weight="bold" aria-hidden="true" /> : <CheckSquareIcon weight="bold" aria-hidden="true" />}
            {label}
        </Button>
    );

    if (!mobileOnly) {
        return button;
    }

    return <div className="mobile-select-toggle">{button}</div>;
}

SelectModeToggle.displayName = 'SelectModeToggle';
