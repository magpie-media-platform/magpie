import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import type { TemplateOption } from '@/lib/jobExecution';
import { RunJobDialog } from './RunJobDialog';

const runnableJobs: TemplateOption[] = [
    {
        id: 'ParameterizedJob',
        name: 'Parameterized Job',
        parameters: [
            { name: 'Tag', description: 'A tag', type: 0, required: true, defaultValue: null },
            { name: 'Limit', description: 'Item count', type: 1, required: false, defaultValue: '10' },
            { name: 'DryRun', description: 'Preview only', type: 2, required: false, defaultValue: null },
            { name: 'Note', description: 'Optional note', type: 0, required: false, defaultValue: null },
        ],
    },
];

describe('RunJobDialog parameters', () => {
    it('disables submit until a job is selected', async () => {
        const user = userEvent.setup();

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <RunJobDialog open={true} onOpenChange={() => {}} runnableJobs={runnableJobs} runJob={vi.fn()} />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        const runButton = within(dialog).getByRole('button', { name: /run job/i });
        expect(runButton).toBeDisabled();

        const jobSelect = dialog.querySelector('[data-field="jobType"]') as HTMLElement;
        await user.click(jobSelect);
        await user.click(screen.getByRole('option', { name: 'Parameterized Job' }));

        expect(runButton).toBeEnabled();
    });

    it('seeds and renders job parameters from the selected job', async () => {
        const user = userEvent.setup();

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <RunJobDialog open={true} onOpenChange={() => {}} runnableJobs={runnableJobs} runJob={vi.fn()} />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        const jobSelect = dialog.querySelector('[data-field="jobType"]') as HTMLElement;
        await user.click(jobSelect);
        await user.click(screen.getByRole('option', { name: 'Parameterized Job' }));

        const tagInput = dialog.querySelector('[data-field="parameter-Tag"]') as HTMLInputElement;
        const limitInput = dialog.querySelector('[data-field="parameter-Limit"]') as HTMLInputElement;
        const dryRunToggle = dialog.querySelector('[data-field="parameter-DryRun"]') as HTMLButtonElement;

        expect(tagInput).toBeInTheDocument();
        expect(tagInput).toHaveAttribute('aria-describedby', 'run-job-parameter-Tag-help');
        expect(limitInput.type).toBe('number');
        expect(limitInput.value).toBe('10');
        expect(dryRunToggle).toHaveAttribute('data-state', 'unchecked');
    });

    it('validates required parameters and submits normalized values', async () => {
        const user = userEvent.setup();
        const runJob = vi.fn().mockResolvedValue({ executionId: 'job-1', status: null });

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <RunJobDialog open={true} onOpenChange={() => {}} runnableJobs={runnableJobs} runJob={runJob} />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        const jobSelect = dialog.querySelector('[data-field="jobType"]') as HTMLElement;
        await user.click(jobSelect);
        await user.click(screen.getByRole('option', { name: 'Parameterized Job' }));
        await user.click(within(dialog).getByRole('button', { name: /run job/i }));

        expect(screen.getByText('Tag is required')).toBeInTheDocument();
        expect(runJob).not.toHaveBeenCalled();

        const tagInput = dialog.querySelector('[data-field="parameter-Tag"]') as HTMLInputElement;
        const dryRunToggle = dialog.querySelector('[data-field="parameter-DryRun"]') as HTMLButtonElement;
        await user.type(tagInput, 'movies');
        await user.click(dryRunToggle);
        await user.click(within(dialog).getByRole('button', { name: /run job/i }));

        await waitFor(() => {
            expect(runJob).toHaveBeenCalledWith({
                jobType: 'ParameterizedJob',
                mode: 'Immediate',
                parameters: {
                    Tag: 'movies',
                    Limit: '10',
                    DryRun: 'true',
                },
            });
        });
        expect(runJob.mock.calls[0][0].parameters).not.toHaveProperty('Note');
    });
});
