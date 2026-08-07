export type JobExecutionStatusValue =
    | 'Queued'
    | 'Running'
    | 'Completed'
    | 'Failed'
    | 'Cancelled'
    | 'CancellationRequested';

export type JobExecutionEffectiveStatusValue = JobExecutionStatusValue | 'Pending';

export type JobScopeValue = 'System' | 'Administrative' | 'Content' | 'User';

export type JobTriggerModeValue = 'Manual' | 'Schedule' | 'Event';

export interface JobParameterDefinition {
    name: string;
    description?: string | null;
    type: number;
    required: boolean;
    defaultValue?: string | null;
}
