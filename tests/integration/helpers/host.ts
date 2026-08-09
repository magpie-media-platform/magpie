import { execFile } from 'node:child_process';
import { mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';

import { integrationEnvironment } from './environment';
import { loginAdmin, type ApiClient } from './api';
import { until } from './wait';

const execFileAsync = promisify(execFile);

const here = dirname(fileURLToPath(import.meta.url));
const integrationRoot = resolve(here, '..');

const HEALTH_TIMEOUT_MS = 120_000;
const HEALTH_POLL_MS = 1_000;

function baseUrl(): string {
    return integrationEnvironment.baseUrl;
}

async function compose(...args: string[]): Promise<void> {
    await execFileAsync('docker', ['compose', ...args], { cwd: integrationRoot });
}

export async function startHost(): Promise<void> {
    await compose('up', '-d', '--wait');
}

export async function waitForReady(): Promise<void> {
    await until(
        'the Magpie host to report ready',
        async () => (await fetch(`${baseUrl()}/api/system/ready`)).ok || undefined,
        HEALTH_TIMEOUT_MS,
        HEALTH_POLL_MS,
    );
}

export async function ensureBindMountDirs(): Promise<void> {
    for (const dir of Object.values(integrationEnvironment.paths)) {
        await mkdir(dir, { recursive: true });
    }
    await mkdir(resolve(integrationRoot, 'checkpoints'), { recursive: true });
}

export async function restartHost(
    api: ApiClient,
    restart: () => Promise<void> = async () => {
        await api.post('/api/system/restart');
    },
): Promise<ApiClient> {
    const before = await api.get<{ startedAt: string }>('/api/system');
    await restart();
    return until(
        'the Magpie host to restart',
        async () => {
            try {
                const { api: restarted } = await loginAdmin(
                    integrationEnvironment.adminUsername,
                    integrationEnvironment.adminPassword,
                );
                const after = await restarted.get<{ startedAt: string }>('/api/system');
                return after.startedAt !== before.startedAt ? restarted : undefined;
            } catch {
                return undefined;
            }
        },
        HEALTH_TIMEOUT_MS,
        500,
    );
}
