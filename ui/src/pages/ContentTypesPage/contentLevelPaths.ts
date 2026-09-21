interface ContentLevelRelationship {
    slug: string;
    parentSlug?: string | null;
}

function compareText(left: string, right: string): number {
    return left < right ? -1 : left > right ? 1 : 0;
}

export function buildContentLevelPaths<TLevel extends ContentLevelRelationship>(
    definitions: readonly TLevel[],
): TLevel[][] {
    const definitionsBySlug = new Map(definitions.map((level) => [level.slug, level]));

    const representedChildren = new Map<string, string[]>();
    for (const [slug, level] of definitionsBySlug) {
        if (!level.parentSlug || !definitionsBySlug.has(level.parentSlug)) {
            continue;
        }
        const children = representedChildren.get(level.parentSlug) ?? [];
        children.push(slug);
        representedChildren.set(level.parentSlug, children);
    }

    const terminalSlugs = [...definitionsBySlug.keys()]
        .filter((slug) => (representedChildren.get(slug)?.length ?? 0) === 0)
        .sort(compareText);

    const paths = terminalSlugs.map((terminalSlug) => {
        const path: TLevel[] = [];
        const seen = new Set<string>();
        let current = definitionsBySlug.get(terminalSlug);
        while (current && definitionsBySlug.has(current.slug) && !seen.has(current.slug)) {
            seen.add(current.slug);
            path.push(current);
            current = current.parentSlug ? definitionsBySlug.get(current.parentSlug) : undefined;
        }
        return path.reverse();
    });

    return paths.sort((left, right) =>
        compareText(left.map((level) => level.slug).join('\u0000'), right.map((level) => level.slug).join('\u0000')),
    );
}
