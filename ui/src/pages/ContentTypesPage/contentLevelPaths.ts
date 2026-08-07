interface ContentLevelRelationship {
    slug: string;
    parentSlug?: string | null;
}

function compareText(left: string, right: string): number {
    return left < right ? -1 : left > right ? 1 : 0;
}

export function buildContentLevelPaths<TLevel extends ContentLevelRelationship>(
    definitions: readonly TLevel[],
    supportedLevels: readonly TLevel[] = definitions,
): TLevel[][] {
    const definitionsBySlug = new Map(definitions.map((level) => [level.slug, level]));
    const representedSlugs = new Set<string>();

    for (const supported of supportedLevels) {
        let current = definitionsBySlug.get(supported.slug);
        const ancestors = new Set<string>();
        while (current && !ancestors.has(current.slug)) {
            ancestors.add(current.slug);
            representedSlugs.add(current.slug);
            current = current.parentSlug ? definitionsBySlug.get(current.parentSlug) : undefined;
        }
    }

    const representedChildren = new Map<string, string[]>();
    for (const slug of representedSlugs) {
        const level = definitionsBySlug.get(slug);
        if (!level?.parentSlug || !representedSlugs.has(level.parentSlug)) {
            continue;
        }
        const children = representedChildren.get(level.parentSlug) ?? [];
        children.push(slug);
        representedChildren.set(level.parentSlug, children);
    }

    const terminalSlugs = [...representedSlugs]
        .filter((slug) => (representedChildren.get(slug)?.length ?? 0) === 0)
        .sort(compareText);

    const paths = terminalSlugs.map((terminalSlug) => {
        const path: TLevel[] = [];
        const seen = new Set<string>();
        let current = definitionsBySlug.get(terminalSlug);
        while (current && representedSlugs.has(current.slug) && !seen.has(current.slug)) {
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
