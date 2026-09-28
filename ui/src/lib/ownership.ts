interface OwnedItem {
    ownerId: string | null;
    ownerLock: boolean;
}

export function canEditOwned(
    item: OwnedItem,
    userId: string | undefined,
    isAdmin: boolean,
    canWrite: boolean,
): boolean {
    if (!canWrite) {
        return false;
    }
    if (item.ownerId === null) {
        return isAdmin;
    }
    return item.ownerId === userId || (isAdmin && !item.ownerLock);
}
