/** The tag add/remove patch that turns `original` into `next`, or undefined when the two lists already agree. */
export function tagsPatch(next: string[], original: string[]): { add?: string[]; remove?: string[] } | undefined {
    const add = next.filter((name) => !original.includes(name));
    const remove = original.filter((name) => !next.includes(name));
    if (add.length === 0 && remove.length === 0) {
        return undefined;
    }
    return {
        ...(add.length > 0 && { add }),
        ...(remove.length > 0 && { remove }),
    };
}
