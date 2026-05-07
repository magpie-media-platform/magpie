import { useState, useCallback, forwardRef, type ComponentPropsWithoutRef } from 'react';
import { Funnel, CaretDown, CaretUp, FloppyDisk, X } from '@phosphor-icons/react';

import { Button } from '../Button';
import { Badge } from '../Badge';
import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';
import { SimpleFilterBuilder, isSimpleQuery } from '../SimpleFilterBuilder';
import { FilterQueryEditor } from '@/editor/FilterQueryEditor';

import './FilterBar.css';
import '@/styles/mode-switch.css';

export type FilterMode = 'simple' | 'advanced';

export interface SavedFilterOption {
    id: string;
    name: string;
    query: string;
}

const SAVED_FILTER_NONE = '__none__';

export interface FilterBarProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    value: string;

    onChange: (value: string) => void;

    context?: 'content' | 'subject';

    loading?: boolean;

    onSave?: () => void;

    saveLabel?: string;

    isSaving?: boolean;

    showSave?: boolean;

    defaultExpanded?: boolean;

    expanded?: boolean;

    onExpandedChange?: (expanded: boolean) => void;

    savedFilters?: SavedFilterOption[];
}

export const FilterBar = forwardRef<HTMLDivElement, FilterBarProps>(
    (
        {
            value,
            onChange,
            context = 'content',
            loading = false,
            onSave,
            saveLabel = 'Save Filter',
            isSaving = false,
            showSave = false,
            defaultExpanded = false,
            expanded: controlledExpanded,
            onExpandedChange,
            savedFilters,
            className,
            ...props
        },
        ref,
    ) => {
        const [internalExpanded, setInternalExpanded] = useState(defaultExpanded);
        const isControlled = controlledExpanded !== undefined;
        const isExpanded = isControlled ? controlledExpanded : internalExpanded;

        const canUseSimpleMode = isSimpleQuery(value, context);
        const [mode, setMode] = useState<FilterMode>(canUseSimpleMode ? 'simple' : 'advanced');

        const hasFilter = value.trim().length > 0;

        const handleExpandToggle = useCallback(() => {
            const newExpanded = !isExpanded;
            if (!isControlled) {
                setInternalExpanded(newExpanded);
            }
            onExpandedChange?.(newExpanded);
        }, [isExpanded, isControlled, onExpandedChange]);

        const handleClear = useCallback(() => {
            onChange('');
        }, [onChange]);

        const handleSavedFilterChange = useCallback(
            (value: string) => {
                if (value === SAVED_FILTER_NONE) {
                    return;
                }
                const filter = savedFilters?.find((f) => f.id === value);
                if (filter?.query) {
                    onChange(filter.query);

                    if (!isControlled) {
                        setInternalExpanded(true);
                    }
                    onExpandedChange?.(true);
                }
            },
            [savedFilters, onChange, isControlled, onExpandedChange],
        );

        const hasSavedFilters = (savedFilters?.length ?? 0) > 0;

        const handleModeChange = useCallback(
            (newMode: FilterMode) => {
                if (newMode === 'simple' && !canUseSimpleMode) {
                    return;
                }
                setMode(newMode);
            },
            [canUseSimpleMode],
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
            <div ref={ref} className={classes} data-component="filter-bar" data-expanded={isExpanded} {...props}>
                <div className="filter-bar__header">
                    <button
                        type="button"
                        className="filter-bar__toggle"
                        onClick={handleExpandToggle}
                        aria-expanded={isExpanded}
                        aria-controls="filter-bar-content"
                        data-action="toggle-filter"
                    >
                        <Funnel weight="duotone" className="filter-bar__icon" aria-hidden="true" />
                        <span className="filter-bar__label">Filters</span>
                        {hasFilter && (
                            <Badge variant="primary" size="sm" className="filter-bar__badge">
                                Active
                            </Badge>
                        )}
                        {isExpanded ? (
                            <CaretUp weight="bold" className="filter-bar__caret" aria-hidden="true" />
                        ) : (
                            <CaretDown weight="bold" className="filter-bar__caret" aria-hidden="true" />
                        )}
                    </button>

                    <div className="filter-bar__actions">
                        {hasSavedFilters && (
                            <Select value={SAVED_FILTER_NONE} onValueChange={handleSavedFilterChange}>
                                <SelectTrigger
                                    size="sm"
                                    aria-label="Apply saved filter"
                                    className="filter-bar__saved-trigger"
                                    data-action="apply-saved-filter"
                                >
                                    <Funnel weight="bold" aria-hidden="true" />
                                    <span className="filter-bar__btn-label">Saved</span>
                                </SelectTrigger>
                                <SelectContent>
                                    {savedFilters!.map((f) => (
                                        <SelectItem key={f.id} value={f.id}>
                                            {f.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        )}
                        {hasFilter && (
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={handleClear}
                                className="filter-bar__clear"
                                data-action="clear-filter"
                            >
                                <X weight="bold" aria-hidden="true" />
                                <span className="filter-bar__btn-label">Clear</span>
                            </Button>
                        )}
                        {showSave && hasFilter && onSave && (
                            <Button
                                variant="default"
                                size="sm"
                                onClick={onSave}
                                disabled={isSaving}
                                className="filter-bar__save"
                                data-action="save-filter"
                            >
                                <FloppyDisk weight="bold" aria-hidden="true" />
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
                            {mode === 'simple' ? (
                                <SimpleFilterBuilder
                                    value={value}
                                    onChange={onChange}
                                    context={context}
                                    loading={loading}
                                />
                            ) : (
                                <div className="filter-bar__editor-wrap">
                                    <FilterQueryEditor
                                        value={value}
                                        onChange={onChange}
                                        context={context}
                                        placeholder={
                                            context === 'content'
                                                ? 'e.g. genre has "Action" and monitoring = Active'
                                                : 'e.g. kind = "person" and name contains "Smith"'
                                        }
                                    />
                                    <p className="filter-bar__hint">
                                        Use fields like <code>title</code>, <code>genre</code>, <code>year</code>,{' '}
                                        <code>monitoring</code> with operators like <code>=</code>, <code>!=</code>,{' '}
                                        <code>contains</code>, <code>has</code>, <code>&gt;</code>, <code>&lt;</code>.
                                        Join with <code>and</code> or <code>or</code>.
                                    </p>
                                </div>
                            )}
                        </div>

                        {mode === 'simple' && hasFilter && (
                            <div className="filter-bar__query-preview">
                                <span className="filter-bar__query-label">Query:</span>
                                <code className="filter-bar__query-text">{value}</code>
                            </div>
                        )}

                        <div className="mode-switch">
                            {mode === 'simple' ? (
                                <button
                                    type="button"
                                    className="mode-switch__link"
                                    onClick={() => handleModeChange('advanced')}
                                    data-action="switch-to-advanced"
                                >
                                    Switch to advanced editor
                                </button>
                            ) : (
                                <button
                                    type="button"
                                    className="mode-switch__link"
                                    onClick={() => handleModeChange('simple')}
                                    disabled={!canUseSimpleMode}
                                    title={
                                        !canUseSimpleMode ? 'Current query cannot be edited in simple mode' : undefined
                                    }
                                    data-action="switch-to-simple"
                                >
                                    Switch to simple mode
                                </button>
                            )}
                        </div>
                    </div>
                )}
            </div>
        );
    },
);

FilterBar.displayName = 'FilterBar';

export default FilterBar;
