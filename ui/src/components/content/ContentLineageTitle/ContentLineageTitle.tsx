import {
    createContext,
    useCallback,
    useContext,
    useEffect,
    useMemo,
    useRef,
    useState,
    type ComponentPropsWithoutRef,
    type ReactNode,
} from 'react';

import { useEventStream, type EventStreamPayload } from '@/hooks/useEventStream';
import { apiGet, isApiError } from '@/lib/api';
import { magpieEventNames } from '@/lib/magpieEventNames';
import type { LibraryTypeDefinition } from '@/types/library';
import type { ApiContentHierarchyItem } from '@/types/content';

import './ContentLineageTitle.css';

const CONTENT_LINEAGE_CACHE_CAPACITY = 256;
const MAX_ANCESTOR_DEPTH = 32;

interface ContentLineageQualifiedName {
    id: string;
    parentId?: string | null;
    title: string;
}

interface ContentLineageTitleFallback {
    contentType?: string;
    hierarchyName?: string | null;
    level?: string;
}

type ContentLineageQualifiedNameLoader = (id: string) => Promise<ContentLineageQualifiedName | null>;

class ContentLineageCache {
    private readonly resolved = new Map<string, ContentLineageQualifiedName | null>();
    private readonly inFlight = new Map<string, Promise<ContentLineageQualifiedName | null>>();

    constructor(
        private readonly loadQualifiedName: ContentLineageQualifiedNameLoader,
        private readonly capacity = CONTENT_LINEAGE_CACHE_CAPACITY,
    ) {
        if (!Number.isInteger(capacity) || capacity < 1) {
            throw new RangeError('Content lineage cache capacity must be a positive integer.');
        }
    }

    get size(): number {
        return this.resolved.size;
    }

    resolve(id: string): Promise<ContentLineageQualifiedName | null> {
        if (this.resolved.has(id)) {
            const value = this.resolved.get(id) ?? null;
            this.resolved.delete(id);
            this.resolved.set(id, value);
            return Promise.resolve(value);
        }

        const pending = this.inFlight.get(id);
        if (pending) {
            return pending;
        }

        const request: Promise<ContentLineageQualifiedName | null> = this.loadQualifiedName(id)
            .then((value) => {
                if (this.inFlight.get(id) === request) {
                    this.store(id, value);
                }
                return value;
            })
            .finally(() => {
                if (this.inFlight.get(id) === request) {
                    this.inFlight.delete(id);
                }
            });
        this.inFlight.set(id, request);
        return request;
    }

    invalidate(id: string): void {
        this.resolved.delete(id);
        this.inFlight.delete(id);
    }

    clear(): void {
        this.resolved.clear();
        this.inFlight.clear();
    }

    private store(id: string, value: ContentLineageQualifiedName | null): void {
        this.resolved.delete(id);
        this.resolved.set(id, value);
        while (this.resolved.size > this.capacity) {
            const oldest = this.resolved.keys().next().value as string | undefined;
            if (oldest === undefined) {
                return;
            }
            this.resolved.delete(oldest);
        }
    }
}

interface ContentLineageContextValue {
    refresh: ContentLineageRefresh;
    resolve: ContentLineageQualifiedNameLoader;
    resolveTitle: (title: string, fallback?: ContentLineageTitleFallback) => string;
}

interface ContentLineageRefresh {
    all: boolean;
    itemId: string | null;
}

const ContentLineageContext = createContext<ContentLineageContextValue | null>(null);

async function fetchContentLineageQualifiedName(id: string): Promise<ContentLineageQualifiedName | null> {
    try {
        const item = await apiGet<ApiContentHierarchyItem>(`/content/${encodeURIComponent(id)}`);
        return {
            id: item.id,
            parentId: item.parentId,
            title:
                item.metadataContainer?.contributions?.find((contribution) => contribution.title)?.title ?? item.name,
        };
    } catch (error) {
        if (isApiError(error) && error.status === 404) {
            return null;
        }
        throw error;
    }
}

interface ContentLineageProviderProps {
    children: ReactNode;
    contentTypes: readonly LibraryTypeDefinition[];
    cacheCapacity?: number;
    loadQualifiedName?: ContentLineageQualifiedNameLoader;
}

