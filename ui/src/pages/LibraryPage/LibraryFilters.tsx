import { FunnelIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { FilterBar } from '@/components/filters/FilterBar';

import type { SavedFilterReference } from './useLibraryPage';

interface LibraryFiltersProps {
    filterQuery: string;
    isRestoringFilter?: boolean;
    filterRestoreError: Error | null;
    onRetrySavedFilter: () => void;
    onClearFilters: () => void;
    onFilterQueryChange: (query: string, immediate?: boolean) => void;
    showSaveControl: boolean;
    saveControlLabel: string;
    onOpenSaveFilter: () => void;
    isSavingFilter: boolean;
    filterBarExpanded: boolean | undefined;
    onFilterBarExpandedChange: (expanded: boolean) => void;
    tableLayoutQuery: string;
    defaultContentType: string | undefined;
    restrictContentType: string | undefined;
    lockContentLevel: string | undefined;
    referencedSavedFilter: SavedFilterReference | null;
    isAppliedFilterDirty: boolean;
}

export function LibraryFilters({
    filterQuery,
    isRestoringFilter = false,
    filterRestoreError,
    onRetrySavedFilter,
    onClearFilters,
    onFilterQueryChange,
    showSaveControl,
    saveControlLabel,
    onOpenSaveFilter,
    isSavingFilter,
    filterBarExpanded,
    onFilterBarExpandedChange,
    tableLayoutQuery,
    defaultContentType,
    restrictContentType,
    lockContentLevel,
    referencedSavedFilter,
    isAppliedFilterDirty,
}: LibraryFiltersProps) {
    return (
        <section className="library-page__controls" aria-label="Filters and sorting">
            <div className="library-page__filters">
                {filterRestoreError ? (
                    <Alert variant="error" title="Could not load saved filter">
                        <p>Your saved filter is still selected. Retry, or clear it to show the library.</p>
                        <div className="library-page__filter-recovery-actions">
                            <Button variant="secondary" onClick={onRetrySavedFilter}>
                                Retry saved filter
                            </Button>
                            <Button variant="default" onClick={onClearFilters}>
                                Clear saved filter
                            </Button>
                        </div>
                    </Alert>
                ) : isRestoringFilter ? (
                    <p role="status">Loading saved filter…</p>
                ) : null}
                <FilterBar
                    value={filterQuery}
                    loading={isRestoringFilter && !filterRestoreError}
                    readOnly={isRestoringFilter}
                    onChange={onFilterQueryChange}
                    onImmediateChange={(query) => onFilterQueryChange(query, true)}
                    context="content"
                    localContentPage
                    showSave={showSaveControl}
                    saveLabel={saveControlLabel}
                    onSave={onOpenSaveFilter}
                    isSaving={isSavingFilter}
                    expanded={filterBarExpanded}
                    onExpandedChange={onFilterBarExpandedChange}
                    tableLayoutQuery={tableLayoutQuery}
                    defaultContentType={defaultContentType}
                    restrictContentType={restrictContentType}
                    lockContentLevel={lockContentLevel}
                />

                {referencedSavedFilter && (
                    <div
                        className="library-page__saved-filter"
                        data-testid="library-page__applied-saved-filter"
                        data-filter-id={referencedSavedFilter.id}
                        data-dirty={isAppliedFilterDirty || undefined}
                    >
                        <FunnelIcon weight="duotone" aria-hidden="true" />
                        <span className="library-page__saved-filter-label">
                            Saved filter: <strong>{referencedSavedFilter.name}</strong>
                        </span>
                        {isAppliedFilterDirty && (
                            <Badge
                                variant="warning"
                                size="sm"
                                badgeStyle="outline"
                                data-testid="library-page__saved-filter-modified"
                            >
                                Modified
                            </Badge>
                        )}
                    </div>
                )}
            </div>
        </section>
    );
}

LibraryFilters.displayName = 'LibraryFilters';

export default LibraryFilters;
