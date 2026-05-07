import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';

import { IndexerFeedForm } from './IndexerFeedForm';

describe('IndexerFeedForm', () => {
    describe('add mode', () => {
        it('renders add form when no initial values', () => {
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />);

            expect(screen.getByText('Add Feed')).toBeInTheDocument();
            expect(screen.getByText('Configure a new feed to sync indexer definitions.')).toBeInTheDocument();
        });

        it('shows name, description, and provider fields', () => {
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />);

            expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
            expect(screen.getByLabelText(/Description/)).toBeInTheDocument();
            expect(screen.getByLabelText(/Provider/)).toBeInTheDocument();
        });

        it('shows enabled toggle defaulting to on', () => {
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />);

            const toggle = screen.getByRole('switch');
            expect(toggle).toBeChecked();
        });

        it('shows settings section with add button', () => {
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />);

            expect(screen.getByText('Settings')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /add setting/i })).toBeInTheDocument();
        });

        it('adds a key-value row when Add Setting is clicked', async () => {
            const user = userEvent.setup();
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />);

            await user.click(screen.getByRole('button', { name: /add setting/i }));

            expect(screen.getByPlaceholderText('Key')).toBeInTheDocument();
            expect(screen.getByPlaceholderText('Value')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /remove setting/i })).toBeInTheDocument();
        });
    });

    describe('edit mode', () => {
        const existingFeed = {
            id: 'feed-1',
            name: 'My Feed',
            description: 'Test feed description',
            provider: 'prowlarr',
            enabled: true,
            settings: { baseUrl: 'http://localhost:9696', apiKey: 'secret123' },
        };

        it('renders edit form with initial values', () => {
            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={vi.fn()} />,
            );

            expect(screen.getByText('Edit Feed')).toBeInTheDocument();
            expect(screen.getByText('Update the indexer feed configuration.')).toBeInTheDocument();
        });

        it('populates name from initial values', () => {
            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={vi.fn()} />,
            );

            expect(screen.getByDisplayValue('My Feed')).toBeInTheDocument();
        });

        it('populates description from initial values', () => {
            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={vi.fn()} />,
            );

            expect(screen.getByDisplayValue('Test feed description')).toBeInTheDocument();
        });

        it('populates provider from initial values', () => {
            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={vi.fn()} />,
            );

            expect(screen.getByDisplayValue('prowlarr')).toBeInTheDocument();
        });

        it('populates settings entries from initial values', () => {
            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={vi.fn()} />,
            );

            expect(screen.getByDisplayValue('baseUrl')).toBeInTheDocument();
            expect(screen.getByDisplayValue('http://localhost:9696')).toBeInTheDocument();
            expect(screen.getByDisplayValue('apiKey')).toBeInTheDocument();
            expect(screen.getByDisplayValue('secret123')).toBeInTheDocument();
        });

        it('shows remove buttons for each settings entry', () => {
            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={vi.fn()} />,
            );

            const removeButtons = screen.getAllByRole('button', { name: /remove setting/i });
            expect(removeButtons.length).toBe(2);
        });
    });

    describe('validation', () => {
        it('shows error when name is empty on submit', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.click(screen.getByText('Save'));

            expect(screen.getByText('Name is required')).toBeInTheDocument();
            expect(onSubmit).not.toHaveBeenCalled();
        });

        it('shows error when provider is empty on submit', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.type(screen.getByLabelText(/Name/), 'Test Feed');
            await user.click(screen.getByText('Save'));

            expect(screen.getByText('Provider is required')).toBeInTheDocument();
            expect(onSubmit).not.toHaveBeenCalled();
        });

        it('shows duplicate key errors when settings have the same key', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.type(screen.getByLabelText(/Name/), 'Test');
            await user.type(screen.getByLabelText(/Provider/), 'test');

            await user.click(screen.getByRole('button', { name: /add setting/i }));
            const keyInputs = screen.getAllByPlaceholderText('Key');
            await user.type(keyInputs[0], 'baseUrl');

            await user.click(screen.getByRole('button', { name: /add setting/i }));
            const keyInputs2 = screen.getAllByPlaceholderText('Key');
            await user.type(keyInputs2[1], 'baseUrl');

            await user.click(screen.getByText('Save'));

            const errors = screen.getAllByText('Duplicate key');
            expect(errors.length).toBe(2);
            expect(onSubmit).not.toHaveBeenCalled();
        });

        it('clears duplicate key error when key is changed', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.type(screen.getByLabelText(/Name/), 'Test');
            await user.type(screen.getByLabelText(/Provider/), 'test');

            await user.click(screen.getByRole('button', { name: /add setting/i }));
            const keyInputs = screen.getAllByPlaceholderText('Key');
            await user.type(keyInputs[0], 'baseUrl');

            await user.click(screen.getByRole('button', { name: /add setting/i }));
            const keyInputs2 = screen.getAllByPlaceholderText('Key');
            await user.type(keyInputs2[1], 'baseUrl');

            await user.click(screen.getByText('Save'));
            expect(screen.getAllByText('Duplicate key').length).toBe(2);

            const keyInputsAfter = screen.getAllByPlaceholderText('Key');
            await user.clear(keyInputsAfter[1]);
            await user.type(keyInputsAfter[1], 'apiKey');

            await user.click(screen.getByText('Save'));
            expect(onSubmit).toHaveBeenCalled();
        });
    });

    describe('form submission', () => {
        it('calls onSubmit with form values when editing valid data', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            const existingFeed = {
                id: 'feed-1',
                name: 'My Feed',
                description: 'Test description',
                provider: 'prowlarr',
                enabled: true,
                settings: { baseUrl: 'http://localhost:9696', apiKey: 'secret123' },
            };

            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={onSubmit} />,
            );

            await user.click(screen.getByText('Save'));

            expect(onSubmit).toHaveBeenCalledWith(existingFeed);
        });

        it('submits settings from key-value entries', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();

            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.type(screen.getByLabelText(/Name/), 'New Feed');
            await user.type(screen.getByLabelText(/Provider/), 'jackett');

            await user.click(screen.getByRole('button', { name: /add setting/i }));
            await user.type(screen.getByPlaceholderText('Key'), 'baseUrl');
            await user.type(screen.getByPlaceholderText('Value'), 'http://localhost:9117');

            await user.click(screen.getByText('Save'));

            expect(onSubmit).toHaveBeenCalledWith({
                id: undefined,
                name: 'New Feed',
                description: '',
                provider: 'jackett',
                enabled: true,
                settings: { baseUrl: 'http://localhost:9117' },
            });
        });

        it('disables buttons when submitting', () => {
            render(
                <IndexerFeedForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />,
            );

            const saveButton = screen.getByRole('button', { name: /save/i });
            expect(saveButton).toHaveAttribute('aria-busy', 'true');
            expect(saveButton).toBeDisabled();
            expect(screen.getByRole('button', { name: /cancel/i })).toBeDisabled();
        });
    });

    describe('cancel', () => {
        it('calls onOpenChange with false when cancel clicked on clean form', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />);

            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).toHaveBeenCalledWith(false);
        });
    });

    describe('unsaved changes guard', () => {
        it('shows unsaved changes dialog when cancelling a dirty form', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />);

            await user.type(screen.getByLabelText(/Name/), 'Modified');
            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).not.toHaveBeenCalled();
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        it('closes the form when discard is clicked in unsaved dialog', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />);

            await user.type(screen.getByLabelText(/Name/), 'Modified');
            await user.click(screen.getByText('Cancel'));
            await user.click(screen.getByText('Discard Changes'));

            expect(onOpenChange).toHaveBeenCalledWith(false);
        });

        it('returns to editing when keep editing is clicked in unsaved dialog', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />);

            await user.type(screen.getByLabelText(/Name/), 'Modified');
            await user.click(screen.getByText('Cancel'));
            await user.click(screen.getByText('Keep Editing'));

            expect(onOpenChange).not.toHaveBeenCalled();
            expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        });

        it('detects dirty state from provider change', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />);

            await user.type(screen.getByLabelText(/Provider/), 'prowlarr');
            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).not.toHaveBeenCalled();
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        it('detects dirty state from settings change', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(<IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />);

            await user.click(screen.getByRole('button', { name: /add setting/i }));
            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).not.toHaveBeenCalled();
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });
    });

    describe('settings editor', () => {
        it('removes a settings entry when remove button is clicked', async () => {
            const user = userEvent.setup();
            const existingFeed = {
                id: 'feed-1',
                name: 'My Feed',
                description: '',
                provider: 'prowlarr',
                enabled: true,
                settings: { baseUrl: 'http://localhost:9696', apiKey: 'secret' },
            };

            render(
                <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={existingFeed} onSubmit={vi.fn()} />,
            );

            const removeButtons = screen.getAllByRole('button', { name: /remove setting/i });
            expect(removeButtons.length).toBe(2);

            await user.click(removeButtons[0]);

            await waitFor(() => {
                expect(screen.getAllByRole('button', { name: /remove setting/i }).length).toBe(1);
            });
        });

        it('excludes entries with empty keys from submitted settings', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();

            render(<IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.type(screen.getByLabelText(/Name/), 'Test');
            await user.type(screen.getByLabelText(/Provider/), 'test');

            await user.click(screen.getByRole('button', { name: /add setting/i }));

            await user.type(screen.getByPlaceholderText('Value'), 'some-value');

            await user.click(screen.getByText('Save'));

            expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ settings: {} }));
        });
    });
});
