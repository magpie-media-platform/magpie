import { MOCK_SLUGS, setActiveScenario, writeScenarioFile } from './fixtures';

export async function stageDefaultScenarios(): Promise<void> {
    const scenario = 'default';
    const ok = { success: true, message: 'default' };

    await setActiveScenario(MOCK_SLUGS.indexer, scenario);
    await writeScenarioFile(MOCK_SLUGS.indexer, scenario, 'search', { releases: [] });
    await writeScenarioFile(MOCK_SLUGS.indexer, scenario, 'testConnection', ok);

    await setActiveScenario(MOCK_SLUGS.indexerFeed, scenario);
    await writeScenarioFile(MOCK_SLUGS.indexerFeed, scenario, 'SyncAsync', []);
    await writeScenarioFile(MOCK_SLUGS.indexerFeed, scenario, 'TestConnectionAsync', true);

    await setActiveScenario(MOCK_SLUGS.downloadClient, scenario);
    await writeScenarioFile(MOCK_SLUGS.downloadClient, scenario, 'DownloadReleaseAsync', {
        externalId: '',
        status: 'Queued',
    });
    await writeScenarioFile(MOCK_SLUGS.downloadClient, scenario, 'TestConnectionAsync', ok);
    await writeScenarioFile(MOCK_SLUGS.downloadClient, scenario, 'RemoveDownloadAsync', true);
    await writeScenarioFile(MOCK_SLUGS.downloadClient, scenario, 'PauseDownloadAsync', true);
    await writeScenarioFile(MOCK_SLUGS.downloadClient, scenario, 'ResumeDownloadAsync', true);
    await writeScenarioFile(MOCK_SLUGS.downloadClient, scenario, 'ForceStartDownloadAsync', true);

    await setActiveScenario(MOCK_SLUGS.mediaServer, scenario);
    await writeScenarioFile(MOCK_SLUGS.mediaServer, scenario, 'TestConnectionAsync', ok);
    await writeScenarioFile(MOCK_SLUGS.mediaServer, scenario, 'RefreshForContentAsync', { refreshed: false });

    await setActiveScenario(MOCK_SLUGS.notification, scenario);
    await setActiveScenario(MOCK_SLUGS.subjectMetadata, scenario);

    await setActiveScenario(MOCK_SLUGS.settings, scenario);
    await writeScenarioFile(MOCK_SLUGS.settings, scenario, 'TestConfigurationAsync', ok);

    for (const slug of [MOCK_SLUGS.hierarchyMetadataPrimary, MOCK_SLUGS.hierarchyMetadataSecondary]) {
        await setActiveScenario(slug, scenario);
        await writeScenarioFile(slug, scenario, 'tryGetMetadataAsync', {
            providerType: slug,
            source: 'tmdb:default',
            title: 'Unstaged fixture',
            language: 'en',
        });
        await writeScenarioFile(slug, scenario, 'expandChildrenAsync', []);
        await writeScenarioFile(slug, scenario, 'expandRelatedSubjectsAsync', []);
        await writeScenarioFile(slug, scenario, 'searchAsync', []);
        await writeScenarioFile(slug, scenario, 'advancedSearchAsync', []);
    }
}
