import { useCallback, useEffect, useMemo, useState, type SubmitEvent, type ReactNode } from 'react';
import { LightningIcon, WarningIcon, XIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import type { RunJobRequest, RunJobResult } from '@/hooks/useJobs';
import type { TemplateOption } from '@/lib/jobExecution';
import { getErrorMessage } from '@/lib/errors';
import { focusFirstInvalidField } from '@/lib/focusFirstInvalidField';
import { JobParameterFieldset } from './JobParameterFieldset';
import {
    jobParameterErrorKey,
    normalizeJobParameterValues,
    seedJobParameters,
    validateJobParameters,
} from './jobParameters';

import './RunJobDialog.css';

interface RunJobDialogProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    runnableJobs: TemplateOption[];

    runJob: (request: RunJobRequest) => Promise<RunJobResult>;

    onRan?: () => void;
}

export function RunJobDialog({ open, onOpenChange, runnableJobs, runJob, onRan }: RunJobDialogProps): ReactNode {
    const { toast } = useToast();
    const [jobType, setJobType] = useState('');
    const [parameters, setParameters] = useState<Record<string, string>>({});
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [isSubmitting, setIsSubmitting] = useState(false);

    const selectedJob = useMemo(() => runnableJobs.find((j) => j.id === jobType), [runnableJobs, jobType]);
    const parameterSpec = useMemo(() => selectedJob?.parameters ?? [], [selectedJob]);
    const queued = selectedJob?.supportsImmediate === false;

    useEffect(() => {
        if (open) {
            setJobType('');
            setParameters({});
            setErrors({});
            setIsSubmitting(false);
        }
    }, [open]);

    const handleJobTypeChange = useCallback(
        (value: string) => {
            setJobType(value);
            const job = runnableJobs.find((j) => j.id === value);
            setParameters(seedJobParameters(job?.parameters));
            setErrors({});
        },
        [runnableJobs],
    );

    const handleParameterChange = useCallback((name: string, value: string) => {
        setParameters((prev) => ({ ...prev, [name]: value }));
        setErrors((prev) => {
            const errorKey = jobParameterErrorKey(name);
            if (!prev[errorKey]) {
                return prev;
            }
            const next = { ...prev };
            delete next[errorKey];
            return next;
        });
    }, []);

    const validate = useCallback((): boolean => {
        const next: Record<string, string> = {};
        if (!jobType) {
            next.jobType = 'Select a job to run';
        }
        Object.assign(next, validateJobParameters(parameterSpec, parameters));
        setErrors(next);
        return Object.keys(next).length === 0;
    }, [jobType, parameterSpec, parameters]);

    const handleSubmit = useCallback(
        async (e: SubmitEvent) => {
            e.preventDefault();
            const form = e.currentTarget;
            if (!validate()) {
                focusFirstInvalidField(form);
                return;
            }
            const payloadParameters = normalizeJobParameterValues(parameters);
            setIsSubmitting(true);
            try {
                await runJob({
                    jobType,
                    mode: queued ? 'Queue' : 'Immediate',
                    parameters: Object.keys(payloadParameters).length > 0 ? payloadParameters : undefined,
                });
                toast({
                    title: selectedJob?.name ?? jobType,
                    description: queued ? 'Job queued' : 'Job started successfully',
                    variant: 'success',
                });
                onOpenChange(false);
                onRan?.();
            } catch (error) {
                const message = getErrorMessage(error, 'Failed to start job');
                const parameterName = parameterSpec.find(
                    (parameter) =>
                        message.startsWith(`Parameter ${parameter.name} `) ||
                        message.startsWith(`Required parameter ${parameter.name} `),
                )?.name;
                if (parameterName) {
                    setErrors({ [jobParameterErrorKey(parameterName)]: message });
                    focusFirstInvalidField(form);
                }
                toast({
                    title: selectedJob?.name ?? jobType,
                    description: message,
                    variant: 'error',
                });
            } finally {
                setIsSubmitting(false);
            }
        },
        [validate, parameters, runJob, jobType, queued, toast, selectedJob, parameterSpec, onOpenChange, onRan],
    );

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent data-component="run-job-dialog">
                <DialogHeader>
                    <DialogTitle>Run a job</DialogTitle>
                    <DialogDescription>Run a job on demand.</DialogDescription>
                </DialogHeader>

                <Alert
                    variant="warning"
                    icon={<WarningIcon weight="fill" aria-hidden="true" />}
                    className="run-job-dialog__caution"
                    data-testid="run-job-dialog__caution"
                >
                    {queued
                        ? 'This job runs from the queue when a worker is free, after work already waiting for the same target.'
                        : 'The job starts immediately. It can overlap with scheduled work or work that is already in progress.'}
                </Alert>

                <form onSubmit={handleSubmit} className="run-job-dialog__form">
                    <div className="run-job-dialog__field">
                        <Label htmlFor="run-job-type" error={!!errors.jobType} required>
                            Job
                        </Label>
                        <Select value={jobType} onValueChange={handleJobTypeChange} disabled={isSubmitting}>
                            <SelectTrigger
                                id="run-job-type"
                                placeholder="Select a job..."
                                error={!!errors.jobType}
                                data-field="jobType"
                            >
                                {selectedJob?.name ?? 'Select a job...'}
                            </SelectTrigger>
                            <SelectContent>
                                {runnableJobs.map((job) => (
                                    <SelectItem key={job.id} value={job.id}>
                                        {job.name}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                        {errors.jobType && (
                            <p className="form-error" role="alert">
                                {errors.jobType}
                            </p>
                        )}
                    </div>

                    <JobParameterFieldset
                        parameters={parameterSpec}
                        values={parameters}
                        errors={errors}
                        disabled={isSubmitting}
                        idPrefix="run-job-parameter"
                        classNames={{
                            fieldset: 'run-job-dialog__parameters',
                            legend: 'run-job-dialog__legend',
                            field: 'run-job-dialog__field',
                            toggleField: 'run-job-dialog__field run-job-dialog__field--toggle',
                            help: 'run-job-dialog__help',
                        }}
                        showBooleanDescription={false}
                        onChange={handleParameterChange}
                    />

                    <DialogFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            onClick={() => onOpenChange(false)}
                            disabled={isSubmitting}
                            data-action="cancel"
                        >
                            <XIcon weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button
                            type="submit"
                            variant="primary"
                            loading={isSubmitting}
                            disabled={!selectedJob}
                            data-action="run"
                        >
                            <LightningIcon weight="bold" aria-hidden="true" />
                            Run job
                        </Button>
                    </DialogFooter>
                </form>
            </DialogContent>
        </Dialog>
    );
}

RunJobDialog.displayName = 'RunJobDialog';

export default RunJobDialog;
