import { access, cp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

import { loginAdmin, type ApiClient } from './api';
import { cli } from './cli';
import { INTEGRATION_ROOT, integrationEnvironment } from './environment';
import { waitForJob } from './jobs';
import { allMocks } from './mocks';
import { until } from './wait';

export type CheckpointName = 'configured-baseline' | 'rich-library' | 'journey-rich-library';
type SnapshotName = CheckpointName | 'administration-restore-point';

interface BackupInfo {
    id: string;
    name: string;
}

interface SystemInfo {
    startedAt: string;
}

interface CheckpointManifest {
    backupId: string;
    fileAnchors: readonly string[];
}

const checkpointRoot = resolve(INTEGRATION_ROOT, 'checkpoints');
const cliKeyPath = resolve(checkpointRoot, 'cli-api-key');
const fixtureDirectories = {
    downloads: integrationEnvironment.paths.downloads,
    library: integrationEnvironment.paths.library,
    'test-fixtures': integrationEnvironment.paths.fixtures,
} as const;

export async function createCheckpoint(
    api: ApiClient,
    name: SnapshotName,
    fileAnchors: readonly string[],
): Promise<BackupInfo> {
    const existing = await api.get<BackupInfo[]>('/api/database/backups');
    for (const backup of existing.filter((item) => item.name === name)) {
        await api.delete(`/api/database/backups/${backup.id}`);
    }

    const executionId = await api.post<string>('/api/jobs/enqueue', {
        jobType: 'DatabaseBackup',
        parameters: { name },
    });
    const execution = await waitForJob(api, executionId);
    if (execution.status !== 'Completed') {
        throw new Error(`${name} backup failed: ${execution.statusMessage ?? execution.status}`);
    }

    const backup = await until(
        `${name} backup to appear`,
        async () => (await api.get<BackupInfo[]>('/api/database/backups')).find((item) => item.name === name),
        30_000,
        250,
    );

    for (const mock of allMocks) {
        await mock.clearCalls();
    }

    const target = resolve(checkpointRoot, name);
    await rm(target, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
    await mkdir(target, { recursive: true });
    for (const [directory, source] of Object.entries(fixtureDirectories)) {
        await cp(source, resolve(target, directory), { recursive: true });
    }
    await writeFile(
        resolve(target, 'manifest.json'),
        JSON.stringify({ backupId: backup.id, fileAnchors } satisfies CheckpointManifest, null, 2),
        'utf8',
    );
    await proveFileAnchors(fileAnchors);
    return backup;
}

export async function saveCliApiKey(apiKey: string): Promise<void> {
    await mkdir(checkpointRoot, { recursive: true });
    await writeFile(cliKeyPath, apiKey, { encoding: 'utf8', mode: 0o600 });
    cli.setApiKey(apiKey);
}

export async function restoreCheckpoint(
    name: SnapshotName,
    proveDatabaseAnchors: (api: ApiClient) => Promise<void>,
): Promise<ApiClient> {
    cli.setApiKey((await readFile(cliKeyPath, 'utf8')).trim());
    const source = resolve(checkpointRoot, name);
    const manifest = JSON.parse(await readFile(resolve(source, 'manifest.json'), 'utf8')) as CheckpointManifest;

    for (const [directory, target] of Object.entries(fixtureDirectories)) {
        await restoreDirectory(resolve(source, directory), target);
    }

    const { api: currentApi } = await loginAdmin(
        integrationEnvironment.adminUsername,
        integrationEnvironment.adminPassword,
    );
    const baselineStartedAt = (await currentApi.get<SystemInfo>('/api/system')).startedAt;
    await currentApi.post(`/api/database/backups/${manifest.backupId}/restore`);

    await until(
        `${name} restore to restart the host`,
        async () => {
            try {
                const setup = await fetch(`${integrationEnvironment.baseUrl}/api/system/setup/status`);
                if (!setup.ok) {
                    return undefined;
                }
                const { api } = await loginAdmin(
                    integrationEnvironment.adminUsername,
                    integrationEnvironment.adminPassword,
                );
                const info = await api.get<SystemInfo>('/api/system');
                return info.startedAt !== baselineStartedAt ? api : undefined;
            } catch {
                return undefined;
            }
        },
        120_000,
        500,
    );

    const { api } = await loginAdmin(integrationEnvironment.adminUsername, integrationEnvironment.adminPassword);
    await proveFileAnchors(manifest.fileAnchors);
    await proveDatabaseAnchors(api);
    return api;
}

async function restoreDirectory(source: string, target: string): Promise<void> {
    await mkdir(target, { recursive: true });
    for (const entry of await readdir(target)) {
        await rm(resolve(target, entry), { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
    }
    for (const entry of await readdir(source)) {
        await cp(resolve(source, entry), resolve(target, entry), { recursive: true });
    }
}

async function proveFileAnchors(anchors: readonly string[]): Promise<void> {
    for (const anchor of anchors) {
        await access(resolve(INTEGRATION_ROOT, anchor));
    }
}
