import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { ToastList } from '@/components/ui/Toast/ToastList';
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
    it('leaves an inherited import collision policy out of the submitted parameters', async () => {
        const user = userEvent.setup();
        const runJob = vi.fn().mockResolvedValue({ executionId: 'import-1', status: null });
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <RunJobDialog
                        open={true}
                        onOpenChange={() => {}}
                        runnableJobs={[
                            {
                                id: 'DownloadImportItem',
                                name: 'Download import item',
                                parameters: [
                                    {
                                        name: 'collisionMode',
                                        description: 'When omitted, inherit the root-folder policy.',
                                        type: 0,
                                        required: false,
                                        defaultValue: null,
                                    },
                                ],
                            },
                        ]}
                        runJob={runJob}
                    />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );
        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        await user.click(dialog.querySelector('[data-field="jobType"]') as HTMLElement);
        await user.click(screen.getByRole('option', { name: 'Download import item' }));
        expect(dialog.querySelector('[data-field="parameter-collisionMode"]')).toHaveValue('');
        await user.click(within(dialog).getByRole('button', { name: /run job/i }));
        await waitFor(() => expect(runJob).toHaveBeenCalled());
        expect(runJob.mock.calls[0][0].parameters).toBeUndefined();
    });

    it('queues a job that cannot run immediately and says so', async () => {
        const user = userEvent.setup();
        const runJob = vi.fn().mockResolvedValue({ executionId: 'backup-1', status: 'Queued' });
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <RunJobDialog
                        open={true}
                        onOpenChange={() => {}}
                        runnableJobs={[{ id: 'DatabaseBackup', name: 'Database backup', supportsImmediate: false }]}
                        runJob={runJob}
                    />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );
        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        await user.click(dialog.querySelector('[data-field="jobType"]') as HTMLElement);
        await user.click(screen.getByRole('option', { name: 'Database backup' }));

        expect(screen.getByTestId('run-job-dialog__caution')).toHaveTextContent('runs from the queue');
        await user.click(within(dialog).getByRole('button', { name: /run job/i }));

        await waitFor(() => expect(runJob).toHaveBeenCalledWith({ jobType: 'DatabaseBackup', mode: 'Queue' }));
        expect(await screen.findByText('Job queued')).toBeInTheDocument();
    });

    it('shows a rejected parameter at its field with the server reason and keeps the values', async () => {
        const user = userEvent.setup();
        const runJob = vi.fn().mockRejectedValue(new Error('Parameter Limit must be a valid number.'));
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <RunJobDialog open={true} onOpenChange={() => {}} runnableJobs={runnableJobs} runJob={runJob} />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );
        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        await user.click(dialog.querySelector('[data-field="jobType"]') as HTMLElement);
        await user.click(screen.getByRole('option', { name: 'Parameterized Job' }));
        await user.type(dialog.querySelector('[data-field="parameter-Tag"]') as HTMLInputElement, 'movies');
        await user.click(within(dialog).getByRole('button', { name: /run job/i }));

        await waitFor(() =>
            expect(within(dialog).getByText('Parameter Limit must be a valid number.')).toBeInTheDocument(),
        );
        expect(dialog.querySelector('[data-field="parameter-Limit"]')).toHaveAttribute('aria-invalid', 'true');
        expect(dialog.querySelector('[data-field="parameter-Tag"]')).toHaveValue('movies');
        expect(screen.queryByText('Failed to start job')).not.toBeInTheDocument();
    });

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
