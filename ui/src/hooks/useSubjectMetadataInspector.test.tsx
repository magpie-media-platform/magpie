import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import {
    usePatchSubjectMetadataInspector,
    useReorderSubjectMetadataContributions,
    useSubjectMetadataInspector,
} from './useSubjectMetadataInspector';
import { subjectQueryKeys } from './useSubjects';
import * as api from '@/lib/api';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPatch: vi.fn(),
    };
});

function createQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false },
        },
    });
}

function createWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

describe('useSubjectMetadataInspector', () => {
    const subjectId = '11111111-1111-1111-1111-111111111111';

    it('composes subject metadata routes and resolves relationship subjects through the subject query', async () => {
        const queryClient = createQueryClient();
        const relatedContainerId = '44444444-4444-4444-4444-444444444444';
        const materialized = { containerId: '22222222-2222-2222-2222-222222222222', title: 'Keanu Reeves' };
        const contributions = [
            {
                id: '33333333-3333-3333-3333-333333333333',
                ordinal: 0,
                providerType: 'provider-c',
                title: 'Keanu Reeves',
                relationships: [
                    {
                        id: '66666666-6666-6666-6666-666666666666',
                        kind: 'collaborator',
                        ordinal: 0,
                        relatedMetadataContainerId: relatedContainerId,
                    },
                ],
            },
        ];
        const subjects = {
            [relatedContainerId]: {
                subjectId: '55555555-5555-5555-5555-555555555555',
                name: 'Carrie-Anne Moss',
                imageUrl: null,
            },
        };
        vi.mocked(api.apiGet).mockImplementation(async (url) => {
            if (url === `/subjects/${subjectId}/metadata`) {
                return materialized;
            }
            if (url === `/subjects/${subjectId}/metadata/contributions`) {
                return contributions;
            }
            if (url.startsWith('/subjects?')) {
                return {
                    items: [
                        {
                            id: subjects[relatedContainerId].subjectId,
                            metadataContainerId: relatedContainerId,
                            kind: 'person',
                            createdAt: '2026-01-01T00:00:00Z',
                            metadataContainer: {
                                id: relatedContainerId,
                                createdAt: '2026-01-01T00:00:00Z',
                                contributions: [
                                    {
                                        id: '77777777-7777-7777-7777-777777777777',
                                        ordinal: 0,
                                        locked: false,
                                        title: 'Carrie-Anne Moss',
                                    },
                                ],
                            },
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 500,
                    totalItems: 1,
                };
            }
            throw new Error(`Unexpected URL: ${url}`);
        });

        const { result } = renderHook(() => useSubjectMetadataInspector(subjectId), {
            wrapper: createWrapper(queryClient),
        });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(api.apiGet).toHaveBeenCalledWith(`/subjects/${subjectId}/metadata`);
        expect(api.apiGet).toHaveBeenCalledWith(`/subjects/${subjectId}/metadata/contributions`);
        expect(api.apiGet).toHaveBeenCalledWith('/subjects?pageNumber=1&pageSize=500');
        expect(result.current.data).toEqual({ materialized, contributions, subjects, subjectsTruncated: false });
    });

    it('stops the subject scan at fifty pages and reports the lookup as truncated', async () => {
        // Arrange
        const queryClient = createQueryClient();
        const relatedContainerId = '44444444-4444-4444-4444-444444444444';
        const materialized = { containerId: '22222222-2222-2222-2222-222222222222', title: 'Keanu Reeves' };
        const contributions = [
            {
                id: '33333333-3333-3333-3333-333333333333',
                ordinal: 0,
                providerType: 'provider-c',
                title: 'Keanu Reeves',
                relationships: [
                    {
                        id: '66666666-6666-6666-6666-666666666666',
                        kind: 'collaborator',
                        ordinal: 0,
                        relatedMetadataContainerId: relatedContainerId,
                    },
                ],
            },
        ];
        vi.mocked(api.apiGet).mockReset();
        vi.mocked(api.apiGet).mockImplementation(async (url) => {
            if (url === `/subjects/${subjectId}/metadata`) {
                return materialized;
            }
            if (url === `/subjects/${subjectId}/metadata/contributions`) {
                return contributions;
            }
            if (url.startsWith('/subjects?')) {
                return {
                    items: [
                        {
                            id: '99999999-9999-9999-9999-999999999999',
                            metadataContainerId: '88888888-8888-8888-8888-888888888888',
                            kind: 'person',
                            createdAt: '2026-01-01T00:00:00Z',
                            metadataContainer: {
                                id: '88888888-8888-8888-8888-888888888888',
                                createdAt: '2026-01-01T00:00:00Z',
                                contributions: [],
                            },
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 500,
                    totalItems: 100000,
                };
            }
            throw new Error(`Unexpected URL: ${url}`);
        });

        // Act
        const { result } = renderHook(() => useSubjectMetadataInspector(subjectId), {
            wrapper: createWrapper(queryClient),
        });
        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        // Assert
        const scanCalls = vi.mocked(api.apiGet).mock.calls.filter(([url]) => url.startsWith('/subjects?'));
        expect(scanCalls).toHaveLength(50);
        expect(result.current.data).toEqual({
            materialized,
            contributions,
            subjects: {},
            subjectsTruncated: true,
        });
    });

    it('PATCHes the subject metadata route and invalidates after the 204 response', async () => {
        const queryClient = createQueryClient();
        const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
        vi.mocked(api.apiPatch).mockResolvedValue(undefined);

        const { result } = renderHook(() => usePatchSubjectMetadataInspector(subjectId), {
            wrapper: createWrapper(queryClient),
        });

        result.current.mutate({ title: { value: 'Neo' } });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(api.apiPatch).toHaveBeenCalledWith(`/subjects/${subjectId}/metadata`, { title: { value: 'Neo' } });
        expect(invalidateQueries).toHaveBeenCalledWith({
            queryKey: [...subjectQueryKeys.detail(subjectId), 'metadata-inspector'],
        });
        expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: subjectQueryKeys.detail(subjectId) });
    });

    it('PATCHes the complete subject provider order and invalidates materialized metadata', async () => {
        const queryClient = createQueryClient();
        const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
        vi.mocked(api.apiPatch).mockResolvedValue(undefined);
        const { result } = renderHook(() => useReorderSubjectMetadataContributions(subjectId), {
            wrapper: createWrapper(queryClient),
        });

        result.current.mutate(['contrib-b', 'contrib-a']);

        await waitFor(() => expect(result.current.isSuccess).toBe(true));
        expect(api.apiPatch).toHaveBeenCalledWith(`/subjects/${subjectId}/metadata`, {
            contributionIds: ['contrib-b', 'contrib-a'],
        });
        expect(invalidateQueries).toHaveBeenCalledWith({
            queryKey: [...subjectQueryKeys.detail(subjectId), 'metadata-inspector'],
        });
        expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: subjectQueryKeys.detail(subjectId) });
    });
});
