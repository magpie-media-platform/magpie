import { apiPost } from '@/lib/api';

export type JobExecutionModeValue = 'Queue' | 'Immediate';

export interface JobExecutionRequest {
    jobType: string;

    extensionId?: string | null;

    parameters?: Record<string, string>;

    mode?: JobExecutionModeValue;
}

type ImmediateJobExecutionRequest = Omit<JobExecutionRequest, 'mode'>;

export interface JobActionDisposition {
    executionId: string | null;

    status?: string | null;

    statusMessage?: string | null;
}

export type JobActionResponse =
    | string
    | null
    | undefined
    | {
          executionId?: string | null;
          id?: string | null;
          status?: string | null;
          statusMessage?: string | null;
      };

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
}

export function normalizeJobActionDisposition(response: JobActionResponse): JobActionDisposition {
    if (typeof response === 'string') {
        return { executionId: response, status: null };
    }

    if (!isRecord(response)) {
        return { executionId: null, status: null };
    }

    const executionId =
        typeof response.executionId === 'string'
            ? response.executionId
            : typeof response.id === 'string'
              ? response.id
              : null;
    const status = typeof response.status === 'string' ? response.status : null;
    const statusMessage = typeof response.statusMessage === 'string' ? response.statusMessage : null;

    return { executionId, status, statusMessage };
}

export function requireJobActionExecutionId(disposition: JobActionDisposition, actionName: string): string {
    if (!disposition.executionId) {
        throw new Error(`${actionName} did not return an execution id.`);
    }

    return disposition.executionId;
}

export async function executeJobRequest(request: JobExecutionRequest): Promise<JobActionDisposition> {
    if (request.mode === 'Immediate') {
        return executeImmediateJobRequest({
            jobType: request.jobType,
            extensionId: request.extensionId,
            parameters: request.parameters,
        });
    }

    const response = await apiPost<JobActionResponse>('/jobs/enqueue', request);
    return normalizeJobActionDisposition(response);
}

/** The job ran but ended Failed or Cancelled, as opposed to a request that could not start it. */
export class JobExecutionEndedError extends Error {
    constructor(readonly disposition: JobActionDisposition) {
        super(disposition.statusMessage ?? `Job execution ${String(disposition.status).toLowerCase()}.`);
        this.name = 'JobExecutionEndedError';
    }
}

export async function executeImmediateJobRequest(request: ImmediateJobExecutionRequest): Promise<JobActionDisposition> {
    const response = await apiPost<JobActionResponse>('/jobs/execute-immediate', request);
    const disposition = normalizeJobActionDisposition(response);
    if (disposition.status === 'Failed' || disposition.status === 'Cancelled') {
        throw new JobExecutionEndedError(disposition);
    }

    return disposition;
}
