export function formatLibraryItemCount(count: number | undefined): string | undefined {
    if (count === undefined) {
        return undefined;
    }
    return count === 1 ? '1 item' : `${count.toLocaleString()} items`;
}
