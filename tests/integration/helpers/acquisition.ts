import { ApiError, type ApiClient } from './api';
import { drainDownloadRefresh, drainReleaseAcquisitionJobs, waitForJob } from './jobs';
import { mocks, type LandedFileFixture } from './mocks';
import { until } from './wait';

interface Paged<T> {
    items: T[];
}

interface InteractiveCandidate {
    release: Record<string, unknown> & {
        externalId?: string;
        title: string;
    };
}

export interface AcquisitionResult {
    candidates: InteractiveCandidate[];
    content: {
        id: string;
        name: string;
        contentItem?: {
            fulfillment: string;
            qualityStatus?: string | null;
            mediaFiles: Array<{
                fileKind?: string;
                filePath: string;
                mediaInfo?: { tracks?: Array<{ type: string }> } | null;
                mimeType?: string | null;
            }>;
        } | null;
    };
    download: {
        id: string;
        externalId: string;
        progress?: number | null;
        status: string;
        statusReason?: string | null;
        title: string;
    };
    secondGrabStatus?: number;
}

export interface AcquisitionFixture {
    categories: string[];
    contentId: string;
    downloadExternalId: string;
    landedFiles: LandedFileFixture[];
    importedContentId?: string;
    releaseExternalId: string;
    releaseTitle: string;
    scenario: string;
    searchTitle: string;
    proveConcurrentGrab?: boolean;
}

export async function acquireContent(api: ApiClient, fixture: AcquisitionFixture): Promise<AcquisitionResult> {
    await drainReleaseAcquisitionJobs(api);

    const release = {
        title: fixture.releaseTitle,
        protocol: 'torrent',
        externalId: fixture.releaseExternalId,
        infoUrl: `https://indexer.journey.invalid/releases/${fixture.releaseExternalId}`,
        size: 734_003_200,
        publishDate: '2026-01-02T00:00:00Z',
        categories: fixture.categories,
        languages: ['en'],
    };

    await mocks.indexer.useScenario(fixture.scenario);
    await mocks.indexer.armSearchFallback([release]);
    await mocks.indexer.clearCalls();

    await mocks.downloadClient.useScenario(fixture.scenario);
    await mocks.downloadClient.write(`DownloadReleaseAsync-${fixture.releaseExternalId}.json`, {
        externalId: fixture.downloadExternalId,
        status: 'Queued',
        progressPercent: 0,
        land: fixture.landedFiles,
    });
    await mocks.downloadClient.armStatus(fixture.downloadExternalId, [
        { externalId: fixture.downloadExternalId, status: 'Downloading', progressPercent: 42 },
        { externalId: fixture.downloadExternalId, status: 'Downloaded', progressPercent: 100 },
    ]);
    await mocks.downloadClient.armRemove(true);
    await mocks.downloadClient.clearCalls();

    await mocks.mediaServer.useScenario(fixture.scenario);
    await mocks.mediaServer.armRefresh(true);
    await mocks.mediaServer.clearCalls();

    const candidates = await until(
        `${fixture.searchTitle} release search`,
        async () => {
            const found = await api.post<InteractiveCandidate[]>(`/api/interactive-search/${fixture.contentId}`);
            return found.length > 0 ? found : undefined;
        },
        60_000,
        500,
    );
    const candidate = candidates.find((item) => item.release.externalId === fixture.releaseExternalId);
    if (!candidate) {
        throw new Error(`Release ${fixture.releaseExternalId} was not returned for ${fixture.searchTitle}.`);
    }

    let secondGrabStatus: number | undefined;
    if (fixture.proveConcurrentGrab) {
        await mocks.downloadClient.setLatency({ DownloadReleaseAsync: 2_000 });
        const firstGrab = api.post(`/api/interactive-search/${fixture.contentId}/grab`, candidate.release);
        await new Promise((resolve) => setTimeout(resolve, 250));
        try {
            await api.post(`/api/interactive-search/${fixture.contentId}/grab`, candidate.release);
        } catch (error) {
            if (!(error instanceof ApiError)) {
                throw error;
            }
            secondGrabStatus = error.status;
        }
        await firstGrab;
        await mocks.downloadClient.setLatency({});
    } else {
        await api.post(`/api/interactive-search/${fixture.contentId}/grab`, candidate.release);
    }

    const queued = await until(
        `${fixture.downloadExternalId} download record`,
        async () => {
            const response = await api.get<Paged<AcquisitionResult['download']>>('/api/downloads?pageSize=100');
            return response.items.find((item) => item.externalId === fixture.downloadExternalId);
        },
        30_000,
        250,
    );
    const failureStatuses = new Set(['Failed', 'Cancelled']);
    if (failureStatuses.has(queued.status)) {
        throw new Error(
            `Download ${queued.id} ended as ${queued.status}: ${queued.statusReason ?? 'No status reason'}.`,
        );
    }

    for (let attempt = 0; attempt < 5; attempt += 1) {
        const current = await api.get<AcquisitionResult['download']>(`/api/downloads/${queued.id}`);
        if (current.status === 'Imported') {
            break;
        }
        if (failureStatuses.has(current.status)) {
            throw new Error(
                `Download ${queued.id} ended as ${current.status}: ${current.statusReason ?? 'No status reason'}.`,
            );
        }

        await drainDownloadRefresh(api);
        const refreshed = await api.get<AcquisitionResult['download']>(`/api/downloads/${queued.id}`);
        if (refreshed.status === 'Imported') {
            break;
        }
        if (failureStatuses.has(refreshed.status)) {
            throw new Error(
                `Download ${queued.id} ended as ${refreshed.status}: ${refreshed.statusReason ?? 'No status reason'}.`,
            );
        }
    }

    const imported = await until(
        `${fixture.downloadExternalId} import`,
        async () => {
            const current = await api.get<AcquisitionResult['download']>(`/api/downloads/${queued.id}`);
            if (failureStatuses.has(current.status)) {
                throw new Error(
                    `Download ${queued.id} ended as ${current.status}: ${current.statusReason ?? 'No status reason'}.`,
                );
            }
            return current.status === 'Imported' ? current : undefined;
        },
        120_000,
        500,
    );
    const importedContentId = fixture.importedContentId ?? fixture.contentId;
    let content = await until(
        `${fixture.searchTitle} imported files`,
        async () => {
            const current = await api.get<AcquisitionResult['content']>(`/api/content/${importedContentId}`);
            return current.contentItem?.mediaFiles.length ? current : undefined;
        },
        60_000,
        500,
    );

    if (
        content.contentItem!.mediaFiles.some((file) =>
            ['audio', 'video'].includes(file.fileKind?.toLowerCase() ?? ''),
        )
    ) {
        const executionId = await api.post<string>('/api/jobs/enqueue', {
            jobType: 'MediaInfoExtractScan',
            parameters: { contentHierarchyItemId: importedContentId },
        });
        const execution = await waitForJob(api, executionId);
        if (execution.status !== 'Completed') {
            throw new Error(`Media info scan ${executionId} ended as ${execution.status}.`);
        }
        content = await until(
            `${fixture.searchTitle} media info`,
            async () => {
                const current = await api.get<AcquisitionResult['content']>(`/api/content/${importedContentId}`);
                return current.contentItem?.mediaFiles.some((file) => file.mediaInfo?.tracks?.length) ? current : undefined;
            },
            60_000,
            500,
        );
    }

    return { candidates, content, download: imported, secondGrabStatus };
}
