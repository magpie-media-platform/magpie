import { useState, useCallback, forwardRef, type ComponentPropsWithoutRef, type KeyboardEvent } from 'react';
import {
    FunnelIcon,
    CaretDownIcon,
    CaretUpIcon,
    FloppyDiskIcon,
    XIcon,
    CodeIcon,
    SlidersIcon,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { SimpleFilterBuilder } from '@/components/filters/SimpleFilterBuilder';
import { FilterQueryEditor } from '@/editor/FilterQueryEditor';
import { useMediaQuery } from '@/hooks/ui/useMediaQuery';
import { isSimpleQuery } from '@/lib/simpleFilterQuery';

import './FilterBar.css';
import { LocalContentFilters } from './LocalContentFilters';

export type FilterMode = 'simple' | 'advanced';

export interface FilterBarProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange' | 'onSubmit'> {
    value: string;

    onChange: (value: string) => void;

    onImmediateChange?: (value: string) => void;

    onSubmit?: () => void;

    context?: 'content' | 'subject';

    loading?: boolean;

    onSave?: () => void;

    saveLabel?: string;

    isSaving?: boolean;

    showSave?: boolean;

    readOnly?: boolean;

    collapsible?: boolean;

    defaultExpanded?: boolean;

    tableLayoutQuery?: string;

    expanded?: boolean;

    onExpandedChange?: (expanded: boolean) => void;

    defaultContentType?: string;

    restrictContentType?: string;

    lockContentLevel?: string;

    defaultSubjectKind?: string;

    lockSubjectKind?: boolean;
    localContentPage?: boolean;
    includeAllContentLevels?: boolean;
    allowTitlePrefix?: boolean;
}

