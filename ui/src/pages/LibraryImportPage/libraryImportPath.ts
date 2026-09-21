export interface LibraryImportPathOptions {
    groupKey?: string;

    rootFolderId?: string;

    title?: string;

    path?: readonly string[];
}

/** Builds a link to the library import queue, or to one group inside it. */
export function libraryImportPath({ groupKey, rootFolderId, title, path = [] }: LibraryImportPathOptions = {}): string {
    const params = new URLSearchParams();
    if (groupKey) {
        params.set('key', groupKey);
    }
    if (rootFolderId) {
        params.set('rootFolderId', rootFolderId);
    }
    if (title) {
        params.set('title', title);
    }
    for (const value of path) {
        params.append('path', value);
    }
    const query = params.toString();
    return query ? `/library/import?${query}` : '/library/import';
}
