import type { WishlistPatch } from '@/hooks/useWishlists';
import { tagsPatch } from '@/lib/tagsPatch';

import type { WishlistFormValues } from './WishlistForm';

export function buildWishlistPatch(values: WishlistFormValues, originalTagNames: string[]): WishlistPatch {
    const patch: WishlistPatch = {
        name: { value: values.name },
        criteria: { value: values.criteria },
        enabled: { value: values.enabled },
        monitoring: { value: values.monitoring },
    };
    if (values.qualityTierListId !== undefined) {
        patch.qualityTierListId = { value: values.qualityTierListId };
    }
    if (values.rootFolderId !== undefined) {
        patch.rootFolderId = { value: values.rootFolderId };
    }

    const tags = tagsPatch(values.tagIds, originalTagNames);
    if (tags) {
        patch.tags = tags;
    }

    return patch;
}
