import { useMemo } from 'react';

import { useRootFolders } from '@/hooks/useRootFolders';
import { useTags } from '@/hooks/useTags';
import type { RootFolderOption, TagOption } from '@/types';

interface FormOptions {
    rootFolderOptions: RootFolderOption[];
    tagOptions: TagOption[];
}

export function useFormOptions(): FormOptions {
    const { folders: rootFolders } = useRootFolders();
    const { tags: apiTags } = useTags();

    const rootFolderOptions: RootFolderOption[] = useMemo(
        () => rootFolders.map((f) => ({ id: f.id, name: f.name, path: f.path, contentType: f.contentType })),
        [rootFolders],
    );

    const tagOptions: TagOption[] = useMemo(() => apiTags.map((t) => ({ id: t.name, name: t.name })), [apiTags]);

    return { rootFolderOptions, tagOptions };
}
