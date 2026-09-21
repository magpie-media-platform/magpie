import { useMemo, useCallback } from 'react';
import { useParams, useNavigate, useLocation, useSearchParams } from 'react-router-dom';

interface ContentDetailParams {
    activeTab: string;
    rawTab: string | null;
    setActiveTab: (tab: string) => void;
    navigate: ReturnType<typeof useNavigate>;
    childrenBasePath: string;
}

export function useContentDetailParams(defaultTab: string): ContentDetailParams {
    const { contentId, childId, grandchildId } = useParams<{
        contentId: string;
        childId?: string;
        grandchildId?: string;
    }>();
    const navigate = useNavigate();
    const location = useLocation();
    const [searchParams, setSearchParams] = useSearchParams();

    const explicitTab = searchParams.get('tab');
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

    const childrenBasePath = useMemo(() => {
        if (grandchildId) {
            return location.pathname;
        } else if (childId) {
            return `/library/${contentId}/${childId}`;
        }
        return `/library/${contentId}`;
    }, [contentId, childId, grandchildId, location.pathname]);

    return {
        activeTab,
        rawTab: explicitTab,
        setActiveTab,
        navigate,
        childrenBasePath,
    };
}
