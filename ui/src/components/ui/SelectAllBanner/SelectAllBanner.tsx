import './SelectAllBanner.css';

export interface SelectAllBannerProps {
    allPages: boolean;

    pageSelectedCount: number;

    totalCount: number;

    selectedCount: number;

    onSelectAllAcrossPages: () => void;

    onClear: () => void;

    itemNounPlural?: string;
}

function formatCount(value: number): string {
    return value.toLocaleString();
}

export function SelectAllBanner({
    allPages,
    pageSelectedCount,
    totalCount,
    selectedCount,
    onSelectAllAcrossPages,
    onClear,
    itemNounPlural = 'items',
}: SelectAllBannerProps) {
    return (
        <div className="select-all-banner" role="status" data-component="select-all-banner">
            {allPages ? (
                <>
                    <span className="select-all-banner__text">
                        All {formatCount(selectedCount)} matching {itemNounPlural} across all pages are selected.
                    </span>
                    <button
                        type="button"
                        className="select-all-banner__action"
                        onClick={onClear}
                        data-action="clear-across-pages"
                    >
                        Clear selection
                    </button>
                </>
            ) : (
                <>
                    <span className="select-all-banner__text">
                        All {formatCount(pageSelectedCount)} on this page are selected.
                    </span>
                    <button
                        type="button"
                        className="select-all-banner__action"
                        onClick={onSelectAllAcrossPages}
                        data-action="select-all-across-pages"
                    >
                        Select all {formatCount(totalCount)} across all pages
                    </button>
                </>
            )}
        </div>
    );
}

SelectAllBanner.displayName = 'SelectAllBanner';

export default SelectAllBanner;
