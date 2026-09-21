/** The selection state the bulk-selection UI reads. */
export interface BulkSelectionState {
    allPages: boolean;
    hasSelection: boolean;
    headerChecked: boolean | 'indeterminate';
    selectedCount: number;
    pageSelectedCount: number;
    totalCount: number;
    showAcrossPagesPrompt: boolean;
    isRowChecked: (id: string) => boolean;
    toggleRow: (id: string, checked: boolean) => void;
    toggleHeader: (checked: boolean) => void;
    selectAllAcrossPages: () => void;
    clear: () => void;
}
