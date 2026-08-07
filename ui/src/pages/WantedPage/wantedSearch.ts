import type { ContentListItem } from '@/types';

function isSearchEligibleLeaf(item: ContentListItem): boolean {
    return item.effectiveMonitoring && (item.fulfillment === 'missing' || item.fulfillment === 'upgradable');
}

export async function countMissingDescendants(
    container: ContentListItem,
    isContainer: (item: ContentListItem) => boolean,
    loadChildren: (parentId: string) => Promise<ContentListItem[]>,
): Promise<number> {
    const pending = await loadChildren(container.id);
    const visited = new Set([container.id]);
    let count = 0;

    while (pending.length > 0) {
        const item = pending.shift()!;
        if (visited.has(item.id)) {
            continue;
        }
        visited.add(item.id);

        if (isContainer(item)) {
            pending.push(...(await loadChildren(item.id)));
        } else if (item.effectiveMonitoring && item.fulfillment === 'missing') {
            count += 1;
        }
    }

    return count;
}

export async function resolveWantedSearchLeaves(
    selectedItems: ContentListItem[],
    isContainer: (item: ContentListItem) => boolean,
    loadChildren: (parentId: string) => Promise<ContentListItem[]>,
): Promise<{ leafIds: string[]; skippedActiveCount: number }> {
    const pending = [...selectedItems];
    const visited = new Set<string>();
    const leafIds = new Set<string>();
    let skippedActiveCount = 0;

    while (pending.length > 0) {
        const item = pending.shift()!;
        if (visited.has(item.id)) {
            continue;
        }
        visited.add(item.id);

        if (isContainer(item)) {
            pending.push(...(await loadChildren(item.id)));
            continue;
        }

        if (!isSearchEligibleLeaf(item)) {
            continue;
        }

        if (item.acquisitionStatus) {
            skippedActiveCount += 1;
            continue;
        }

        leafIds.add(item.id);
    }

    return { leafIds: [...leafIds], skippedActiveCount };
}