export function ContentLineageProvider({
    children,
    contentTypes,
    cacheCapacity = CONTENT_LINEAGE_CACHE_CAPACITY,
    loadQualifiedName = fetchContentLineageQualifiedName,
}: ContentLineageProviderProps) {
    const cache = useMemo(
        () => new ContentLineageCache(loadQualifiedName, cacheCapacity),
        [cacheCapacity, loadQualifiedName],
    );
    const [refresh, setRefresh] = useState<ContentLineageRefresh>({ all: false, itemId: null });

    const handleEvent = useCallback(
        (event: EventStreamPayload) => {
            if (event.name === 'stream.reconnected' || event.name === 'library.scan.completed') {
                cache.clear();
                setRefresh({ all: true, itemId: null });
            } else {
                const itemId = event.context['content.hierarchyItemId'];
                if (itemId) {
                    cache.invalidate(itemId);
                    setRefresh({ all: false, itemId });
                }
            }
        },
        [cache],
    );

    useEventStream(handleEvent, {
        eventName: [
            magpieEventNames.contentHierarchyItemCreated,
            magpieEventNames.contentHierarchyItemUpdated,
            magpieEventNames.contentHierarchyItemDeleted,
            magpieEventNames.metadataRefreshed,
            'library.scan.completed',
            'stream.reconnected',
        ],
        owner: 'content-lineage-provider',
    });

    const resolve = useCallback((id: string) => cache.resolve(id), [cache]);
    const fallbackTitles = useMemo(() => {
        const types = new Map<string, string>();
        const levels = new Map<string, string>();
        for (const type of contentTypes) {
            types.set(type.slug, type.name);
            for (const level of type.levels) {
                levels.set(`${type.slug}:${level.slug}`, level.displayName);
            }
        }
        return { levels, types };
    }, [contentTypes]);
    const resolveTitle = useCallback(
        (title: string, fallback?: ContentLineageTitleFallback) => {
            const materializedTitle = title.trim();
            if (materializedTitle) {
                return materializedTitle;
            }

            const hierarchyName = fallback?.hierarchyName?.trim();
            if (hierarchyName) {
                return hierarchyName;
            }

            const contentType = fallback?.contentType;
            const level = fallback?.level;
            return (
                (contentType && level ? fallbackTitles.levels.get(`${contentType}:${level}`) : undefined) ??
                (contentType ? fallbackTitles.types.get(contentType) : undefined) ??
                'Content'
            );
        },
        [fallbackTitles],
    );
    const value = useMemo(() => ({ refresh, resolve, resolveTitle }), [refresh, resolve, resolveTitle]);

    return <ContentLineageContext.Provider value={value}>{children}</ContentLineageContext.Provider>;
}

interface PathState {
    loading: boolean;
    path: string;
}

interface LineageNodeProps {
    fallback?: ContentLineageTitleFallback;
    isLeaf: boolean;
    onPathChange: (state: PathState) => void;
    qualifiedName: ContentLineageQualifiedName;
    visitedIds: readonly string[];
}

type ParentState =
    | { status: 'none' | 'loading' | 'missing' | 'failed' }
    | { status: 'resolved'; qualifiedName: ContentLineageQualifiedName };