export const FilterBar = forwardRef<HTMLDivElement, FilterBarProps>(
    (
        {
            value,
            onChange,
            onImmediateChange,
            onSubmit,
            context = 'content',
            loading = false,
            onSave,
            saveLabel = 'Save Filter',
            isSaving = false,
            showSave = false,
            readOnly = false,
            collapsible = true,
            defaultExpanded,
            tableLayoutQuery,
            expanded: controlledExpanded,
            onExpandedChange,
            defaultContentType,
            restrictContentType,
            lockContentLevel,
            defaultSubjectKind,
            lockSubjectKind,
            localContentPage = false,
            includeAllContentLevels = false,
            allowTitlePrefix = false,
            className,
            children,
            onKeyDownCapture,
            ...props
        },
        ref,
    ) => {
        const usesTableLayout = useMediaQuery(tableLayoutQuery);
        const [explicitExpanded, setExplicitExpanded] = useState(defaultExpanded);
        const isControlled = controlledExpanded !== undefined;
        const isExpanded = !collapsible || (isControlled ? controlledExpanded : (explicitExpanded ?? usesTableLayout));

        const canUseSimpleMode = isSimpleQuery(value, context, { allowTitlePrefix });
        const [mode, setMode] = useState<FilterMode>(canUseSimpleMode ? 'simple' : 'advanced');

        const effectiveMode: FilterMode = canUseSimpleMode ? mode : 'advanced';

        const hasFilter = value.trim().length > 0;

        const handleExpandToggle = useCallback(() => {
            const newExpanded = !isExpanded;
            setExplicitExpanded(newExpanded);
            onExpandedChange?.(newExpanded);
        }, [isExpanded, onExpandedChange]);

        const handleClear = useCallback(() => {
            (onImmediateChange ?? onChange)('');
        }, [onChange, onImmediateChange]);

        const handleModeChange = useCallback(
            (newMode: FilterMode) => {
                if (newMode === 'simple' && !canUseSimpleMode) {
                    return;
                }
                setMode(newMode);
            },
            [canUseSimpleMode],
        );

        const submit = useCallback(() => {
            if (loading || readOnly) {
                return;
            }
            if (onSubmit) {
                onSubmit();
            } else {
                (onImmediateChange ?? onChange)(value);
            }
        }, [loading, onChange, onImmediateChange, onSubmit, readOnly, value]);

        const handleKeyDownCapture = useCallback(
            (event: KeyboardEvent<HTMLDivElement>) => {
                onKeyDownCapture?.(event);
                const target = event.target;
                if (
                    event.defaultPrevented ||
                    event.key !== 'Enter' ||
                    event.nativeEvent.isComposing ||
                    loading ||
                    readOnly ||
                    !(target instanceof HTMLElement) ||
                    target instanceof HTMLTextAreaElement ||
                    !target.closest('input, [contenteditable="true"], [role="textbox"]') ||
                    target.closest('[aria-multiline="true"]') ||
                    target.closest('[role="combobox"][aria-expanded="true"]') ||
                    event.currentTarget.querySelector('[role="listbox"]')
                ) {
                    return;
                }

                event.preventDefault();
                submit();
            },
            [loading, onKeyDownCapture, readOnly, submit],
        );

        const classes = [
            'filter-bar',
            isExpanded && 'filter-bar--expanded',
            hasFilter && 'filter-bar--active',
            className,
        ]
            .filter(Boolean)
            .join(' ');

        return (
            <div
                ref={ref}
                className={classes}
                data-component="filter-bar"
                data-expanded={isExpanded}
                onKeyDownCapture={handleKeyDownCapture}
                {...props}
            >
                <div className="filter-bar__header">
                    {collapsible ? (
                        <button
                            type="button"
                            className="filter-bar__toggle"
                            onClick={handleExpandToggle}
                            aria-expanded={isExpanded}
                            aria-controls="filter-bar-content"
                            data-action="toggle-filter"
                        >
                            <FunnelIcon weight="duotone" className="filter-bar__icon" aria-hidden="true" />
                            <span className="filter-bar__label">Filters</span>
                            {hasFilter && (
                                <Badge variant="primary" size="sm" className="filter-bar__badge">
                                    Active
                                </Badge>
                            )}
                            {isExpanded ? (
                                <CaretUpIcon weight="bold" className="filter-bar__caret" aria-hidden="true" />
                            ) : (
                                <CaretDownIcon weight="bold" className="filter-bar__caret" aria-hidden="true" />
                            )}
                        </button>
                    ) : (
                        <div className="filter-bar__heading" data-component="filter-bar-heading">
                            <FunnelIcon weight="duotone" className="filter-bar__icon" aria-hidden="true" />
                            <span className="filter-bar__label">Filters</span>
                            {hasFilter && (
                                <Badge variant="primary" size="sm" className="filter-bar__badge">
                                    Active
                                </Badge>
                            )}
                        </div>
                    )}

                    <div className="filter-bar__actions">
                        {isExpanded &&
                            (effectiveMode === 'simple' ? (
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => handleModeChange('advanced')}
                                    disabled={readOnly}
                                    className="filter-bar__mode-toggle"
                                    data-action="switch-to-advanced"
                                >
                                    <CodeIcon weight="bold" aria-hidden="true" />
                                    <span className="filter-bar__btn-label">Advanced filter</span>
                                </Button>
                            ) : (
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => handleModeChange('simple')}
                                    disabled={!canUseSimpleMode || readOnly}
                                    title={!canUseSimpleMode ? 'This query cannot be edited in simple mode' : undefined}
                                    className="filter-bar__mode-toggle"
                                    data-action="switch-to-simple"
                                >
                                    <SlidersIcon weight="bold" aria-hidden="true" />
                                    <span className="filter-bar__btn-label">Simple filter</span>
                                </Button>
                            ))}
                        {hasFilter && !readOnly && (
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={handleClear}
                                className="filter-bar__clear"
                                data-action="clear-filter"
                            >
                                <XIcon weight="bold" aria-hidden="true" />
                                <span className="filter-bar__btn-label">Clear</span>
                            </Button>
                        )}
                        {showSave && hasFilter && onSave && !readOnly && (
                            <Button
                                variant="default"
                                size="sm"
                                onClick={onSave}
                                disabled={isSaving}
                                className="filter-bar__save"
                                data-action="save-filter"
                            >
                                <FloppyDiskIcon weight="bold" aria-hidden="true" />
                                <span className="filter-bar__btn-label">{saveLabel}</span>
                            </Button>
                        )}
                    </div>
                </div>

                {isExpanded && (
                    <div
                        id="filter-bar-content"
                        className="filter-bar__content"
                        role="region"
                        aria-label="Filter options"
                    >
                        <div className="filter-bar__panel">
                            {effectiveMode === 'simple' ? (
                                <SimpleFilterBuilder
                                    value={value}
                                    onChange={onChange}
                                    context={context}
                                    loading={loading}
                                    readOnly={readOnly}
                                    defaultContentType={defaultContentType}
                                    restrictContentType={restrictContentType}
                                    lockContentLevel={lockContentLevel}
                                    includeAllContentLevels={includeAllContentLevels}
                                    defaultSubjectKind={defaultSubjectKind}
                                    lockSubjectKind={lockSubjectKind}
                                >
                                    {localContentPage && context === 'content' && (
                                        <LocalContentFilters
                                            value={value}
                                            onChange={onImmediateChange ?? onChange}
                                            disabled={readOnly || loading}
                                        />
                                    )}
                                </SimpleFilterBuilder>
                            ) : (
                                <div className="filter-bar__editor-wrap">
                                    <FilterQueryEditor
                                        value={value}
                                        onChange={onChange}
                                        onSubmit={submit}
                                        context={context}
                                        readOnly={readOnly}
                                        placeholder={
                                            context === 'content'
                                                ? 'genre has "Action" and monitoring != Unmonitored'
                                                : 'kind = "<kind>" and name contains "Smith"'
                                        }
                                    />
                                    {context === 'content' ? (
                                        <p className="filter-bar__hint">
                                            Filter by title, genre, year, or monitoring using =, !=, contains, has,{' '}
                                            {'>'}, or {'<'}. Combine filters with "and" or "or".
                                        </p>
                                    ) : (
                                        <p className="filter-bar__hint">
                                            Filter by kind, title, or tag using =, !=, contains, or has. Combine filters
                                            with "and" or "or".
                                        </p>
                                    )}
                                </div>
                            )}
                        </div>

                        {effectiveMode === 'simple' && hasFilter && (
                            <div className="filter-bar__query-preview">
                                <span className="filter-bar__query-label">Query:</span>
                                <code className="filter-bar__query-text">{value}</code>
                            </div>
                        )}

                        {children}
                    </div>
                )}
            </div>
        );
    },
);

FilterBar.displayName = 'FilterBar';

export default FilterBar;
