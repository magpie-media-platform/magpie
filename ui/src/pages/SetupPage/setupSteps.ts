import type { SetupStatus } from '@/lib/setupApi';

export type SetupStepId =
    | 'db'
    | 'join'
    | 'admin'
    | 'extensions'
    | 'metadata'
    | 'indexer'
    | 'download'
    | 'server'
    | 'root'
    | 'done';

export const SETUP_STEP_ORDER: readonly SetupStepId[] = [
    'db',
    'admin',
    'extensions',
    'metadata',
    'indexer',
    'download',
    'root',
    'server',
    'done',
] as const;

const databaseStep = (instanceName: string) => `Database ${instanceName} will use.`;

export const SETUP_STEPS: Record<SetupStepId, (instanceName: string) => string> = {
    db: databaseStep,
    join: databaseStep,
    admin: () => 'Create the first administrator account to get started.',
    extensions: () => 'Install extensions.',
    metadata: (instanceName) => `Provide API keys so ${instanceName} can fetch art and details.`,
    indexer: (instanceName) => `Connect a release source so ${instanceName} can search for content.`,
    download: (instanceName) => `Connect a downloader so ${instanceName} can grab releases.`,
    server: (instanceName) => `Tell ${instanceName} about your media server so library refreshes work.`,
    root: (instanceName) => `Choose the folder where ${instanceName} organizes imported files.`,
    done: () => 'Review what you configured and finish setup.',
};

export interface SetupStepAvailability {
    includeMetadata: boolean;
}

const DEFAULT_STEP_AVAILABILITY: SetupStepAvailability = { includeMetadata: true };

function isStepIncluded(id: SetupStepId, availability: SetupStepAvailability): boolean {
    if (id === 'metadata') {
        return availability.includeMetadata;
    }
    return true;
}

export function nextStepAfter(
    current: SetupStepId,
    availability: SetupStepAvailability = DEFAULT_STEP_AVAILABILITY,
): SetupStepId | null {
    const currentIndex = SETUP_STEP_ORDER.indexOf(current);
    if (currentIndex < 0) {
        return null;
    }
    for (let index = currentIndex + 1; index < SETUP_STEP_ORDER.length; index += 1) {
        const candidate = SETUP_STEP_ORDER[index];
        if (isStepIncluded(candidate, availability)) {
            return candidate;
        }
    }
    return null;
}

export function pickInitialStep(status: SetupStatus): SetupStepId {
    if (!status.requiresAdminClaim) {
        return 'extensions';
    }
    return SETUP_STEP_ORDER[0];
}
