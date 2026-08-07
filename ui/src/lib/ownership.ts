import { Role } from '@/auth';
import type { Role as RoleType } from '@/auth';

interface OwnedItem {
    ownerId: string | null;
    ownerLock: boolean;
}

export function canEditOwned(
    item: OwnedItem,
    userId: string | undefined,
    canManage: boolean,
    hasRole: (role: RoleType) => boolean,
    requesterRole: RoleType,
): boolean {
    const isOwner = item.ownerId === userId;
    const isShared = item.ownerId === null;
    const isAdmin = hasRole(Role.Admin);

    if (item.ownerLock && !isShared) {
        return isOwner || isAdmin;
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
