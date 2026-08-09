import { integrationEnvironment } from './environment';
import { ensureBindMountDirs, startHost, waitForReady } from './host';
import { stageDefaultScenarios } from './setup';

export default async function globalSetup(): Promise<void> {
    await ensureBindMountDirs();
    await stageDefaultScenarios();

    if (integrationEnvironment.skipBoot) {
        await waitForReady();
    } else {
        await startHost();
        await waitForReady();
    }
}
