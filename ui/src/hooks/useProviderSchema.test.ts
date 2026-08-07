import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

import { apiGet } from '@/lib/api';
import {
    SchemaDataType,
    omitUnchangedSecretSettings,
    useDownloadClientSchema,
    useIndexerSchema,
    useMediaServerSchema,
    type IntegrationSettingDefinition,
} from './useProviderSchema';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

const sampleSchema: IntegrationSettingDefinition[] = [
    { key: 'host', type: SchemaDataType.String, required: true },
    { key: 'port', type: SchemaDataType.Number },
];

function providerDescriptor(kind: string, providerType: string) {
    return { kind, providerType, schema: sampleSchema };
}

describe('useProviderSchema hooks', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('useDownloadClientSchema', () => {
        it('is disabled when providerType is null', () => {
            const { result } = renderHook(() => useDownloadClientSchema(null), { wrapper: createWrapper() });

            expect(result.current.fetchStatus).toBe('idle');
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        it('is disabled when providerType is empty string', () => {
            const { result } = renderHook(() => useDownloadClientSchema(''), { wrapper: createWrapper() });

            expect(result.current.fetchStatus).toBe('idle');
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        it('fetches the schema when providerType is set', async () => {
            mockApiGet.mockResolvedValueOnce([providerDescriptor('downloadClient', 'downloadclient-a')]);

            const { result } = renderHook(() => useDownloadClientSchema('downloadclient-a'), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/providers');
            expect(result.current.data).toEqual(sampleSchema);
        });

        it('selects the requested provider schema from the catalog', async () => {
            mockApiGet.mockResolvedValueOnce([
                providerDescriptor('downloadClient', 'other'),
                providerDescriptor('downloadClient', 'foo bar'),
            ]);

            const { result } = renderHook(() => useDownloadClientSchema('foo bar'), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.data).toEqual(sampleSchema);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/providers');
        });
    });

    describe('useMediaServerSchema', () => {
        it('selects a media server schema from the provider catalog', async () => {
            mockApiGet.mockResolvedValueOnce([providerDescriptor('mediaServer', 'mediaserver-a')]);

            const { result } = renderHook(() => useMediaServerSchema('mediaserver-a'), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.data).toEqual(sampleSchema);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/providers');
        });
    });

    describe('useIndexerSchema', () => {
        it('selects an indexer schema from the provider catalog', async () => {
            mockApiGet.mockResolvedValueOnce([providerDescriptor('indexer', 'newznab')]);

            const { result } = renderHook(() => useIndexerSchema('newznab'), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.data).toEqual(sampleSchema);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/providers');
        });
    });
});

describe('omitUnchangedSecretSettings', () => {
    const secretSchema: IntegrationSettingDefinition[] = [
        { key: 'host', type: SchemaDataType.String, required: true },
        { key: 'password', type: SchemaDataType.String, secret: true },
    ];

    it('omits a schema-marked secret left blank', () => {
        const result = omitUnchangedSecretSettings({ host: 'localhost', password: '' }, secretSchema);

        expect(result).toEqual({ host: 'localhost' });
    });

    it('omits a schema-marked secret whose value is nullish', () => {
        // an untouched secret can reach this transform as null (not an empty
        // string); it must still be dropped so the stored secret is kept rather
        // than overwritten with a null credential
        const result = omitUnchangedSecretSettings(
            { host: 'localhost', password: null as unknown as string },
            secretSchema,
        );

        expect(result).toEqual({ host: 'localhost' });
    });

    it('keeps a newly entered secret', () => {
        const result = omitUnchangedSecretSettings({ host: 'localhost', password: 'new-secret' }, secretSchema);

        expect(result).toEqual({ host: 'localhost', password: 'new-secret' });
    });

    it('keeps a blank non-secret field', () => {
        const result = omitUnchangedSecretSettings({ host: '', password: 'kept' }, secretSchema);

        expect(result).toEqual({ host: '', password: 'kept' });
    });
});
