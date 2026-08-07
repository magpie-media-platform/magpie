import { lazy } from 'react';
import { Navigate, useLocation, useParams } from 'react-router-dom';

import { useLibraryTypes } from '@/hooks/useLibraryTypes';

import { RouteLoadingState } from './RouteLoadingState';

const ContentDetailPage = lazy(() => import('@/pages/ContentDetailPage/ContentDetailPage'));

function normalizeSegment(value: string): string {
    return value.toLowerCase().replace(/[ _]/g, '-');
}

export function LibraryContentRoute() {
    const { contentId } = useParams<{ contentId: string }>();
    const { search } = useLocation();
    const { contentTypes, isLoading } = useLibraryTypes();

    if (isLoading) {
        return <RouteLoadingState />;
    }

    const segment = contentId ?? '';
    const normalizedSegment = normalizeSegment(segment);
    const matchingTypes = contentTypes.filter(
        (type) =>
            normalizeSegment(type.slug) === normalizedSegment ||
            normalizeSegment(type.pluralName) === normalizedSegment,
    );
    const slugMatches = matchingTypes.filter((type) => type.slug.toLowerCase() === segment.toLowerCase());
    const pluralMatches = matchingTypes.filter((type) => normalizeSegment(type.pluralName) === normalizedSegment);
    const contentType =
        matchingTypes.length === 1
            ? matchingTypes[0]
            : slugMatches.length === 1
              ? slugMatches[0]
              : pluralMatches.length === 1
                ? pluralMatches[0]
                : undefined;

    if (contentType) {
        return <Navigate to={`/library/content/${contentType.slug}${search}`} replace />;
    }

    return <ContentDetailPage />;
}

LibraryContentRoute.displayName = 'LibraryContentRoute';
