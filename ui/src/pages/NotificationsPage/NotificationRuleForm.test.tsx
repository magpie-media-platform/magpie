import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

vi.mock('@/lib/api', async () => {
    const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
    return {
        ...actual,
        apiGet: vi.fn().mockResolvedValue([]),
    };
});

import { ApiError, apiGet } from '@/lib/api';
import { NotificationRuleForm } from './NotificationRuleForm';

const PROVIDERS = ['webhook', 'discord'];
const PROVIDER_LABELS = { webhook: 'Webhook', discord: 'Discord' };

const SCHEMA_WITH_DEFAULTS: IntegrationSettingDefinition[] = [
    { key: 'url', type: SchemaDataType.String, displayName: 'Webhook URL', required: true },
    { key: 'method', type: SchemaDataType.String, displayName: 'HTTP method', defaultValue: 'POST' },
];

function Wrapper({ children }: { children: ReactNode }) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: Infinity, staleTime: Infinity } },
    });
    return (
        <QueryClientProvider client={client}>
            <TooltipProvider>{children}</TooltipProvider>
        </QueryClientProvider>
    );
}

describe('NotificationRuleForm', () => {
    beforeEach(() => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/providers') {
                return Promise.resolve([
                    {
                        kind: 'notification',
                        providerType: 'webhook',
                        schema: SCHEMA_WITH_DEFAULTS,
                    },
                ]);
            }
            return Promise.resolve([]);
        });
    });

    it('opens the add form with a single provider without an update loop', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

        render(
            <Wrapper>
                <NotificationRuleForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    providers={['webhook']}
                    providerLabels={PROVIDER_LABELS}
                    onSubmit={vi.fn()}
                />
            </Wrapper>,
        );

        expect(await screen.findByDisplayValue('POST')).toBeInTheDocument();

        const loopError = errorSpy.mock.calls.some((call) =>
            call.some((arg) => typeof arg === 'string' && arg.includes('Maximum update depth exceeded')),
        );
        expect(loopError).toBe(false);

        errorSpy.mockRestore();
    });

    it('renders the add form with name and provider fields', () => {
        render(
            <Wrapper>
                <NotificationRuleForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    providers={PROVIDERS}
                    providerLabels={PROVIDER_LABELS}
                    onSubmit={vi.fn()}
                />
            </Wrapper>,
        );

        expect(screen.getByText('Add Notification Rule')).toBeInTheDocument();
        expect(screen.getByLabelText(/^Name/)).toBeInTheDocument();
    });

    it('shows a spinner and disables save while a rule mutation is pending', () => {
        render(
            <Wrapper>
                <NotificationRuleForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'rule-1',
                        name: 'My Rule',
                        providerType: 'webhook',
                        eventNamePattern: '',
                        contextFilters: [],
                        settings: {},
                        enabled: true,
                    }}
                    providers={PROVIDERS}
                    providerLabels={PROVIDER_LABELS}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />
            </Wrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toHaveAttribute('data-loading', 'true');
        expect(saveButton).toBeDisabled();
        expect(saveButton.querySelector('.button__spinner')).toBeInTheDocument();
    });

    it('surfaces API provider setting validation errors on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn().mockRejectedValue(
            new ApiError(400, 'Validation failed', {
                isValid: false,
                issues: [{ key: 'url', message: 'URL must be an absolute address.' }],
            }),
        );

        render(
            <Wrapper>
                <NotificationRuleForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    providers={['webhook']}
                    providerLabels={PROVIDER_LABELS}
                    onSubmit={onSubmit}
                />
            </Wrapper>,
        );

        await screen.findByLabelText(/Webhook URL/i);
        await user.type(screen.getByLabelText(/^Name/), 'Failures Webhook');
        await user.type(screen.getByLabelText(/Webhook URL/i), 'hooks/relative');
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(screen.getByText('URL must be an absolute address.')).toBeInTheDocument();
        });
        expect(onSubmit).toHaveBeenCalledOnce();
        expect(screen.getByLabelText(/Webhook URL/i)).toHaveAttribute('aria-invalid', 'true');
    });

    it('blocks saving and marks the field when a required provider setting is empty, even if disabled', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <Wrapper>
                <NotificationRuleForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    providers={['webhook']}
                    providerLabels={PROVIDER_LABELS}
                    onSubmit={onSubmit}
                />
            </Wrapper>,
        );

        await screen.findByLabelText(/Webhook URL/i);
        await user.type(screen.getByLabelText(/^Name/), 'QA rule');
        await user.click(screen.getByRole('switch', { name: 'Enabled' }));
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(await screen.findByText('Webhook URL is required.')).toBeInTheDocument();
        expect(screen.getByLabelText(/Webhook URL/i)).toHaveAttribute('aria-invalid', 'true');
        expect(screen.getByLabelText(/^Name/)).toHaveValue('QA rule');
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('moves focus to the first invalid field above the fold when Save is blocked', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <Wrapper>
                <NotificationRuleForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    providers={['webhook']}
                    providerLabels={PROVIDER_LABELS}
                    onSubmit={onSubmit}
                />
            </Wrapper>,
        );

        await screen.findByLabelText(/Webhook URL/i);
        await user.click(screen.getByRole('button', { name: /add filter/i }));
        await user.type(screen.getByLabelText(/Webhook URL/i), 'https://example.test/hook');
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(screen.getByLabelText(/^Name/)).toHaveFocus());
        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(screen.getByText('Key is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows a retryable schema error instead of treating it as no settings', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet)
            .mockRejectedValueOnce(new Error('Provider catalog unavailable'))
            .mockResolvedValueOnce([
                {
                    kind: 'notification',
                    providerType: 'webhook',
                    schema: SCHEMA_WITH_DEFAULTS,
                },
            ]);

        render(
            <Wrapper>
                <NotificationRuleForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'rule-1',
                        name: 'My Rule',
                        providerType: 'webhook',
                        eventNamePattern: '',
                        contextFilters: [],
                        settings: {},
                        enabled: true,
                    }}
                    providers={PROVIDERS}
                    providerLabels={PROVIDER_LABELS}
                    onSubmit={vi.fn()}
                />
            </Wrapper>,
        );

        expect(await screen.findByText('Failed to load settings for webhook.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();

        await user.click(screen.getByRole('button', { name: 'Retry provider settings' }));

        expect(await screen.findByLabelText(/Webhook URL/i)).toBeInTheDocument();
    });

    describe('schema-default dirty baseline', () => {
        it('stays not-dirty after schema defaults seed with no user edits', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();

            render(
                <Wrapper>
                    <NotificationRuleForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={{
                            id: 'rule-1',
                            name: 'My Rule',
                            providerType: 'webhook',
                            eventNamePattern: '',
                            contextFilters: [],
                            settings: {},
                            enabled: true,
                        }}
                        providers={PROVIDERS}
                        providerLabels={PROVIDER_LABELS}
                        onSubmit={vi.fn()}
                    />
                </Wrapper>,
            );

            await screen.findByDisplayValue('POST');

            await user.click(screen.getByText('Cancel'));

            expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
            expect(onOpenChange).toHaveBeenCalledWith(false);
        });

        it('becomes dirty after a real edit to a seeded-default form', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();

            render(
                <Wrapper>
                    <NotificationRuleForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={{
                            id: 'rule-1',
                            name: 'My Rule',
                            providerType: 'webhook',
                            eventNamePattern: '',
                            contextFilters: [],
                            settings: {},
                            enabled: true,
                        }}
                        providers={PROVIDERS}
                        providerLabels={PROVIDER_LABELS}
                        onSubmit={vi.fn()}
                    />
                </Wrapper>,
            );

            const methodInput = await screen.findByDisplayValue('POST');
            await user.clear(methodInput);
            await user.type(methodInput, 'PUT');

            await user.click(screen.getByText('Cancel'));

            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
            expect(onOpenChange).not.toHaveBeenCalled();
        });
    });

    describe('context-filter dirty baseline', () => {
        const baseInitial = {
            id: 'rule-1',
            name: 'My Rule',
            providerType: 'webhook',
            eventNamePattern: '',
            contextFilters: [],
            settings: {},
            enabled: true,
        };

        it('stays not-dirty after inserting a blank context filter', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();

            render(
                <Wrapper>
                    <NotificationRuleForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={baseInitial}
                        providers={PROVIDERS}
                        providerLabels={PROVIDER_LABELS}
                        onSubmit={vi.fn()}
                    />
                </Wrapper>,
            );

            await screen.findByDisplayValue('POST');
            await user.click(screen.getByRole('button', { name: /add filter/i }));
            await user.click(screen.getByText('Cancel'));

            expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
            expect(onOpenChange).toHaveBeenCalledWith(false);
        });

        it('becomes dirty after filling a context-filter key', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();

            render(
                <Wrapper>
                    <NotificationRuleForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={baseInitial}
                        providers={PROVIDERS}
                        providerLabels={PROVIDER_LABELS}
                        onSubmit={vi.fn()}
                    />
                </Wrapper>,
            );

            await screen.findByDisplayValue('POST');
            await user.click(screen.getByRole('button', { name: /add filter/i }));
            await user.type(screen.getByLabelText(/filter 1 key/i), 'contentType');
            await user.click(screen.getByText('Cancel'));

            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
            expect(onOpenChange).not.toHaveBeenCalled();
        });
    });
});
