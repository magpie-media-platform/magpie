import { useCallback, useState } from 'react';

import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { contentQueryKeys } from '@/hooks/useContent';
import { useEventStream, type EventStreamPayload } from '@/hooks/useEventStream';

export interface AddContentByExternalIdRequest {
    providerType: string;

    externalIdKey: string;

    externalId: string;

    contentType: string;

    levelSlug?: string;

    rootFolderId?: string;
}

async function createContentFromExternalId(request: AddContentByExternalIdRequest): Promise<string> {
    const { rootFolderId, ...body } = request;
    const path = rootFolderId
        ? `/content/from-external-id?rootFolderId=${encodeURIComponent(rootFolderId)}`
        : '/content/from-external-id';

    return apiPost<string>(path, body);
}

const METADATA_REFRESHED_EVENT_NAME = 'metadata.refreshed';
const METADATA_REFRESHED_ITEM_ID_KEY = 'content.hierarchyItemId';

export function useAddContentByExternalId() {
    const queryClient = useQueryClient();

    const [pendingIds, setPendingIds] = useState<readonly string[]>([]);

    const invalidateList = useCallback(() => {
        void queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
    }, [queryClient]);

    const settle = useCallback(
        (id: string) => {
            setPendingIds((prev) => prev.filter((pendingId) => pendingId !== id));
            invalidateList();
        },
        [invalidateList],
    );

    useEventStream(
        useCallback(
            (event: EventStreamPayload) => {
                const refreshedId = event.context[METADATA_REFRESHED_ITEM_ID_KEY];
                if (refreshedId && pendingIds.includes(refreshedId)) {
                    settle(refreshedId);
                }
            },
            [pendingIds, settle],
        ),
        {
            eventName: METADATA_REFRESHED_EVENT_NAME,
            enabled: pendingIds.length > 0,
        },
    );

    return useMutation({
        mutationFn: createContentFromExternalId,
        onSuccess: (newId) => {
            invalidateList();
            setPendingIds((prev) => (prev.includes(newId) ? prev : [...prev, newId]));
        },
    });
}
