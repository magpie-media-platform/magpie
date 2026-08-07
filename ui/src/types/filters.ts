export interface ContentFilterCriteria {
    contentHierarchyItem?: CriteriaSet;
    mediaFiles?: CriteriaSet;
}

export interface CriteriaSet {
    and?: unknown[];
    or?: unknown[];
    not?: unknown;
}
