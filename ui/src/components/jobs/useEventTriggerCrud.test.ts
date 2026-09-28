import { renderHook, act } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { useEventTriggerCrud } from './useEventTriggerCrud';
import type { ApiEventTrigger } from '@/hooks/useEventTriggers';
import { ApiError } from '@/lib/api';

const mockToast = vi.fn();
const mockCreateTrigger = vi.fn();
const mockUpdateTrigger = vi.fn();
const mockDeleteTrigger = vi.fn();

let mockTriggers: ApiEventTrigger[] = [];

vi.mock('@/hooks/useEventTriggers', () => ({
    useEventTriggers: () => ({
        triggers: mockTriggers,
        createTrigger: mockCreateTrigger,
        updateTrigger: mockUpdateTrigger,
        deleteTrigger: mockDeleteTrigger,
        isCreating: false,
        isUpdating: false,
        isDeleting: false,
        isLoading: false,
        refetch: vi.fn(),
    }),
}));

vi.mock('@/components/ui/Toast', async () => {
    const actual = await vi.importActual<typeof import('@/components/ui/Toast')>('@/components/ui/Toast');
    return { ...actual, useToast: () => ({ toast: mockToast }) };
});

function makeTrigger(overrides: Partial<ApiEventTrigger> = {}): ApiEventTrigger {
    return {
        id: 'trigger-1',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'Content',
        name: 'Test Trigger',
        eventFilter: { eventName: { value: 'content.added' } },
        enabled: true,
        delaySeconds: 0,
        createdAt: '2024-01-01T00:00:00Z',
        ...overrides,
    };
}

