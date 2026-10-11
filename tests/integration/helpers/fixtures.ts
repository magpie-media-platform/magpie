import { mkdir, readdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

import { integrationEnvironment } from './environment';
import { until } from './wait';

export const FIXTURES_ROOT = resolve(integrationEnvironment.paths.fixtures);

export const MOCK_SLUGS = {
    indexer: 'mock-indexer',
    indexerFeed: 'mock-indexer-feed',
    downloadClient: 'mock-download-client',
    mediaServer: 'mock-media-server',
    notification: 'mock-notification',
    subjectMetadata: 'subjectmetadata',
    hierarchyMetadataPrimary: 'mock-hierarchy-metadata-primary',
    hierarchyMetadataSecondary: 'mock-hierarchy-metadata-secondary',
    settings: 'mock-settings',
} as const;

export interface AuditRecord<TRequest = unknown, TResponse = unknown> {
    plugin: string;
    method: string;
    timestampUtc: string;
    request: TRequest;
    response: TResponse;
}

export interface AuditWaitOptions<TRequest = unknown, TResponse = unknown> {
    method?: string;
    timeoutMs?: number;
    predicate?: (record: AuditRecord<TRequest, TResponse>) => boolean;
}

function pluginRoot(slug: string): string {
    return resolve(FIXTURES_ROOT, slug);
}

function scenarioFolder(slug: string, scenario: string): string {
    return resolve(pluginRoot(slug), 'scenarios', scenario);
}

function auditFolder(slug: string): string {
    return resolve(pluginRoot(slug), 'audit');
}

export function slugify(value: string): string {
    return value
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-+|-+$/g, '');
}

export async function readActiveScenario(slug: string): Promise<string> {
    const active = await readFile(resolve(pluginRoot(slug), '_active'), 'utf8').catch((error: unknown) => {
        if (isNodeErrno(error) && error.code === 'ENOENT') {
            return '';
        }
        throw error;
    });
    return active.trim() || 'default';
}

export async function setActiveScenario(slug: string, scenario: string): Promise<void> {
    const root = pluginRoot(slug);
    await mkdir(root, { recursive: true });
    await atomicWrite(resolve(root, '_active'), scenario);
}

export async function writeScenarioFile(
    slug: string,
    scenario: string,
    fileName: string,
    payload: unknown,
): Promise<void> {
    const folder = scenarioFolder(slug, scenario);
    await mkdir(folder, { recursive: true });
    const name = fileName.endsWith('.json') ? fileName : `${fileName}.json`;
    await atomicWrite(resolve(folder, name), JSON.stringify(payload, null, 2));
}

export async function writeLatencyMap(
    slug: string,
    scenario: string,
    latencyMs: Record<string, number>,
): Promise<void> {
    const folder = scenarioFolder(slug, scenario);
    await mkdir(folder, { recursive: true });
    await atomicWrite(resolve(folder, '_latency.json'), JSON.stringify(latencyMs, null, 2));
}

export async function clearAuditRecords(slug: string): Promise<void> {
    const target = auditFolder(slug);
    await waitForFolderToQuiesce(target);
    await rm(target, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}

async function waitForFolderToQuiesce(target: string): Promise<void> {
    let previous: string | undefined;
    await until(
        `${target} to stop changing`,
        async () => {
            const current = (await readdir(target).catch(() => [])).sort().join(',');
            const stable = current === previous;
            previous = current;
            return stable ? true : undefined;
        },
        5_000,
        50,
    );
}

export async function readAuditRecords<TRequest = unknown, TResponse = unknown>(
    slug: string,
): Promise<AuditRecord<TRequest, TResponse>[]> {
    let entries: string[];
    try {
        entries = await readdir(auditFolder(slug));
    } catch (error) {
        if (isNodeErrno(error) && error.code === 'ENOENT') {
            return [];
        }
        throw error;
    }

    const files = entries.filter((name) => name.endsWith('.json')).sort((a, b) => a.localeCompare(b));
    const records: AuditRecord<TRequest, TResponse>[] = [];
    for (const file of files) {
        const raw = await readFile(resolve(auditFolder(slug), file), 'utf8');
        records.push(JSON.parse(raw) as AuditRecord<TRequest, TResponse>);
    }
    return records;
}

export async function waitForAuditRecords<TRequest = unknown, TResponse = unknown>(
    slug: string,
    min: number,
    options: AuditWaitOptions<TRequest, TResponse> = {},
): Promise<AuditRecord<TRequest, TResponse>[]> {
    const timeoutMs = options.timeoutMs ?? 20_000;
    const deadline = Date.now() + timeoutMs;
    const matches = async (): Promise<AuditRecord<TRequest, TResponse>[]> => {
        const all = await readAuditRecords<TRequest, TResponse>(slug);
        const methodMatches = options.method ? all.filter((record) => record.method === options.method) : all;
        return options.predicate ? methodMatches.filter(options.predicate) : methodMatches;
    };

    while (Date.now() < deadline) {
        const found = await matches();
        if (found.length >= min) {
            return found;
        }
        await sleep(250);
    }

    const found = await matches();
    if (found.length >= min) {
        return found;
    }
    throw new Error(
        `Timed out after ${timeoutMs}ms waiting for ${min} audit record(s) on '${slug}'` +
            `${options.method ? ` for method '${options.method}'` : ''}; saw ${found.length}.`,
    );
}

async function atomicWrite(target: string, contents: string): Promise<void> {
    const tmp = `${target}.tmp`;
    await writeFile(tmp, contents, 'utf8');
    await rename(tmp, target);
}

function sleep(ms: number): Promise<void> {
    return new Promise((r) => setTimeout(r, ms));
}

interface NodeErrno extends Error {
    code?: string;
}

function isNodeErrno(value: unknown): value is NodeErrno {
    return value instanceof Error && typeof (value as NodeErrno).code === 'string';
}
