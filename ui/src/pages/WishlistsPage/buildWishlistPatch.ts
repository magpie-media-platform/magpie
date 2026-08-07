import type { WishlistPatch } from '@/hooks/useWishlists';
import type { WishlistFormValues } from '@/types';

export function buildWishlistPatch(values: WishlistFormValues, originalTagNames: string[]): WishlistPatch {
    const patch: WishlistPatch = {
        name: { value: values.name },
        criteria: { value: values.criteria },
        enabled: { value: values.monitored },
    };
    if (values.qualityTierListId !== undefined) {
        patch.qualityTierListId = { value: values.qualityTierListId };
    }
    if (values.rootFolderId !== undefined) {
        patch.rootFolderId = { value: values.rootFolderId };
    }

    const tagsToAdd = values.tagIds.filter((id) => !originalTagNames.includes(id));
    const tagsToRemove = originalTagNames.filter((id) => !values.tagIds.includes(id));
    if (tagsToAdd.length > 0 || tagsToRemove.length > 0) {
        patch.tags = {};
        if (tagsToAdd.length > 0) {
            patch.tags.add = tagsToAdd;
        }
        if (tagsToRemove.length > 0) {
            patch.tags.remove = tagsToRemove;
        }
    }

    return patch;
}
