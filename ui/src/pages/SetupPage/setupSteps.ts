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

interface SetupStepDescriptor {
    id: SetupStepId;
    title: string;
    subtitle: string;
}

export const SETUP_STEPS: Record<SetupStepId, SetupStepDescriptor> = {
    db: {
        id: 'db',
        title: 'Choose a database',
        subtitle: 'Database Magpie will use.',
    },
    admin: {
        id: 'admin',
        title: 'Claim the administrator account',
        subtitle: 'Create the first administrator account to get started.',
    },
    metadata: {
        id: 'metadata',
        title: 'Add metadata providers',
        subtitle: 'Provide API keys so Magpie can fetch art and details.',
    },
    indexer: {
        id: 'indexer',
        title: 'Add your first indexer',
        subtitle: 'Connect a release source so Magpie can search for content.',
    },
    download: {
        id: 'download',
        title: 'Add a download client',
        subtitle: 'Connect a downloader so Magpie can grab releases.',
    },
    server: {
        id: 'server',
        title: 'Add a media server',
        subtitle: 'Tell Magpie about your media server so library refreshes work.',
    },
    root: {
        id: 'root',
        title: 'Add a root folder',
        subtitle: 'Choose the folder where Magpie organizes imported files.',
    },
    done: {
        id: 'done',
        title: 'Setup complete',
        subtitle: 'Review what you configured and finish setup.',
    },
} as const;

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
