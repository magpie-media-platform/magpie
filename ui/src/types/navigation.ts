import type { ContentFilterCriteria } from './filters';

export interface FiltersPageCreateState {
    createQuery: string;
    createName?: string;
}

export interface LibraryAddPreloadState {
    filterQuery: string;
    contentType?: string;
    savedFilterId?: string;
}

export interface WishlistsPageCreateState {
    createCriteria: ContentFilterCriteria;
    createName?: string;
}
