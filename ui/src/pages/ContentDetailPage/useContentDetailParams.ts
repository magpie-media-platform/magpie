import { useMemo, useCallback } from 'react';
import { useParams, useNavigate, useLocation, useSearchParams } from 'react-router-dom';

export interface ContentDetailParams {
    contentId: string | undefined;
    childId: string | undefined;
    grandchildId: string | undefined;
    activeContentId: string | undefined;
    parentId: string | undefined;
    rootId: string | undefined;
    activeTab: string;
    setActiveTab: (tab: string) => void;
    navigate: ReturnType<typeof useNavigate>;
    childrenBasePath: string;
}

export function useContentDetailParams(): ContentDetailParams {
    const { contentId, childId, grandchildId } = useParams<{
        contentId: string;
        childId?: string;
        grandchildId?: string;
    }>();
    const navigate = useNavigate();
    const location = useLocation();
    const [searchParams, setSearchParams] = useSearchParams();

    const activeTab = searchParams.get('tab') || 'overview';
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === 'overview') {
                        next.delete('tab');
                    } else {
                        next.set('tab', tab);
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const activeContentId = grandchildId || childId || contentId;

    const parentId = useMemo(() => {
        if (grandchildId) {
            return childId;
        } else if (childId) {
            return contentId;
        }
        return undefined;
    }, [contentId, childId, grandchildId]);

    const rootId = grandchildId ? contentId : undefined;

    const childrenBasePath = useMemo(() => {
        if (grandchildId) {
            return location.pathname;
        } else if (childId) {
            return `/library/${contentId}/${childId}`;
        }
        return `/library/${contentId}`;
    }, [contentId, childId, grandchildId, location.pathname]);

    return {
        contentId,
        childId,
        grandchildId,
        activeContentId,
        parentId,
        rootId,
        activeTab,
        setActiveTab,
        navigate,
        childrenBasePath,
    };
}
