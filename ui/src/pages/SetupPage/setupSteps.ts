import type { SetupStatus } from '@/lib/setupApi';

export type SetupStepId = 'db' | 'admin' | 'metadata' | 'indexer' | 'download' | 'server' | 'root' | 'done';

export const SETUP_STEP_ORDER: readonly SetupStepId[] = [
    'db',
    'admin',
    'metadata',
    'indexer',
    'download',
    'root',
    'server',
    'done',
] as const;

export const SETUP_STEPS: Record<SetupStepId, string> = {
    db: 'Database Magpie will use.',
    admin: 'Create the first administrator account to get started.',
    metadata: 'Provide API keys so Magpie can fetch art and details.',
    indexer: 'Connect a release source so Magpie can search for content.',
    download: 'Connect a downloader so Magpie can grab releases.',
    server: 'Tell Magpie about your media server so library refreshes work.',
    root: 'Choose the folder where Magpie organizes imported files.',
    done: 'Review what you configured and finish setup.',
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
        return 'metadata';
    }
    return SETUP_STEP_ORDER[0];
}
