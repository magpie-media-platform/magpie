import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ExtensionContext } from '@/extensions/ExtensionProvider';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

import { IntegrationSettingsForm } from './IntegrationSettingsForm';
import { parseMappingRows } from './parseMappingRows';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';

describe('IntegrationSettingsForm', () => {
    describe('input type rendering', () => {
        it('renders a text input for String type without options', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String, displayName: 'Host' }]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const input = screen.getByLabelText('Host') as HTMLInputElement;
            expect(input).toBeInTheDocument();
            expect(input.type).toBe('text');
        });

        it('renders a password input when Secret is true', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true }]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const input = screen.getByLabelText('API Key') as HTMLInputElement;
            expect(input.type).toBe('password');
        });

        it('renders a number input for Number type', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'port', type: SchemaDataType.Number, displayName: 'Port' }]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const input = screen.getByLabelText('Port') as HTMLInputElement;
            expect(input.type).toBe('number');
        });

        it('renders a date input for Date type', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'effectiveOn', type: SchemaDataType.Date, displayName: 'Effective Date' }]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const input = screen.getByLabelText('Effective Date') as HTMLInputElement;
            expect(input.type).toBe('date');
        });

        it('renders a switch for Boolean type', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'useSsl', type: SchemaDataType.Boolean, displayName: 'Use SSL' }]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const toggle = screen.getByRole('switch', { name: 'Use SSL' });
            expect(toggle).toBeInTheDocument();
        });

        it('renders a select dropdown when Options is set on a String entry', () => {
            render(
                <IntegrationSettingsForm
                    schema={[
                        {
                            key: 'protocol',
                            type: SchemaDataType.String,
                            displayName: 'Protocol',
                            options: ['http', 'https'],
                        },
                    ]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const trigger = document.querySelector('[data-field="protocol"]');
            expect(trigger).toBeInTheDocument();
            expect(trigger?.tagName).toBe('BUTTON');
        });
    });

    describe('labels and help text', () => {
        it('uses key as fallback label when displayName is missing', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String }]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            expect(screen.getByLabelText('host')).toBeInTheDocument();
        });

        it('renders the description as help text below the input', () => {
            render(
                <IntegrationSettingsForm
                    schema={[
                        {
                            key: 'host',
                            type: SchemaDataType.String,
                            displayName: 'Host',
                            description: 'Hostname of the server',
                        },
                    ]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            expect(screen.getByText('Hostname of the server')).toBeInTheDocument();
        });

        it('marks required fields with the label primitive', () => {
            render(
                <IntegrationSettingsForm
                    schema={[
                        {
                            key: 'host',
                            type: SchemaDataType.String,
                            displayName: 'Host',
                            required: true,
                        },
                    ]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const required = document.querySelector('[data-setting-key="host"] .label__required');
            expect(required).toBeInTheDocument();
        });

        it('uses the placeholder on string inputs', () => {
            render(
                <IntegrationSettingsForm
                    schema={[
                        {
                            key: 'host',
                            type: SchemaDataType.String,
                            displayName: 'Host',
                            placeholder: 'localhost',
                        },
                    ]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            expect(screen.getByPlaceholderText('localhost')).toBeInTheDocument();
        });
    });

    describe('value display', () => {
        it('shows the current value in a text input', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String, displayName: 'Host' }]}
                    values={{ host: 'example.com' }}
                    onChange={vi.fn()}
                />,
            );

            expect(screen.getByDisplayValue('example.com')).toBeInTheDocument();
        });

        it('checks the switch when value is "true"', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'useSsl', type: SchemaDataType.Boolean, displayName: 'Use SSL' }]}
                    values={{ useSsl: 'true' }}
                    onChange={vi.fn()}
                />,
            );

            const toggle = screen.getByRole('switch', { name: 'Use SSL' });
            expect(toggle).toHaveAttribute('data-state', 'checked');
        });

        it('does not check the switch when value is "false"', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'useSsl', type: SchemaDataType.Boolean, displayName: 'Use SSL' }]}
                    values={{ useSsl: 'false' }}
                    onChange={vi.fn()}
                />,
            );

            const toggle = screen.getByRole('switch', { name: 'Use SSL' });
            expect(toggle).toHaveAttribute('data-state', 'unchecked');
        });

        it('renders an unconfigured secret as an ordinary editable password with no unlock button', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true }]}
                    values={{}}
                    onChange={vi.fn()}
                />,
            );

            const input = screen.getByLabelText('API Key') as HTMLInputElement;
            expect(input).toHaveAttribute('type', 'password');
            expect(input).not.toHaveAttribute('readonly');
            expect(input).not.toHaveAttribute('placeholder', '********');
            expect(screen.queryByRole('button', { name: 'Replace API Key' })).not.toBeInTheDocument();
        });

        it('masks and locks a configured secret behind a clear button', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true }]}
                    values={{}}
                    onChange={vi.fn()}
                    secretsConfigured={{ apiKey: true }}
                />,
            );

            const input = screen.getByLabelText('API Key') as HTMLInputElement;
            expect(input.value).toBe('');
            expect(input).toHaveAttribute('readonly');
            expect(input).toHaveAttribute('placeholder', '********');
            expect(screen.getByRole('button', { name: 'Replace API Key' })).toBeInTheDocument();
        });

        it('unlocks a configured secret for editing when the clear button is clicked', async () => {
            const user = userEvent.setup();
            const onChange = vi.fn();
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true }]}
                    values={{}}
                    onChange={onChange}
                    secretsConfigured={{ apiKey: true }}
                />,
            );

            await user.click(screen.getByRole('button', { name: 'Replace API Key' }));

            const input = screen.getByLabelText('API Key') as HTMLInputElement;
            expect(input).not.toHaveAttribute('readonly');
            expect(input).toHaveFocus();

            await user.type(input, 'x');
            expect(onChange).toHaveBeenCalledWith('apiKey', 'x');
            expect(screen.queryByRole('button', { name: 'Replace API Key' })).not.toBeInTheDocument();
        });
    });

    describe('change events', () => {
        it('calls onChange with new string value as user types', async () => {
            const user = userEvent.setup();
            const onChange = vi.fn();
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String, displayName: 'Host' }]}
                    values={{}}
                    onChange={onChange}
                />,
            );

            const input = screen.getByLabelText('Host');
            await user.type(input, 'a');

            expect(onChange).toHaveBeenCalledWith('host', 'a');
        });

        it('emits "true" / "false" strings for Boolean toggles', async () => {
            const user = userEvent.setup();
            const onChange = vi.fn();
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'useSsl', type: SchemaDataType.Boolean, displayName: 'Use SSL' }]}
                    values={{}}
                    onChange={onChange}
                />,
            );

            const toggle = screen.getByRole('switch', { name: 'Use SSL' });
            await user.click(toggle);

            expect(onChange).toHaveBeenLastCalledWith('useSsl', 'true');
        });
    });

    describe('issues', () => {
        it('renders a per-field issue beneath the input', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String, displayName: 'Host' }]}
                    values={{}}
                    onChange={vi.fn()}
                    issues={[{ key: 'host', message: 'Host is required' }]}
                />,
            );

            expect(screen.getByText('Host is required')).toBeInTheDocument();
        });

        it('renders a form-level issue (key=null) at the bottom', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String, displayName: 'Host' }]}
                    values={{}}
                    onChange={vi.fn()}
                    issues={[{ key: null, message: 'Connection failed' }]}
                />,
            );

            const formIssues = document.querySelector('[data-field="integration-settings-form-issues"]');
            expect(formIssues).toBeInTheDocument();
            expect(formIssues?.textContent).toContain('Connection failed');
        });

        it('does not render the form-level issues block when no key=null issues exist', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String, displayName: 'Host' }]}
                    values={{}}
                    onChange={vi.fn()}
                    issues={[{ key: 'host', message: 'Host is required' }]}
                />,
            );

            expect(document.querySelector('[data-field="integration-settings-form-issues"]')).not.toBeInTheDocument();
        });
    });

    describe('disabled', () => {
        it('disables string inputs', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'host', type: SchemaDataType.String, displayName: 'Host' }]}
                    values={{}}
                    onChange={vi.fn()}
                    disabled={true}
                />,
            );

            expect(screen.getByLabelText('Host')).toBeDisabled();
        });

        it('disables switches', () => {
            render(
                <IntegrationSettingsForm
                    schema={[{ key: 'useSsl', type: SchemaDataType.Boolean, displayName: 'Use SSL' }]}
                    values={{}}
                    onChange={vi.fn()}
                    disabled={true}
                />,
            );

            expect(screen.getByRole('switch', { name: 'Use SSL' })).toBeDisabled();
        });

        it('disables select triggers', () => {
            render(
                <IntegrationSettingsForm
                    schema={[
                        {
                            key: 'protocol',
                            type: SchemaDataType.String,
                            displayName: 'Protocol',
                            options: ['http', 'https'],
                        },
                    ]}
                    values={{}}
                    onChange={vi.fn()}
                    disabled={true}
                />,
            );

            const trigger = document.querySelector('[data-field="protocol"]');
            expect(trigger).toBeDisabled();
        });
    });

    describe('MappingsList editor', () => {
        const mappingsSchema: IntegrationSettingDefinition[] = [
            {
                key: 'libraryMappings',
                type: SchemaDataType.MappingsList,
                displayName: 'Library mappings',
                rowSchema: [
                    { key: 'rootFolderId', type: SchemaDataType.String, displayName: 'Root folder id', required: true },
                    { key: 'contentType', type: SchemaDataType.String, displayName: 'Content type', required: true },
                    {
                        key: 'serverLibraryId',
                        type: SchemaDataType.String,
                        displayName: 'Server library id',
                        required: true,
                    },
                ],
            },
        ];

        it('preserves structured mapping values as JSON text', () => {
            const rows = parseMappingRows(
                JSON.stringify([
                    {
                        objectValue: { id: 'rf-1' },
                        arrayValue: ['shows', 'movies'],
                        numberValue: 42,
                        booleanValue: true,
                        nullValue: null,
                    },
                ]),
            );

            expect(rows).toEqual([
                {
                    objectValue: '{"id":"rf-1"}',
                    arrayValue: '["shows","movies"]',
                    numberValue: '42',
                    booleanValue: 'true',
                    nullValue: '',
                },
            ]);
        });

        it('renders an empty-state row when the value is empty', () => {
            render(<IntegrationSettingsForm schema={mappingsSchema} values={{}} onChange={vi.fn()} />);

            expect(
                document.querySelector('[data-field="libraryMappings"][data-field-kind="mappings-list"]'),
            ).toBeInTheDocument();
            expect(screen.getByText(/No rows configured/)).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Add row' })).toBeInTheDocument();
        });

        it('renders one row per persisted entry with column inputs prefilled', () => {
            render(
                <IntegrationSettingsForm
                    schema={mappingsSchema}
                    values={{
                        libraryMappings: JSON.stringify([
                            {
                                rootFolderId: 'rf-1',
                                contentType: 'movies',
                                serverLibraryId: 'lib-1',
                            },
                            {
                                rootFolderId: 'rf-2',
                                contentType: 'shows',
                                serverLibraryId: 'lib-2',
                            },
                        ]),
                    }}
                    onChange={vi.fn()}
                />,
            );

            const inputs = document.querySelectorAll('[data-row-index] input');
            expect(inputs.length).toBe(6);
            expect((inputs[0] as HTMLInputElement).value).toBe('rf-1');
            expect((inputs[5] as HTMLInputElement).value).toBe('lib-2');
        });

        it('adds a row when the Add row button is clicked', async () => {
            const onChange = vi.fn();
            render(<IntegrationSettingsForm schema={mappingsSchema} values={{}} onChange={onChange} />);

            const user = userEvent.setup();
            await user.click(screen.getByRole('button', { name: 'Add row' }));

            expect(onChange).toHaveBeenCalledTimes(1);
            const [key, value] = onChange.mock.calls[0]!;
            expect(key).toBe('libraryMappings');
            expect(JSON.parse(value as string)).toEqual([{ rootFolderId: '', contentType: '', serverLibraryId: '' }]);
        });

        it('removes a row when the row Remove button is clicked', async () => {
            const onChange = vi.fn();
            render(
                <IntegrationSettingsForm
                    schema={mappingsSchema}
                    values={{
                        libraryMappings: JSON.stringify([
                            { rootFolderId: 'rf-1', contentType: 'movies', serverLibraryId: 'lib-1' },
                            { rootFolderId: 'rf-2', contentType: 'shows', serverLibraryId: 'lib-2' },
                        ]),
                    }}
                    onChange={onChange}
                />,
            );

            const user = userEvent.setup();
            await user.click(screen.getByRole('button', { name: 'Remove row 1' }));

            expect(onChange).toHaveBeenCalledTimes(1);
            const [key, value] = onChange.mock.calls[0]!;
            expect(key).toBe('libraryMappings');
            expect(JSON.parse(value as string)).toEqual([
                { rootFolderId: 'rf-2', contentType: 'shows', serverLibraryId: 'lib-2' },
            ]);
        });

        it('serialises a single-character edit back to JSON', async () => {
            const onChange = vi.fn();
            render(
                <IntegrationSettingsForm
                    schema={mappingsSchema}
                    values={{
                        libraryMappings: JSON.stringify([
                            { rootFolderId: 'rf-1', contentType: 'movies', serverLibraryId: 'lib-1' },
                        ]),
                    }}
                    onChange={onChange}
                />,
            );

            const user = userEvent.setup();
            const cell = document.querySelector(
                '[data-row-index="0"] input[data-field="serverLibraryId"]',
            ) as HTMLInputElement;
            await user.type(cell, '9');

            const final = onChange.mock.calls.at(-1)!;
            expect(final[0]).toBe('libraryMappings');
            const persisted = JSON.parse(final[1] as string);
            expect(persisted[0].serverLibraryId).toBe('lib-19');
        });

        it('disables row controls when disabled is true', () => {
            render(
                <IntegrationSettingsForm
                    schema={mappingsSchema}
                    values={{
                        libraryMappings: JSON.stringify([
                            { rootFolderId: 'rf-1', contentType: 'movies', serverLibraryId: 'lib-1' },
                        ]),
                    }}
                    onChange={vi.fn()}
                    disabled={true}
                />,
            );

            expect(screen.getByRole('button', { name: 'Add row' })).toBeDisabled();
            expect(screen.getByRole('button', { name: 'Remove row 1' })).toBeDisabled();
            const cellInputs = document.querySelectorAll('[data-row-index] input');
            expect(Array.from(cellInputs).map((input) => (input as HTMLInputElement).disabled)).toEqual([
                true,
                true,
                true,
            ]);
        });

        describe('secret cells', () => {
            const secretMappingsSchema: IntegrationSettingDefinition[] = [
                {
                    key: 'credentials',
                    type: SchemaDataType.MappingsList,
                    displayName: 'Credentials',
                    rowSchema: [
                        { key: 'account', type: SchemaDataType.String, displayName: 'Account', required: true },
                        { key: 'token', type: SchemaDataType.String, displayName: 'Token', secret: true },
                    ],
                },
            ];

            function ControlledMappings({ initialValue }: { initialValue?: string }) {
                const [values, setValues] = useState<Record<string, string>>(
                    initialValue !== undefined ? { credentials: initialValue } : {},
                );
                return (
                    <IntegrationSettingsForm
                        schema={secretMappingsSchema}
                        values={values}
                        onChange={(key, value) => setValues((prev) => ({ ...prev, [key]: value }))}
                    />
                );
            }

            it('masks a stored secret cell for a pre-existing mapping row', () => {
                render(
                    <IntegrationSettingsForm
                        schema={secretMappingsSchema}
                        values={{ credentials: JSON.stringify([{ account: 'acct-1', token: '' }]) }}
                        onChange={vi.fn()}
                    />,
                );

                const cell = document.querySelector(
                    '[data-row-index="0"] input[data-field="token"]',
                ) as HTMLInputElement;
                expect(cell).toHaveAttribute('type', 'password');
                expect(cell).toHaveAttribute('readonly');
                expect(cell).toHaveAttribute('placeholder', '********');
                expect(screen.getByRole('button', { name: 'Replace Token' })).toBeInTheDocument();
            });

            it('unlocks a stored secret cell and flows the replacement through onChange', async () => {
                const user = userEvent.setup();
                const onChange = vi.fn();
                render(
                    <IntegrationSettingsForm
                        schema={secretMappingsSchema}
                        values={{ credentials: JSON.stringify([{ account: 'acct-1', token: '' }]) }}
                        onChange={onChange}
                    />,
                );

                await user.click(screen.getByRole('button', { name: 'Replace Token' }));
                const cell = document.querySelector(
                    '[data-row-index="0"] input[data-field="token"]',
                ) as HTMLInputElement;
                expect(cell).not.toHaveAttribute('readonly');

                await user.type(cell, 'x');
                const last = onChange.mock.calls.at(-1)!;
                expect(last[0]).toBe('credentials');
                expect(JSON.parse(last[1] as string)).toEqual([{ account: 'acct-1', token: 'x' }]);
            });

            it('renders a newly added row secret cell as an editable input with no unlock control', async () => {
                const user = userEvent.setup();
                render(<ControlledMappings initialValue={JSON.stringify([{ account: 'acct-1', token: '' }])} />);

                expect(screen.getByRole('button', { name: 'Replace Token' })).toBeInTheDocument();

                await user.click(screen.getByRole('button', { name: 'Add row' }));

                const newCell = document.querySelector(
                    '[data-row-index="1"] input[data-field="token"]',
                ) as HTMLInputElement;
                expect(newCell).toBeInTheDocument();
                expect(newCell).not.toHaveAttribute('readonly');
                expect(newCell).not.toHaveAttribute('placeholder', '********');
                expect(screen.getAllByRole('button', { name: 'Replace Token' })).toHaveLength(1);
            });
        });

        describe('options-source dropdowns', () => {
            const mockApiGet = vi.mocked(apiGet);

            beforeEach(() => {
                mockApiGet.mockReset();
                delete (window as unknown as Record<string, unknown>).sampleProvider;
            });

            const optionsSchema: IntegrationSettingDefinition[] = [
                {
                    key: 'libraryMappings',
                    type: SchemaDataType.MappingsList,
                    displayName: 'Library mappings',
                    rowSchema: [
                        {
                            key: 'rootFolderPath',
                            type: SchemaDataType.String,
                            displayName: 'Root folder',
                            required: true,
                            optionsSource: { kind: 'RootFolders' },
                        },
                        {
                            key: 'contentType',
                            type: SchemaDataType.String,
                            displayName: 'Content type',
                            required: true,
                            optionsSource: { kind: 'ContentTypes' },
                        },
                        {
                            key: 'serverLibraryId',
                            type: SchemaDataType.String,
                            displayName: 'Provider library',
                            required: true,
                            optionsSource: {
                                kind: 'JSFunction',
                                functionName: 'sampleProvider.listLibraries',
                            },
                        },
                    ],
                },
            ];

            it('renders dropdowns populated from the root-folder and content-type endpoints', async () => {
                mockApiGet.mockImplementation((path) => {
                    if (path === '/root-folders') {
                        return Promise.resolve([{ name: 'Movies', path: '/data/movies' }]);
                    }
                    if (path === '/content-types') {
                        return Promise.resolve([{ slug: 'movies', displayName: 'Movies' }]);
                    }
                    return Promise.reject(new Error(`Unexpected path: ${path}`));
                });
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries: () => [] };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    libraryMappings: JSON.stringify([
                                        { rootFolderPath: '', contentType: '', serverLibraryId: '' },
                                    ]),
                                }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                await waitFor(() => {
                    expect(mockApiGet).toHaveBeenCalledWith('/root-folders');
                    expect(mockApiGet).toHaveBeenCalledWith('/content-types');
                });

                expect((await screen.findByLabelText('Root folder')).tagName).toBe('BUTTON');
                expect((await screen.findByLabelText('Content type')).tagName).toBe('BUTTON');
            });

            it('keeps an options-source picker visible when its resolver fails', async () => {
                mockApiGet.mockRejectedValue(new Error('upstream details'));
                const schema: IntegrationSettingDefinition[] = [
                    {
                        key: 'mappings',
                        type: SchemaDataType.MappingsList,
                        rowSchema: [
                            {
                                key: 'rootFolderPath',
                                type: SchemaDataType.String,
                                displayName: 'Root folder',
                                optionsSource: { kind: 'RootFolders' },
                            },
                        ],
                    },
                ];

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={schema}
                                values={{ mappings: JSON.stringify([{ rootFolderPath: '' }]) }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                expect(await screen.findByText(/Failed to load options/)).toBeInTheDocument();
                expect(screen.getByLabelText('Root folder').tagName).toBe('BUTTON');
                expect(screen.queryByRole('textbox', { name: 'Root folder' })).not.toBeInTheDocument();
                expect(screen.queryByText(/upstream details/)).not.toBeInTheDocument();
            });

            it('keeps an options-source picker visible when its resolver returns no options', async () => {
                mockApiGet.mockResolvedValue([]);
                const schema: IntegrationSettingDefinition[] = [
                    {
                        key: 'mappings',
                        type: SchemaDataType.MappingsList,
                        rowSchema: [
                            {
                                key: 'rootFolderPath',
                                type: SchemaDataType.String,
                                displayName: 'Root folder',
                                optionsSource: { kind: 'RootFolders' },
                            },
                        ],
                    },
                ];

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={schema}
                                values={{ mappings: JSON.stringify([{ rootFolderPath: '' }]) }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                expect(await screen.findByText('No options are available.')).toBeInTheDocument();
                expect(screen.getByLabelText('Root folder').tagName).toBe('BUTTON');
                expect(screen.queryByRole('textbox', { name: 'Root folder' })).not.toBeInTheDocument();
            });

            it('calls the JSFunction with the extension, entity, and full form state', async () => {
                mockApiGet.mockResolvedValue([]);
                const listLibraries = vi.fn(() => [
                    { value: 'lib-1', label: 'Movies' },
                    { value: 'lib-2', label: 'Shows' },
                ]);
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries };
                const values = {
                    host: 'http://provider.local:8096',
                    apiKey: 'k',
                    libraryMappings: JSON.stringify([
                        { rootFolderPath: '/data/movies', contentType: 'movies', serverLibraryId: '' },
                    ]),
                };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={values}
                                onChange={vi.fn()}
                                extensionId="provider-extension"
                                entityId="server-1"
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                await waitFor(() => {
                    expect(listLibraries).toHaveBeenCalledWith({
                        extensionId: 'provider-extension',
                        entityId: 'server-1',
                        state: values,
                    });
                });

                const cell = await screen.findByLabelText('Provider library');
                expect(cell.tagName).toBe('BUTTON');
            });

            it.each([
                new Error('upstream unreachable'),
                'upstream unreachable',
                Object.assign(new Error('upstream unreachable'), { userMessage: ' ' }),
            ])('keeps the picker visible with a retryable generic error for %o', async (error) => {
                mockApiGet.mockResolvedValue([]);
                const listLibraries = vi
                    .fn()
                    .mockRejectedValueOnce(error)
                    .mockResolvedValueOnce([{ value: 'lib-1', label: 'Movies' }]);
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    host: 'http://provider.local:8096',
                                    apiKey: 'secret',
                                    libraryMappings: JSON.stringify([
                                        { rootFolderPath: '', contentType: '', serverLibraryId: '' },
                                    ]),
                                }}
                                onChange={vi.fn()}
                                entityId="server-1"
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                const failure = await screen.findByText('Failed to load options. Check the source connection.');
                expect(failure).toHaveAttribute('role', 'alert');
                expect(failure).toHaveClass('integration-settings-form__error');
                expect(screen.getByLabelText('Provider library').tagName).toBe('BUTTON');
                expect(screen.queryByRole('textbox', { name: 'Provider library' })).not.toBeInTheDocument();

                await userEvent.click(
                    within(
                        screen
                            .getByLabelText('Provider library')
                            .closest('.integration-settings-form__cell-fallback') as HTMLElement,
                    ).getByRole('button', { name: 'Retry options' }),
                );

                await waitFor(() => expect(listLibraries).toHaveBeenCalledTimes(2));
                await waitFor(() => expect(screen.getByLabelText('Provider library')).toBeEnabled());
                await userEvent.click(screen.getByLabelText('Provider library'));
                expect(await screen.findByRole('option', { name: 'Movies' })).toBeInTheDocument();
            });

            it('renders a provider-supplied message without exposing the resolver error', async () => {
                mockApiGet.mockResolvedValue([]);
                const error = Object.assign(new Error('sensitive upstream response'), {
                    userMessage: 'The provider rejected the API key.',
                });
                (window as unknown as Record<string, unknown>).sampleProvider = {
                    listLibraries: () => Promise.reject(error),
                };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    host: 'http://provider.local:8096',
                                    apiKey: 'secret',
                                    libraryMappings: JSON.stringify([
                                        { rootFolderPath: '', contentType: '', serverLibraryId: '' },
                                    ]),
                                }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                expect(await screen.findByText(/rejected the API key/)).toBeInTheDocument();
                expect(screen.queryByText(/sensitive upstream response/)).not.toBeInTheDocument();
                expect(screen.getByLabelText('Provider library').tagName).toBe('BUTTON');
            });

            it('calls the JSFunction with unsaved credentials without requiring an existing entity', async () => {
                mockApiGet.mockResolvedValue([]);
                const listLibraries = vi.fn((_context: unknown) => [{ value: 'lib-1', label: 'Movies' }]);
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries };
                const values = {
                    host: 'http://provider.local:8096',
                    apiKey: 'new-key',
                    libraryMappings: JSON.stringify([{ rootFolderPath: '', contentType: '', serverLibraryId: '' }]),
                };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm schema={optionsSchema} values={values} onChange={vi.fn()} />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                await waitFor(() => {
                    expect(listLibraries).toHaveBeenCalledWith({
                        extensionId: null,
                        entityId: null,
                        state: values,
                    });
                });
                expect((await screen.findByLabelText('Provider library')).tagName).toBe('BUTTON');
            });

            it('uses stored edit settings when the secret is omitted from the form', async () => {
                mockApiGet.mockResolvedValue([]);
                const listLibraries = vi.fn(() => [{ value: 'lib-1', label: 'Movies' }]);
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries };
                const values = {
                    host: 'http://provider.local:8096',
                    libraryMappings: JSON.stringify([
                        { rootFolderPath: '/data/movies', contentType: 'movies', serverLibraryId: 'lib-1' },
                    ]),
                };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={values}
                                onChange={vi.fn()}
                                entityId="server-1"
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                await waitFor(() => {
                    expect(listLibraries).toHaveBeenCalledWith({
                        extensionId: null,
                        entityId: 'server-1',
                        state: values,
                    });
                });
                await waitFor(() => expect(screen.getByLabelText('Provider library')).toHaveTextContent('Movies'));
            });

            it('shows a credential prompt supplied by the resolver on an incomplete create form', async () => {
                mockApiGet.mockResolvedValue([]);
                const listLibraries = vi.fn(() => {
                    throw Object.assign(new Error('Missing credentials.'), {
                        userMessage: 'Enter the connection credentials.',
                        notConfigured: true,
                    });
                });
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries };
                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    libraryMappings: JSON.stringify([
                                        { rootFolderPath: '', contentType: '', serverLibraryId: '' },
                                    ]),
                                }}
                                onChange={vi.fn()}
                                entityId={null}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                expect(screen.getByLabelText('Provider library').tagName).toBe('BUTTON');
                const prompt = await screen.findByText('Enter the connection credentials.');
                expect(prompt).toHaveAttribute('role', 'status');
                expect(prompt).toHaveClass('integration-settings-form__help');
                expect(
                    within(
                        screen
                            .getByLabelText('Provider library')
                            .closest('.integration-settings-form__cell-fallback') as HTMLElement,
                    ).queryByRole('button', { name: 'Retry options' }),
                ).not.toBeInTheDocument();
                expect(screen.queryByRole('textbox', { name: 'Provider library' })).not.toBeInTheDocument();
                expect(listLibraries).toHaveBeenCalledTimes(1);
                expect(document.querySelector('[data-testid="mappings-needs-saved-server"]')).not.toBeInTheDocument();
            });

            it('keeps the picker visible when the server has no libraries', async () => {
                mockApiGet.mockResolvedValue([]);
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries: () => [] };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    host: 'http://provider.local:8096',
                                    apiKey: 'secret',
                                    libraryMappings: JSON.stringify([
                                        { rootFolderPath: '', contentType: '', serverLibraryId: '' },
                                    ]),
                                }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                expect(await screen.findByText(/No options are available./)).toBeInTheDocument();
                expect(screen.getByLabelText('Provider library').tagName).toBe('BUTTON');
                expect(screen.queryByRole('textbox', { name: 'Provider library' })).not.toBeInTheDocument();
            });

            it('shows a loading state while Provider discovery is pending', async () => {
                mockApiGet.mockResolvedValue([]);
                (window as unknown as Record<string, unknown>).sampleProvider = {
                    listLibraries: () => new Promise(() => undefined),
                };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    host: 'http://provider.local:8096',
                                    apiKey: 'secret',
                                    libraryMappings: JSON.stringify([
                                        { rootFolderPath: '', contentType: '', serverLibraryId: '' },
                                    ]),
                                }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                expect(await screen.findByText('Loading options...')).toBeInTheDocument();
                expect(screen.getByLabelText('Provider library').tagName).toBe('BUTTON');
            });

            it('shows a loading state while extension assets are unsettled with credentials present', () => {
                mockApiGet.mockResolvedValue([]);
                const listLibraries = vi.fn(() => []);
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries };

                render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'loading', error: null, manifest: null },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    host: 'http://provider.local:8096',
                                    apiKey: 'secret',
                                    libraryMappings: JSON.stringify([
                                        { rootFolderPath: '', contentType: '', serverLibraryId: '' },
                                    ]),
                                }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                expect(
                    within(
                        screen
                            .getByLabelText('Provider library')
                            .closest('.integration-settings-form__cell-fallback') as HTMLElement,
                    ).getByText('Loading options...'),
                ).toBeInTheDocument();
                expect(screen.queryByText(/No options are available./)).not.toBeInTheDocument();
                expect(screen.queryByRole('textbox', { name: 'Provider library' })).not.toBeInTheDocument();
                expect(listLibraries).not.toHaveBeenCalled();
            });

            it('reloads libraries after either connection setting changes without putting secrets in query keys', async () => {
                mockApiGet.mockResolvedValue([]);
                const listLibraries = vi.fn((_context: unknown) => [{ value: 'lib-1', label: 'Movies' }]);
                (window as unknown as Record<string, unknown>).sampleProvider = { listLibraries };
                const client = new QueryClient({
                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                });
                const mappings = JSON.stringify([{ rootFolderPath: '', contentType: '', serverLibraryId: 'lib-1' }]);
                const { rerender } = render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider client={client}>
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{ host: 'http://one.local', apiKey: 'first-secret', libraryMappings: mappings }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                await waitFor(() => expect(listLibraries).toHaveBeenCalledTimes(1));

                rerender(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider client={client}>
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{
                                    host: 'http://two.local',
                                    apiKey: 'second-secret',
                                    libraryMappings: mappings,
                                }}
                                onChange={vi.fn()}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                await waitFor(() => expect(listLibraries).toHaveBeenCalledTimes(2));
                expect(listLibraries.mock.calls[1]?.[0]).toEqual({
                    extensionId: null,
                    entityId: null,
                    state: {
                        host: 'http://two.local',
                        apiKey: 'second-secret',
                        libraryMappings: mappings,
                    },
                });
                const queryKeys = client
                    .getQueryCache()
                    .getAll()
                    .map((query) => query.queryKey);
                expect(JSON.stringify(queryKeys)).not.toContain('first-secret');
                expect(JSON.stringify(queryKeys)).not.toContain('second-secret');
            });

            it('persists a picked library and renders it selected on rerender', async () => {
                mockApiGet.mockResolvedValue([]);
                (window as unknown as Record<string, unknown>).sampleProvider = {
                    listLibraries: () => [
                        { value: 'lib-1', label: 'Movies' },
                        { value: 'lib-2', label: 'Shows' },
                    ],
                };
                const onChange = vi.fn();
                const baseValues = {
                    host: 'http://provider.local:8096',
                    apiKey: 'secret',
                    libraryMappings: JSON.stringify([
                        { rootFolderPath: '', contentType: '', serverLibraryId: 'lib-1' },
                    ]),
                };
                const { rerender } = render(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm schema={optionsSchema} values={baseValues} onChange={onChange} />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );

                await waitFor(() => expect(screen.getByLabelText('Provider library')).toHaveTextContent('Movies'));
                const picker = screen.getByLabelText('Provider library');

                await userEvent.click(picker);
                await userEvent.click(screen.getByRole('option', { name: 'Shows' }));

                const persisted = JSON.parse(onChange.mock.calls.at(-1)?.[1] as string);
                expect(persisted[0].serverLibraryId).toBe('lib-2');

                rerender(
                    <ExtensionContext.Provider
                        value={{
                            state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
                            retriesExhausted: false,
                            context: { apiBaseUrl: '/api', userId: 'user-1', username: 'admin', role: 'Admin' },
                            getExtensionsForSlot: () => [],
                            retry: () => Promise.resolve(),
                        }}
                    >
                        <QueryClientProvider
                            client={
                                new QueryClient({
                                    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
                                })
                            }
                        >
                            <IntegrationSettingsForm
                                schema={optionsSchema}
                                values={{ ...baseValues, libraryMappings: JSON.stringify(persisted) }}
                                onChange={onChange}
                            />
                        </QueryClientProvider>
                    </ExtensionContext.Provider>,
                );
                await waitFor(() => expect(screen.getByLabelText('Provider library')).toHaveTextContent('Shows'));
            });
        });

        it('round-trips a complete edit cycle through the persisted JSON value', () => {
            let persisted: Record<string, string> = {};
            const onChange = (key: string, value: string) => {
                persisted = { ...persisted, [key]: value };
            };
            const { rerender } = render(
                <IntegrationSettingsForm schema={mappingsSchema} values={persisted} onChange={onChange} />,
            );

            const seeded = [{ rootFolderId: 'rf-99', contentType: 'shows', serverLibraryId: 'lib-99' }];
            onChange('libraryMappings', JSON.stringify(seeded));
            rerender(<IntegrationSettingsForm schema={mappingsSchema} values={persisted} onChange={onChange} />);

            const rowInputs = document.querySelectorAll('[data-row-index] input');
            expect(rowInputs.length).toBe(3);
            expect((rowInputs[0] as HTMLInputElement).value).toBe('rf-99');
            expect((rowInputs[2] as HTMLInputElement).value).toBe('lib-99');
        });
    });
});
