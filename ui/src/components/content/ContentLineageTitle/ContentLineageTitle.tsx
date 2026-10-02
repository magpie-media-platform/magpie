import {
    createContext,
    useCallback,
    useContext,
    useEffect,
    useMemo,
    useState,
    type ComponentPropsWithoutRef,
    type ReactNode,
} from 'react';

import { skipToken, useQuery, useQueryClient } from '@tanstack/react-query';

import { useEventStream, type EventStreamPayload } from '@/hooks/useEventStream';
import { apiGet, isApiError } from '@/lib/api';
import { magpieEventNames } from '@/lib/magpieEventNames';
import type { LibraryTypeDefinition } from '@/types/library';
import type { ApiContentHierarchyItem } from '@/types/content';

import './ContentLineageTitle.css';

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

interface ContentLineageContextValue {
    resolveTitle: (title: string, fallback?: ContentLineageTitleFallback) => string;
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
}

export function ContentLineageProvider({ children, contentTypes }: ContentLineageProviderProps) {
    const queryClient = useQueryClient();

    const handleEvent = useCallback(
        (event: EventStreamPayload) => {
            if (event.name === 'stream.reconnected' || event.name === 'library.scan.completed') {
                void queryClient.cancelQueries({ queryKey: ['content-lineage'] });
                void queryClient.invalidateQueries({ queryKey: ['content-lineage'] });
            } else {
                const itemId = event.context['content.hierarchyItemId'];
                if (itemId) {
                    void queryClient.cancelQueries({ queryKey: ['content-lineage', itemId], exact: true });
                    void queryClient.invalidateQueries({ queryKey: ['content-lineage', itemId], exact: true });
                }
            }
        },
        [queryClient],
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
    });

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
    const value = useMemo(() => ({ resolveTitle }), [resolveTitle]);

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
    const parentId = qualifiedName.parentId ?? null;
    const canResolveParent =
        parentId !== null && !visitedIds.includes(parentId) && visitedIds.length <= MAX_ANCESTOR_DEPTH;
    const parentQuery = useQuery({
        queryKey: ['content-lineage', parentId],
        queryFn: parentId === null ? skipToken : () => fetchContentLineageQualifiedName(parentId),
        enabled: canResolveParent,
        staleTime: Infinity,
    });
    const parent: ParentState = !canResolveParent
        ? { status: 'none' }
        : parentQuery.isPending
          ? { status: 'loading' }
          : parentQuery.isError
            ? { status: 'failed' }
            : parentQuery.data === null
              ? { status: 'missing' }
              : { status: 'resolved', qualifiedName: parentQuery.data };
    const [parentPath, setParentPath] = useState<PathState>({ loading: false, path: '' });
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
