import { useCallback, useEffect, useMemo } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { type ContentItem } from '@/components/ui/ContentList';
import { getRootLevels, useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';

export function useLibraryRoute() {
    const { contentType: contentTypeSlug, levelSlug } = useParams<{
        contentType?: string;
        levelSlug?: string;
    }>();
    const navigate = useNavigate();

    const { contentTypes, isLoading: libraryTypesLoading, error: libraryTypesError } = useLibraryTypes();
    const typeIconName = useTypeIconName();

    const currentContentType = useMemo(() => {
        if (!contentTypeSlug) {
            return null;
        }
        return contentTypes.find((type) => type.slug === contentTypeSlug) ?? null;
    }, [contentTypeSlug, contentTypes]);
    const routeContentTypeSlug = currentContentType?.slug;
    const canValidateContentTypeSlug = !libraryTypesLoading && libraryTypesError === null;
    const hasInvalidContentTypeSlug = !!contentTypeSlug && canValidateContentTypeSlug && currentContentType === null;
    const contentTypeValidationPending = !!contentTypeSlug && libraryTypesLoading && currentContentType === null;

    useEffect(() => {
        if (hasInvalidContentTypeSlug) {
            void navigate('/library/all', { replace: true });
        }
    }, [hasInvalidContentTypeSlug, navigate]);

    const rootLevels = useMemo(() => getRootLevels(currentContentType), [currentContentType]);

    const selectedLevel = useMemo(() => {
        if (!levelSlug) {
            return null;
        }
        return rootLevels.find((l) => l.slug === levelSlug) ?? null;
    }, [levelSlug, rootLevels]);

    const showTypePicker = currentContentType !== null && !levelSlug && rootLevels.length > 1;

    const pageTitle = selectedLevel?.displayName ?? currentContentType?.pluralName ?? 'Library';
    const pageIconName = selectedLevel
        ? typeIconName(selectedLevel.slug)
        : routeContentTypeSlug
          ? typeIconName(routeContentTypeSlug)
          : undefined;

    const handleContentClick = useCallback(
        (item: ContentItem) => {
            void navigate(`/library/${item.id}`);
        },
        [navigate],
    );

    const addContentTo = routeContentTypeSlug
        ? `/library/add?contentType=${encodeURIComponent(routeContentTypeSlug)}`
        : '/library/add';

    return {
        contentTypeSlug,
        currentContentType,
        routeContentTypeSlug,
        contentTypes,
        libraryTypesError,
        rootLevels,
        selectedLevel,
        showTypePicker,
        pageTitle,
        pageIconName,
        hasInvalidContentTypeSlug,
        contentTypeValidationPending,
        handleContentClick,
        addContentTo,
    };
}
