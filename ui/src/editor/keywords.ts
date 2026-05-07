const keywords: Record<string, number> = {
    not: 1,
    and: 2,
    or: 3,
    contains: 4,
    startswith: 5,
    endswith: 6,
    null: 7,
    in: 8,
    has: 9,
};

export function specializeKeyword(value: string): number {
    const normalized = value.toLowerCase();
    return keywords[normalized] ?? -1;
}
