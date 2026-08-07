import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import {
    JobExecutionFilters,
    type JobExecutionFilterState,
    type TemplateOption,
    type StatusFilterOption,
} from './JobExecutionFilters';

const mockTemplates: TemplateOption[] = [
    { id: 'template-1', name: 'Content metadata refresh item' },
    { id: 'template-2', name: 'Search Missing' },
    { id: 'template-3', name: 'RSS Sync' },
];

const mockStatusOptions: StatusFilterOption[] = [
    { value: 'all', label: 'All Statuses' },
    { value: 'running', label: 'Running' },
    { value: 'queued', label: 'Queued' },
    { value: 'pending', label: 'Pending' },
    { value: 'completed', label: 'Completed' },
    { value: 'failed', label: 'Failed' },
    { value: 'cancelled', label: 'Cancelled' },
];

const defaultFilters: JobExecutionFilterState = { template: '', status: '' };

describe('JobExecutionFilters', () => {
    it('renders with correct data-component attribute', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(document.querySelector('[data-component="job-execution-filters"]')).toBeInTheDocument();
    });

    it('renders template and status dropdowns', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(screen.getByRole('combobox', { name: /filter by template/i })).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: /filter by status/i })).toBeInTheDocument();
    });

    it('shows "All Templates" when no template filter is selected', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        expect(templateTrigger).toHaveTextContent('All Templates');
    });

    it('shows "All Statuses" when no status filter is selected', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        expect(statusTrigger).toHaveTextContent('All Statuses');
    });

    it('shows selected template name', () => {
        render(
            <JobExecutionFilters
                filters={{ template: 'template-1', status: '' }}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        expect(templateTrigger).toHaveTextContent('Content metadata refresh item');
    });

    it('shows selected status label', () => {
        render(
            <JobExecutionFilters
                filters={{ template: '', status: 'running' }}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        expect(statusTrigger).toHaveTextContent('Running');
    });

    it('does not show "Clear" button when no filters are active', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(screen.queryByText('Clear filters')).not.toBeInTheDocument();
    });

    it('shows "Clear" button when filters are active', () => {
        render(
            <JobExecutionFilters
                filters={{ template: 'template-1', status: '' }}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(screen.getByText('Clear filters')).toBeInTheDocument();
    });
});

describe('JobExecutionFilters interactions', () => {
    it('calls onChange when template filter changes', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();

        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={onChange}
            />,
        );

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        await user.click(templateTrigger);

        const option = screen.getByRole('option', { name: 'Content metadata refresh item' });
        await user.click(option);

        expect(onChange).toHaveBeenCalledWith({ template: 'template-1', status: '' });
    });

    it('calls onChange when status filter changes', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();

        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={onChange}
            />,
        );

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);

        const option = screen.getByRole('option', { name: 'Running' });
        await user.click(option);

        expect(onChange).toHaveBeenCalledWith({ template: '', status: 'running' });
    });

    it('offers Pending and Queued as separate, independently selectable status options', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();

        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={onChange}
            />,
        );

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);

        expect(screen.getByRole('option', { name: 'Pending' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Queued' })).toBeInTheDocument();

        await user.click(screen.getByRole('option', { name: 'Queued' }));
        expect(onChange).toHaveBeenCalledWith({ template: '', status: 'queued' });

        await user.click(statusTrigger);
        await user.click(screen.getByRole('option', { name: 'Pending' }));
        expect(onChange).toHaveBeenCalledWith({ template: '', status: 'pending' });
    });

    it('clears template filter when "All Templates" is selected', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();

        render(
            <JobExecutionFilters
                filters={{ template: 'template-1', status: '' }}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={onChange}
            />,
        );

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        await user.click(templateTrigger);

        const option = screen.getByRole('option', { name: 'All Templates' });
        await user.click(option);

        expect(onChange).toHaveBeenCalledWith({ template: '', status: '' });
    });

    it('clears all filters when "Clear" is clicked', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();

        render(
            <JobExecutionFilters
                filters={{ template: 'template-1', status: 'running' }}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={onChange}
            />,
        );

        const clearButton = screen.getByText('Clear filters');
        await user.click(clearButton);

        expect(onChange).toHaveBeenCalledWith({ template: '', status: '' });
    });
});

describe('JobExecutionFilters accessibility', () => {
    it('template dropdown has correct aria-label', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(screen.getByRole('combobox', { name: /filter by template/i })).toBeInTheDocument();
    });

    it('status dropdown has correct aria-label', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(screen.getByRole('combobox', { name: /filter by status/i })).toBeInTheDocument();
    });

    it('has data-filter attributes on triggers', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(document.querySelector('[data-filter="template"]')).toBeInTheDocument();
        expect(document.querySelector('[data-filter="status"]')).toBeInTheDocument();
    });

    it('clear button has data-action attribute', () => {
        render(
            <JobExecutionFilters
                filters={{ template: 'template-1', status: '' }}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
            />,
        );

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });
});

describe('JobExecutionFilters loading state', () => {
    it('disables dropdowns when loading', () => {
        render(
            <JobExecutionFilters
                filters={defaultFilters}
                templates={mockTemplates}
                statusOptions={mockStatusOptions}
                onChange={vi.fn()}
                loading={true}
            />,
        );

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });

        expect(templateTrigger).toBeDisabled();
        expect(statusTrigger).toBeDisabled();
    });
});
