import { useQuery } from '@tanstack/react-query';

import { useAuth } from '@/auth';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface QualityAttributeField {
    name: string;

    label: string;

    isNumeric: boolean;
}

interface ApiQualityAttributesResponse {
    universalFields: QualityAttributeField[];
}

export const qualityFieldsQueryKeys = {
    all: ['qualityFields'] as const,
    qualityAttributes: () => ['qualityFields', 'quality-attributes'] as const,
};

async function fetchQualityAttributes(): Promise<ApiQualityAttributesResponse> {
    return apiGet<ApiQualityAttributesResponse>('/content-types/quality-attributes');
}

export function useQualityFields() {
    const { isAuthenticated } = useAuth();
    useInvalidateQueriesOnEvents(
        isAuthenticated
            ? [
                  'extension.installed',
                  'extension.enabled',
                  'extension.disabled',
                  'extension.uninstalled',
                  'extension.upgraded',
              ]
            : [],
        [qualityFieldsQueryKeys.qualityAttributes()],
        { exact: true, owner: 'quality-field-catalog' },
    );

    const query = useQuery({
        queryKey: qualityFieldsQueryKeys.qualityAttributes(),
        queryFn: fetchQualityAttributes,
        staleTime: POLLING_INTERVALS.slow,
        gcTime: 5 * 60 * 1000,
        enabled: isAuthenticated,
    });

    return {
        universalFields: query.data?.universalFields ?? [],
        isLoading: query.isLoading,
        error: queryErrorOrNull({ error: query.error }),
    };
}
