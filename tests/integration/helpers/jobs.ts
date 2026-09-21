import { type ApiClient } from './api';
import { until } from './wait';

interface Paged<T> {
    items: T[];
}

export interface JobExecution {
    id: string;
    jobType: string;
    status: string;
    statusMessage?: string | null;
    progress?: number | null;
    parentExecutionId?: string | null;
}

interface RunJobResponse {
    executionId: string;
    status: string;
}

export async function runImmediateJob(api: ApiClient, jobType: string): Promise<JobExecution> {
    const started = await api.post<RunJobResponse>('/api/jobs/execute-immediate', { jobType });
    if (started.status === 'Completed') {
        return { id: started.executionId, jobType, status: started.status };
    }
    return waitForJob(api, started.executionId);
}

export async function drainDownloadRefresh(api: ApiClient): Promise<void> {
    const started = await api.post<RunJobResponse>('/api/jobs/execute-immediate', {
        jobType: 'DownloadRefresh',
    });
    const completed = started.status === 'Completed'
        ? started
        : await until(
              `DownloadRefresh execution ${started.executionId} to complete`,
              async () => {
                  const execution = await api.get<JobExecution>(`/api/jobs/${started.executionId}`);
                  return ['Completed', 'Failed', 'Cancelled'].includes(execution.status) ? execution : undefined;
              },
              30_000,
              250,
          );
    if (completed.status !== 'Completed') {
        throw new Error(`DownloadRefresh execution ${started.executionId} ended as ${completed.status}.`);
    }

    await until(
        'outstanding DownloadRefresh executions to become terminal',
        async () => {
            const active = await api.get<Paged<JobExecution>>(
                '/api/jobs?jobType=DownloadRefresh&status=Queued&status=Running' +
                    '&status=CancellationRequested&pageSize=100',
            );
            if (active.items.length === 0) {
                return true;
            }
            for (const execution of active.items) {
                await api.post(`/api/jobs/${execution.id}/cancel`).catch(() => undefined);
            }
            return undefined;
        },
        30_000,
        250,
    );
}

export async function drainContentMetadataRefreshItems(api: ApiClient): Promise<void> {
    await until(
        'outstanding ContentMetadataRefreshItem executions to complete',
        async () => {
            const active = await api.get<Paged<JobExecution>>(
                '/api/jobs?jobType=ContentMetadataRefreshItem&status=Queued&status=Running&pageSize=1',
            );
            return active.items.length === 0 ? true : undefined;
        },
        60_000,
        250,
    );
}

export async function drainLibraryImportMatchResolution(api: ApiClient): Promise<void> {
    await until(
        'outstanding LibraryImportMatchResolution executions to complete',
        async () => {
            const active = await api.get<Paged<JobExecution>>(
                '/api/jobs?jobType=LibraryImportMatchResolution&status=Queued&status=Running&pageSize=1',
            );
            return active.items.length === 0 ? true : undefined;
        },
        60_000,
        250,
    );
}

export async function drainReleaseAcquisitionJobs(api: ApiClient): Promise<void> {
    const jobTypes = new Set(['ReleaseSearchItem', 'ReleaseAutoGrabItem']);
    await until(
        'outstanding release acquisition executions to become terminal',
        async () => {
            const active = await api.get<Paged<JobExecution>>(
                '/api/jobs?status=Queued&status=Running&status=CancellationRequested&pageSize=100',
            );
            return active.items.every((execution) => !jobTypes.has(execution.jobType)) ? true : undefined;
        },
        60_000,
        250,
    );
}

export async function waitForJob(
    api: ApiClient,
    executionId: string,
    timeoutMs = 120_000,
): Promise<JobExecution> {
    return until(
        `job ${executionId} to become terminal`,
        async () => {
            const execution = await api.get<JobExecution>(`/api/jobs/${executionId}`);
            return ['Completed', 'Failed', 'Cancelled'].includes(execution.status) ? execution : undefined;
        },
        timeoutMs,
        250,
    );
}
