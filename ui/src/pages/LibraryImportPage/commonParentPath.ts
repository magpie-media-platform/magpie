export function commonParentPath(paths: string[]): string {
    if (paths.length === 0) {
        return '';
    }

    const parents = paths.map((path) => {
        const index = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
        if (index < 0) return '';
        if (index === 0) return path[0];
        if (index === 2 && /^[A-Za-z]:[/\\]/.test(path)) return path.slice(0, 3);
        return path.slice(0, index);
    });
    const separator = parents[0]?.includes('\\') ? '\\' : '/';
    const roots = parents.map((path) => path.match(/^(?:[A-Za-z]:[/\\]|[/\\]{1,2})/)?.[0] ?? '');
    if (roots.some((root) => root.replace(/\\/g, '/') !== roots[0].replace(/\\/g, '/'))) return '';

    const segments = parents.map((path, index) => path.slice(roots[index].length).split(/[/\\]/).filter(Boolean));
    const commonLength = segments[0].findIndex((part, index) => segments.some((path) => path[index] !== part));
    const common = segments[0].slice(0, commonLength < 0 ? undefined : commonLength).join(separator);
    return roots[0] + common;
}
