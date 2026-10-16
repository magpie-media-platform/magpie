import {
    clearAuditRecords,
    MOCK_SLUGS,
    readActiveScenario,
    readAuditRecords,
    setActiveScenario,
    slugify,
    waitForAuditRecords,
    writeLatencyMap,
    writeScenarioFile,
    type AuditRecord,
    type AuditWaitOptions,
} from './fixtures';

export interface ConnectionResult {
    success: boolean;
    message?: string;
    failureKind?: 'None' | 'Transient' | 'NotFound' | 'Auth' | 'Fatal';
}

export interface ReleaseFixture {
    title: string;
    protocol: string;
    externalId?: string;
    infoUrl?: string;
    size?: number;
    publishDate?: string;
    categories?: string[];
    languages?: string[];
}

export interface LandedFileFixture {
    source: string;
    target: string;
}

export interface GrabFixture {
    externalId: string;
    status: string;
    statusReason?: string;
    progressPercent?: number;
    land?: LandedFileFixture[];
}

export interface DownloadInfoFixture {
    externalId: string;
    status: string;
    statusReason?: string;
    outputPath?: string;
    progressPercent?: number;
}

class MockController {
    protected scenarioName = 'default';
    public readonly slug: string;

    constructor(slug: string) {
        this.slug = slug;
    }

    get scenario(): string {
        return this.scenarioName;
    }

    async useScenario(name: string): Promise<this> {
        this.scenarioName = name;
        await setActiveScenario(this.slug, name);
        return this;
    }

    async syncScenario(): Promise<this> {
        this.scenarioName = await readActiveScenario(this.slug);
        return this;
    }

    async write(fileName: string, payload: unknown): Promise<this> {
        await writeScenarioFile(this.slug, this.scenarioName, fileName, payload);
        return this;
    }

    async setLatency(latencyMs: Record<string, number>): Promise<this> {
        await writeLatencyMap(this.slug, this.scenarioName, latencyMs);
        return this;
    }

    async clearCalls(): Promise<this> {
        await clearAuditRecords(this.slug);
        return this;
    }

    async calls<TRequest = unknown, TResponse = unknown>(
        method?: string,
    ): Promise<AuditRecord<TRequest, TResponse>[]> {
        const all = await readAuditRecords<TRequest, TResponse>(this.slug);
        return method ? all.filter((r) => r.method === method) : all;
    }

    async waitForCalls<TRequest = unknown, TResponse = unknown>(
        min: number,
        options: AuditWaitOptions<TRequest, TResponse> = {},
    ): Promise<AuditRecord<TRequest, TResponse>[]> {
        return waitForAuditRecords<TRequest, TResponse>(this.slug, min, options);
    }
}

class IndexerMock extends MockController {
    async armSearch(query: string, releases: ReleaseFixture[]): Promise<this> {
        const key = query ? `search-${slugify(query)}` : 'search';
        return this.write(`${key}.json`, { releases });
    }

    async armSearchFallback(releases: ReleaseFixture[]): Promise<this> {
        return this.write('search.json', { releases });
    }

    async armTestConnection(result: ConnectionResult): Promise<this> {
        return this.write('testConnection.json', result);
    }
}

class DownloadClientMock extends MockController {
    async armGrab(grab: GrabFixture): Promise<this> {
        return this.write('DownloadReleaseAsync.json', grab);
    }

    async armStatus(externalId: string, states: DownloadInfoFixture[]): Promise<this> {
        return this.write(`status-${externalId}.json`, states);
    }

    async armTestConnection(
        result: ConnectionResult & { expectedSettings?: Record<string, string> },
    ): Promise<this> {
        return this.write('TestConnectionAsync.json', result);
    }

    async armRemove(removed: boolean): Promise<this> {
        return this.write('RemoveDownloadAsync.json', removed);
    }
}

class MediaServerMock extends MockController {
    async armDeepLink(externalId: string, url: string | null): Promise<this> {
        return this.write(`BuildItemDeepLinkAsync-${externalId}.json`, { url });
    }

    async armTestConnection(result: ConnectionResult): Promise<this> {
        return this.write('TestConnectionAsync.json', result);
    }

    async armRefresh(refreshed: boolean): Promise<this> {
        return this.write('RefreshForContentAsync.json', { refreshed });
    }
}

class SubjectMetadataMock extends MockController {
    async armMetadata(kind: string, contribution: unknown): Promise<this> {
        return this.write(`tryGetMetadata-${kind}.json`, contribution);
    }
}

class SettingsMock extends MockController {
    async armTestConfiguration(result: ConnectionResult): Promise<this> {
        return this.write('TestConfigurationAsync.json', result);
    }
}

export const mocks = {
    indexer: new IndexerMock(MOCK_SLUGS.indexer),
    indexerFeed: new MockController(MOCK_SLUGS.indexerFeed),
    downloadClient: new DownloadClientMock(MOCK_SLUGS.downloadClient),
    mediaServer: new MediaServerMock(MOCK_SLUGS.mediaServer),
    notification: new MockController(MOCK_SLUGS.notification),
    subjectMetadata: new SubjectMetadataMock(MOCK_SLUGS.subjectMetadata),
    hierarchyPrimary: new MockController(MOCK_SLUGS.hierarchyMetadataPrimary),
    hierarchySecondary: new MockController(MOCK_SLUGS.hierarchyMetadataSecondary),
    settings: new SettingsMock(MOCK_SLUGS.settings),
} as const;

export const allMocks: MockController[] = Object.values(mocks);