describe('useEventTriggerCrud', () => {
    beforeEach(() => {
        mockToast.mockReset();
        mockCreateTrigger.mockReset();
        mockUpdateTrigger.mockReset();
        mockDeleteTrigger.mockReset();
        mockTriggers = [];
    });

    it('emits operator regex with a single value for a single event name pattern', async () => {
        const { result } = renderHook(() => useEventTriggerCrud());

        await act(async () => {
            await result.current.handleSubmit({
                name: 'My Trigger',
                templateId: 'ContentMetadataRefreshItem',
                eventNamePatterns: ['content.added'],
                filters: [],
                enabled: true,
                delaySeconds: 0,
                parameters: {},
            });
        });

        expect(mockCreateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({
                eventFilter: expect.objectContaining({
                    eventName: { operator: 'regex', value: 'content.added' },
                }),
                delaySeconds: 0,
            }),
        );
    });

    it('emits operator In with values for multiple event name patterns', async () => {
        const { result } = renderHook(() => useEventTriggerCrud());

        await act(async () => {
            await result.current.handleSubmit({
                name: 'My Trigger',
                templateId: 'ContentMetadataRefreshItem',
                eventNamePatterns: ['content.added', 'content.updated', ''],
                filters: [],
                enabled: true,
                delaySeconds: 5,
                parameters: {},
            });
        });

        expect(mockCreateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({
                eventFilter: expect.objectContaining({
                    eventName: { operator: 'In', values: ['content.added', 'content.updated'] },
                }),
                delaySeconds: 5,
            }),
        );
    });

    it('includes delaySeconds in the patch when editing an existing trigger', async () => {
        const { result } = renderHook(() => useEventTriggerCrud());

        await act(async () => {
            await result.current.handleSubmit({
                id: 'trigger-1',
                name: 'My Trigger',
                templateId: 'ContentMetadataRefreshItem',
                eventNamePatterns: ['content.added'],
                filters: [],
                enabled: true,
                delaySeconds: 20,
                parameters: {},
            });
        });

        expect(mockUpdateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({
                id: 'trigger-1',
                patch: expect.objectContaining({ delaySeconds: { value: 20 } }),
            }),
        );
    });

    it('stores event binding expressions in trigger parameters', async () => {
        const { result } = renderHook(() => useEventTriggerCrud());

        await act(async () => {
            await result.current.handleSubmit({
                name: 'Bound Trigger',
                templateId: 'ContentMetadataRefreshItem',
                eventNamePatterns: ['content.added'],
                filters: [],
                enabled: true,
                delaySeconds: 0,
                parameters: { contentItemId: '${event.content.itemId}' },
            });
        });

        expect(mockCreateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({ parameters: { contentItemId: '${event.content.itemId}' } }),
        );
    });

    it('patches changed event trigger parameters', async () => {
        mockTriggers = [makeTrigger({ parameters: { contentItemId: 'static', removed: 'old' } })];
        const { result } = renderHook(() => useEventTriggerCrud());

        act(() => {
            result.current.handleEdit(mockTriggers[0]);
        });

        await act(async () => {
            await result.current.handleSubmit({
                ...result.current.formInitialValues!,
                parameters: {
                    contentItemId: '${event.content.itemId}',
                    added: 'new',
                },
            });
        });

        expect(mockUpdateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({
                patch: expect.objectContaining({
                    parameters: {
                        add: { added: 'new' },
                        update: { contentItemId: '${event.content.itemId}' },
                        remove: ['removed'],
                    },
                }),
            }),
        );
    });

    it('hydrates formInitialValues with every pattern from an In-operator trigger', () => {
        mockTriggers = [
            makeTrigger({
                eventFilter: { eventName: { operator: 'In', values: ['a.b', 'c.d', 'e.f'] } },
                delaySeconds: 15,
            }),
        ];

        const { result } = renderHook(() => useEventTriggerCrud());

        act(() => {
            result.current.handleEdit(mockTriggers[0]);
        });

        expect(result.current.formInitialValues).toEqual(
            expect.objectContaining({
                eventNamePatterns: ['a.b', 'c.d', 'e.f'],
                delaySeconds: 15,
                parameters: {},
            }),
        );
    });

    it('round-trips an unchanged In criterion with its operator and complete value set', async () => {
        mockTriggers = [
            makeTrigger({
                eventFilter: {
                    eventName: { operator: 'In', values: ['download.imported', 'download.failed'] },
                },
            }),
        ];

        const { result } = renderHook(() => useEventTriggerCrud());

        act(() => {
            result.current.handleEdit(mockTriggers[0]);
        });

        await act(async () => {
            await result.current.handleSubmit(result.current.formInitialValues!);
        });

        expect(mockUpdateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({
                id: 'trigger-1',
                patch: expect.objectContaining({
                    eventFilter: {
                        value: expect.objectContaining({
                            eventName: {
                                operator: 'In',
                                values: ['download.imported', 'download.failed'],
                            },
                        }),
                    },
                }),
            }),
        );
    });

    it('round-trips context criteria through create, read, edit and patch with the items wrapper', async () => {
        const { result, rerender } = renderHook(() => useEventTriggerCrud());

        await act(async () => {
            await result.current.handleSubmit({
                name: 'QA Trigger',
                templateId: 'ContentMetadataRefreshItem',
                eventNamePatterns: ['content.updated'],
                filters: [
                    {
                        key: 'qa.key',
                        pattern: 'qa.value',
                        operator: 'regex',
                        values: null,
                        negate: true,
                    },
                ],
                enabled: true,
                delaySeconds: 0,
                parameters: {},
            });
        });

        const createRequest = mockCreateTrigger.mock.calls[0][0];
        expect(createRequest.eventFilter.eventContext).toEqual({
            items: {
                'qa.key': {
                    operator: 'regex',
                    value: 'qa.value',
                    negate: true,
                },
            },
        });

        mockTriggers = [
            makeTrigger({
                name: createRequest.name,
                eventFilter: createRequest.eventFilter,
            }),
        ];
        rerender();

        act(() => {
            result.current.handleEdit(mockTriggers[0]);
        });

        expect(result.current.formInitialValues?.filters).toEqual([
            {
                key: 'qa.key',
                pattern: 'qa.value',
                operator: 'regex',
                values: null,
                negate: true,
            },
        ]);

        await act(async () => {
            await result.current.handleSubmit(result.current.formInitialValues!);
        });

        expect(mockUpdateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({
                id: 'trigger-1',
                patch: expect.objectContaining({
                    eventFilter: {
                        value: expect.objectContaining({
                            eventContext: createRequest.eventFilter.eventContext,
                        }),
                    },
                }),
            }),
        );
    });

    it('preserves non-regex and set-valued context criteria when an event trigger is edited', async () => {
        mockTriggers = [
            makeTrigger({
                eventFilter: {
                    eventName: { operator: 'In', values: ['content.updated', 'content.created'] },
                    eventContext: {
                        items: {
                            'content.changedFields': {
                                operator: 'ContainsString',
                                value: 'EffectiveMonitoring',
                                negate: true,
                            },
                            'qa.tags': {
                                operator: 'In',
                                values: ['qa.key', 'qa.value'],
                                negate: false,
                            },
                        },
                    },
                },
            }),
        ];

        const { result } = renderHook(() => useEventTriggerCrud());

        act(() => {
            result.current.handleEdit(mockTriggers[0]);
        });

        await act(async () => {
            await result.current.handleSubmit(result.current.formInitialValues!);
        });

        expect(mockUpdateTrigger).toHaveBeenCalledWith(
            expect.objectContaining({
                patch: expect.objectContaining({
                    eventFilter: {
                        value: {
                            eventName: {
                                operator: 'In',
                                values: ['content.updated', 'content.created'],
                            },
                            eventContext: {
                                items: {
                                    'content.changedFields': {
                                        operator: 'ContainsString',
                                        value: 'EffectiveMonitoring',
                                        negate: true,
                                    },
                                    'qa.tags': {
                                        operator: 'In',
                                        values: ['qa.key', 'qa.value'],
                                        negate: false,
                                    },
                                },
                            },
                        },
                    },
                }),
            }),
        );
    });

    it('hydrates a single-value eventName trigger into a single-element array', () => {
        mockTriggers = [makeTrigger({ eventFilter: { eventName: { value: 'content.added' } } })];

        const { result } = renderHook(() => useEventTriggerCrud());

        act(() => {
            result.current.handleEdit(mockTriggers[0]);
        });

        expect(result.current.formInitialValues?.eventNamePatterns).toEqual(['content.added']);
    });

    it.each([
        [400, 'A user trigger must target a specific item.', 'A user trigger must target a specific item.'],
        [500, 'Request failed with status 500', 'The server request failed. Try again later.'],
    ])('shows the actionable reason when a save fails with status %i', async (status, message, description) => {
        const error = new ApiError(status, message);
        mockCreateTrigger.mockRejectedValue(error);
        const { result } = renderHook(() => useEventTriggerCrud());

        await act(async () => {
            await expect(
                result.current.handleSubmit({
                    name: 'My Trigger',
                    templateId: 'ContentMetadataRefreshItem',
                    eventNamePatterns: ['content.added'],
                    filters: [],
                    enabled: false,
                    delaySeconds: 0,
                    parameters: {},
                }),
            ).rejects.toBe(error);
        });

        expect(mockToast).toHaveBeenCalledWith({ title: 'Error', description, variant: 'error' });
    });
});