function LineageNode({ qualifiedName, fallback, isLeaf, onPathChange, visitedIds }: LineageNodeProps) {
    const context = useContext(ContentLineageContext);
    const title = (context?.resolveTitle(qualifiedName.title, fallback) ?? qualifiedName.title.trim()) || 'Content';
    const refresh = context?.refresh;
    const resolve = context?.resolve;
    const parentId = qualifiedName.parentId ?? null;
    const canResolveParent =
        parentId !== null && !visitedIds.includes(parentId) && visitedIds.length <= MAX_ANCESTOR_DEPTH;
    const [parent, setParent] = useState<ParentState>({ status: canResolveParent ? 'loading' : 'none' });
    const [parentPath, setParentPath] = useState<PathState>({ loading: false, path: '' });
    const lastResolution = useRef<
        | {
              canResolveParent: boolean;
              parentId: string | null;
              resolve: ContentLineageQualifiedNameLoader | undefined;
          }
        | undefined
    >(undefined);
    const resolutionSequence = useRef(0);

    useEffect(
        () => () => {
            resolutionSequence.current += 1;
            lastResolution.current = undefined;
        },
        [],
    );

    useEffect(() => {
        const sourceChanged =
            lastResolution.current?.parentId !== parentId ||
            lastResolution.current?.resolve !== resolve ||
            lastResolution.current?.canResolveParent !== canResolveParent;
        if (!sourceChanged && !refresh?.all && refresh?.itemId !== parentId) {
            return;
        }
        lastResolution.current = { canResolveParent, parentId, resolve };
        resolutionSequence.current += 1;
        const sequence = resolutionSequence.current;

        if (!resolve || !canResolveParent) {
            setParent({ status: 'none' });
            return;
        }

        setParent({ status: 'loading' });
        void resolve(parentId)
            .then((resolved) => {
                if (resolutionSequence.current === sequence) {
                    setParent(resolved ? { status: 'resolved', qualifiedName: resolved } : { status: 'missing' });
                }
            })
            .catch(() => {
                if (resolutionSequence.current === sequence) {
                    setParent({ status: 'failed' });
                }
            });
    }, [refresh, resolve, canResolveParent, parentId]);

    const handleParentPathChange = useCallback((state: PathState) => {
        setParentPath((current) =>
            current.loading === state.loading && current.path === state.path ? current : state,
        );
    }, []);

    useEffect(() => {
        const hasResolvedParent = parent.status === 'resolved' && parentPath.path.length > 0;
        const path = hasResolvedParent ? `${parentPath.path} > ${title}` : title;
        const loading = parent.status === 'loading' || (parent.status === 'resolved' && parentPath.loading);
        onPathChange({ loading, path });
    }, [onPathChange, parent.status, parentPath, title]);

    const nextVisitedIds = parent.status === 'resolved' ? [...visitedIds, parent.qualifiedName.id] : visitedIds;

    return (
        <span className="content-lineage-title__node">
            {parent.status === 'loading' && (
                <>
                    <span
                        aria-hidden="true"
                        className="content-lineage-title__placeholder"
                        data-testid="content-lineage-placeholder"
                    />
                    <span aria-hidden="true" className="content-lineage-title__separator">
                        &gt;
                    </span>
                </>
            )}
            {parent.status === 'resolved' && (
                <>
                    <LineageNode
                        qualifiedName={parent.qualifiedName}
                        isLeaf={false}
                        onPathChange={handleParentPathChange}
                        visitedIds={nextVisitedIds}
                    />
                    <span aria-hidden="true" className="content-lineage-title__separator">
                        &gt;
                    </span>
                </>
            )}
            <span
                className={
                    `content-lineage-title__segment ` +
                    `content-lineage-title__segment--${isLeaf ? 'leaf' : 'ancestor'}`
                }
            >
                {title}
            </span>
        </span>
    );
}

interface ContentLineageTitleProps extends Omit<ComponentPropsWithoutRef<'span'>, 'children' | 'title'> {
    fallback?: ContentLineageTitleFallback;
    qualify?: boolean;
    qualifiedName: ContentLineageQualifiedName;
}

export function ContentLineageTitle({
    qualifiedName,
    fallback,
    qualify = true,
    className,
    ...props
}: ContentLineageTitleProps) {
    const context = useContext(ContentLineageContext);
    const title = (context?.resolveTitle(qualifiedName.title, fallback) ?? qualifiedName.title.trim()) || 'Content';
    const [pathState, setPathState] = useState<PathState>({ loading: false, path: title });
    const handlePathChange = useCallback((state: PathState) => {
        setPathState((current) => (current.loading === state.loading && current.path === state.path ? current : state));
    }, []);
    const classes = ['content-lineage-title', className].filter(Boolean).join(' ');

    useEffect(() => {
        if (!qualify || !context || !qualifiedName.parentId) {
            setPathState({ loading: false, path: title });
        }
    }, [context, qualify, qualifiedName.parentId, title]);

    return (
        <span
            className={classes}
            data-component="content-lineage-title"
            data-qualified={qualify && context && qualifiedName.parentId ? 'true' : undefined}
            aria-busy={pathState.loading || undefined}
            title={pathState.path}
            {...props}
        >
            <span className="content-lineage-title__visual">
                {qualify && context && qualifiedName.parentId ? (
                    <LineageNode
                        qualifiedName={qualifiedName}
                        fallback={fallback}
                        isLeaf
                        onPathChange={handlePathChange}
                        visitedIds={[qualifiedName.id]}
                    />
                ) : (
                    <span className="content-lineage-title__segment content-lineage-title__segment--leaf">{title}</span>
                )}
            </span>
        </span>
    );
}

ContentLineageTitle.displayName = 'ContentLineageTitle';

export default ContentLineageTitle;
