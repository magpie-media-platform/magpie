import { useMemo, useCallback } from 'react';
import { useParams, useNavigate, useLocation, useSearchParams } from 'react-router-dom';

interface ContentDetailParams {
    contentId: string | undefined;
    childId: string | undefined;
    grandchildId: string | undefined;
    activeContentId: string | undefined;
    parentId: string | undefined;
    rootId: string | undefined;
    activeTab: string;
    hasExplicitTab: boolean;
    setActiveTab: (tab: string) => void;
    navigate: ReturnType<typeof useNavigate>;
    childrenBasePath: string;
}

const FALLBACK_DEFAULT_TAB = 'related';

export function useContentDetailParams(defaultTab: string = FALLBACK_DEFAULT_TAB): ContentDetailParams {
    const { contentId, childId, grandchildId } = useParams<{
        contentId: string;
        childId?: string;
        grandchildId?: string;
    }>();
    const navigate = useNavigate();
    const location = useLocation();
    const [searchParams, setSearchParams] = useSearchParams();

    const explicitTab = searchParams.get('tab');
    const hasExplicitTab = explicitTab !== null && explicitTab.length > 0;
    const activeTab = explicitTab || defaultTab;
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === defaultTab) {
                        next.delete('tab');
                    } else {
                        next.set('tab', tab);
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams, defaultTab],
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
        hasExplicitTab,
        setActiveTab,
        navigate,
        childrenBasePath,
    };
}
