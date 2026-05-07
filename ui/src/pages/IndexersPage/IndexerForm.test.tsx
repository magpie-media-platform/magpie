import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';

import { IndexerForm } from './IndexerForm';

describe('IndexerForm', () => {
    describe('add mode', () => {
        it('renders add form when no initial values', () => {
            render(<IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />);

            expect(screen.getByText('Add Indexer')).toBeInTheDocument();
            expect(screen.getByText('Configure a new indexer to search for content.')).toBeInTheDocument();
        });

        it('shows name, provider type, and description fields', () => {
            render(<IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />);

            expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
            expect(screen.getByLabelText(/Provider Type/)).toBeInTheDocument();
            expect(screen.getByLabelText(/Description/)).toBeInTheDocument();
        });
    });

    describe('edit mode', () => {
        const existingIndexer = {
            id: 'idx-1',
            name: 'My Indexer',
            providerType: 'newznab',
            description: 'A test indexer',
        };

        it('renders edit form with initial values', () => {
            render(
                <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={existingIndexer} onSubmit={vi.fn()} />,
            );

            expect(screen.getByText('Edit Indexer')).toBeInTheDocument();
            expect(screen.getByText('Update the indexer configuration.')).toBeInTheDocument();
        });

        it('populates name from initial values', () => {
            render(
                <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={existingIndexer} onSubmit={vi.fn()} />,
            );

            expect(screen.getByDisplayValue('My Indexer')).toBeInTheDocument();
        });

        it('populates provider type from initial values', () => {
            render(
                <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={existingIndexer} onSubmit={vi.fn()} />,
            );

            expect(screen.getByDisplayValue('newznab')).toBeInTheDocument();
        });

        it('populates description from initial values', () => {
            render(
                <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={existingIndexer} onSubmit={vi.fn()} />,
            );

            expect(screen.getByDisplayValue('A test indexer')).toBeInTheDocument();
        });
    });

    describe('validation', () => {
        it('shows error when name is empty on submit', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(<IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.click(screen.getByText('Save'));

            expect(screen.getByText('Name is required')).toBeInTheDocument();
            expect(onSubmit).not.toHaveBeenCalled();
        });

        it('shows error when provider type is empty on submit', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(<IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />);

            await user.type(screen.getByLabelText(/Name/), 'Test Indexer');
            await user.click(screen.getByText('Save'));

            expect(screen.getByText('Provider type is required')).toBeInTheDocument();
            expect(onSubmit).not.toHaveBeenCalled();
        });
    });

    describe('form submission', () => {
        it('calls onSubmit with form values when editing valid data', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            const existingIndexer = {
                id: 'idx-1',
                name: 'My Indexer',
                providerType: 'newznab',
                description: 'A test indexer',
            };

            render(
                <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={existingIndexer} onSubmit={onSubmit} />,
            );

            await user.click(screen.getByText('Save'));

            expect(onSubmit).toHaveBeenCalledWith(existingIndexer);
        });

        it('disables buttons when submitting', () => {
            render(
                <IndexerForm
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
        it('calls onOpenChange with false when cancel clicked', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(<IndexerForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />);

            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).toHaveBeenCalledWith(false);
        });
    });

    describe('test button', () => {
        const existingIndexer = {
            id: 'idx-1',
            name: 'My Indexer',
            providerType: 'newznab',
        };

        it('renders test button when editing and onTest provided', () => {
            render(
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={existingIndexer}
                    onSubmit={vi.fn()}
                    onTest={vi.fn()}
                />,
            );

            expect(screen.getByRole('button', { name: /test/i })).toBeInTheDocument();
        });

        it('does not render test button in add mode', () => {
            render(
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    onSubmit={vi.fn()}
                    onTest={vi.fn()}
                />,
            );

            expect(screen.queryByRole('button', { name: /test/i })).not.toBeInTheDocument();
        });

        it('calls onTest when clicked', async () => {
            const user = userEvent.setup();
            const onTest = vi.fn();
            render(
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={existingIndexer}
                    onSubmit={vi.fn()}
                    onTest={onTest}
                />,
            );

            await user.click(screen.getByRole('button', { name: /test/i }));

            expect(onTest).toHaveBeenCalledTimes(1);
        });

        it('disables test button while testing', () => {
            render(
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={existingIndexer}
                    onSubmit={vi.fn()}
                    onTest={vi.fn()}
                    isTesting={true}
                />,
            );

            const testButton = screen.getByRole('button', { name: /test/i });
            expect(testButton).toBeDisabled();
            expect(testButton).toHaveAttribute('aria-busy', 'true');
        });
    });
});
