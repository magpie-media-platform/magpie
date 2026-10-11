import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

import { useIntegrationSettingsFormController } from './useIntegrationSettingsFormController';

interface Values {
    id?: string;
    name: string;
    provider: string;
    settings: Record<string, string>;
}

const emptyValues: Values = { name: '', provider: 'sample', settings: {} };

const schema: IntegrationSettingDefinition[] = [
    { key: 'url', type: SchemaDataType.String, displayName: 'URL', required: true },
    { key: 'category', type: SchemaDataType.String, displayName: 'Category', defaultValue: 'tv' },
];

function useSchema() {
    return { data: schema };
}

describe('useIntegrationSettingsFormController', () => {
    it('presents a first-open state again when it is reopened without initial values', async () => {
        // Arrange
        const { result, rerender } = renderHook(
            ({ open }: { open: boolean }) =>
                useIntegrationSettingsFormController<Values>({
                    open,
                    onOpenChange: vi.fn(),
                    emptyValues,
                    onSubmit: vi.fn(),
                    useSchema,
                }),
            { initialProps: { open: true } },
        );
        act(() => result.current.setValues((prev) => ({ ...prev, name: 'My Indexer' })));
        act(() => result.current.handleSettingChange('category', 'movies'));
        await act(async () => {
            await result.current.handleSubmit(null, () => false);
        });
        expect(result.current.combinedIssues.map((issue) => issue.message)).toEqual(['URL is required.']);

        // Act
        rerender({ open: false });
        rerender({ open: true });

        // Assert
        await waitFor(() => expect(result.current.settings).toEqual({ category: 'tv' }));
        expect(result.current.values.name).toBe('');
        expect(result.current.combinedIssues).toEqual([]);
        expect(result.current.isDirty).toBe(false);
    });

    it('stays clean when only schema defaults seed the settings', async () => {
        // Arrange
        const { result } = renderHook(() =>
            useIntegrationSettingsFormController<Values>({
                open: true,
                onOpenChange: vi.fn(),
                emptyValues,
                onSubmit: vi.fn(),
                useSchema,
            }),
        );

        // Act
        await waitFor(() => expect(result.current.settings).toEqual({ category: 'tv' }));

        // Assert
        expect(result.current.isDirty).toBe(false);
    });

    it('keeps the seeded defaults when the stored values refetch while open', async () => {
        // Arrange
        const stored: Values = { id: 'server-1', name: 'Server', provider: 'sample', settings: { url: 'http://host' } };
        const { result, rerender } = renderHook(
            ({ initialValues }: { initialValues: Values }) =>
                useIntegrationSettingsFormController<Values>({
                    open: true,
                    onOpenChange: vi.fn(),
                    initialValues,
                    emptyValues,
                    onSubmit: vi.fn(),
                    useSchema,
                }),
            { initialProps: { initialValues: stored } },
        );
        await waitFor(() => expect(result.current.settings).toEqual({ url: 'http://host', category: 'tv' }));

        // Act
        rerender({ initialValues: { ...stored } });

        // Assert
        expect(result.current.settings).toEqual({ url: 'http://host', category: 'tv' });
        expect(result.current.isDirty).toBe(false);
    });
});
