import type { Role } from '@/auth';

interface OwnedItem {
    ownerId: string | null;
    ownerLock: boolean;
}

export function canEditOwned(
    item: OwnedItem,
    userId: string | undefined,
    canManage: boolean,
    hasRole: (role: Role) => boolean,
    requesterRole: Role,
): boolean {
    const isOwner = item.ownerId === userId;
    const isShared = item.ownerId === null;

    if (item.ownerLock && !isShared) {
        return isOwner;
    }

    if (isShared) {
        return canManage;
    }

    if (canManage) {
        return true;
    }
    if (hasRole(requesterRole) && isOwner) {
        return true;
    }
    return false;
}
