export function displayLineageLeafTitle(download: {
    contentMetadataTitle?: string | null;
    contentHierarchyName?: string | null;
    title: string;
}): string {
    return download.contentMetadataTitle?.trim() || download.contentHierarchyName?.trim() || download.title;
}
