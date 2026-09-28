import { createContext, useContext, useState, type ReactNode } from 'react';
import { useLocation, type Location } from 'react-router-dom';

import { apiGet } from '@/lib/api';

export interface ContentOrigin {
    pathname: string;
    search: string;
    hash: string;
    state?: unknown;
}
const PreviousLocation = createContext<ContentOrigin | null>(null);

export function ContentOriginProvider({ children }: { children: ReactNode }) {
    const location = useLocation();
    const [history, setHistory] = useState<{ current: Location; previous: ContentOrigin | null }>({
        current: location,
        previous: null,
    });
    if (history.current.key !== location.key) {
        setHistory({ current: location, previous: history.current });
    }
    return <PreviousLocation value={history.previous}>{children}</PreviousLocation>;
}

export function useContentOrigin(contentId: string | undefined) {
    const location = useLocation();
    const previous = useContext(PreviousLocation);
    const candidate = (location.state as { contentOrigin?: ContentOrigin } | null)?.contentOrigin ?? previous;
    const [saved, setSaved] = useState({ contentId, origin: candidate });
    if (saved.contentId !== contentId) setSaved({ contentId, origin: candidate });
    return saved.origin;
}

export async function resolveContentOrigin(
    origin: ContentOrigin | null,
    deletedId: string,
    canManage: boolean,
    isAdmin: boolean,
): Promise<ContentOrigin> {
    const fallback = { pathname: '/library', search: '', hash: '' };
    if (
        !origin ||
        typeof origin.pathname !== 'string' ||
        typeof origin.search !== 'string' ||
        typeof origin.hash !== 'string'
    )
        return fallback;
    if (!origin.pathname.startsWith('/') || origin.pathname.startsWith('//') || /[\\?#\s]/.test(origin.pathname))
        return fallback;
    if ((origin.search && !origin.search.startsWith('?')) || (origin.hash && !origin.hash.startsWith('#')))
        return fallback;
    let parts: string[];
    try {
        parts = origin.pathname.split('/').filter(Boolean).map(decodeURIComponent);
    } catch {
        return fallback;
    }
    if (parts.some((part) => part === '.' || part === '..' || /[\\/]/.test(part))) return fallback;
    if (parts.some((part) => part.toLowerCase() === deletedId.toLowerCase())) return fallback;
    if (parts[0] === 'admin' && !isAdmin) return fallback;
    if (['add', 'import'].includes(parts[1] ?? '') && !canManage) return fallback;
    const listPath =
        /^(?:\/|\/library(?:\/all|\/content\/[^/]+(?:\/[^/]+)?|\/subjects\/[^/]+|\/add|\/import)?|\/wanted|\/calendar|\/downloads|\/filters|\/wishlists|\/my-jobs|\/admin\/(?:system|jobs))$/;
    const entity = /^\/(library|subjects|wishlists)\/([^/]+)(?:\/([^/]+))?(?:\/([^/]+))?$/.exec(origin.pathname);
    if (listPath.test(origin.pathname)) {
        const params = new URLSearchParams(origin.search);
        const parentId = params.get('parentId');
        if (parentId?.toLowerCase() === deletedId.toLowerCase()) return fallback;
        try {
            if (parentId) await apiGet(`/content/${encodeURIComponent(parentId)}`);
            const savedFilter = params.get('savedFilter');
            if (savedFilter) await apiGet(`/content-filters/${encodeURIComponent(savedFilter)}`);
            if (parts[0] === 'library' && parts[1] === 'content') {
                const types = await apiGet<{ slug: string; levelDefinitions: { slug: string }[] }[]>('/content-types');
                const type = types.find((candidate) => candidate.slug === parts[2]);
                if (!type || (parts[3] && !type.levelDefinitions.some((level) => level.slug === parts[3])))
                    return fallback;
            }
            if (parts[0] === 'library' && parts[1] === 'subjects' && parts[2] !== 'all') {
                const types = await apiGet<{ slug: string }[]>('/subject-types');
                if (!types.some((type) => type.slug === parts[2])) return fallback;
            }
        } catch {
            return fallback;
        }
        return origin;
    }
    if (!entity) return fallback;
    try {
        const ids = parts.slice(1);
        for (const id of ids)
            await apiGet(`/${entity[1] === 'library' ? 'content' : entity[1]}/${encodeURIComponent(id)}`);
        return origin;
    } catch {
        return fallback;
    }
}
