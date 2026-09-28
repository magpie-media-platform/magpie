import { isGuid } from '@/lib/isGuid';

interface AffectedEntityDisplay {
    label: string;
    reference: string | null;
    to: string | null;
}

interface EntityTypeMeta {
    label: string;
    to?: (value: string) => string;
}

const ENTITY_TYPES: Record<string, EntityTypeMeta> = {
    ContentHierarchyItem: { label: 'Hierarchy item', to: (value) => `/library/${value}` },
    IndexerConnection: { label: 'Indexer', to: () => '/admin/indexers' },
    DownloadClient: { label: 'Download client', to: () => '/admin/download-clients' },
    MediaServer: { label: 'Media server', to: () => '/admin/media-servers' },
    RootFolder: { label: 'Root folder', to: () => '/admin/root-folders' },
    RemotePathMapping: { label: 'Remote path mapping', to: () => '/admin/remote-path-mappings' },
    QualityTierList: { label: 'Tier list', to: () => '/admin/quality-tiers' },
    Job: { label: 'Job', to: () => '/admin/jobs' },
    ScheduleTrigger: { label: 'Scheduled job', to: () => '/admin/jobs' },
    Database: { label: 'Database', to: () => '/admin/database' },
    Extension: { label: 'Extension', to: (value) => `/admin/extensions/${value}` },
};

function humanizeType(type: string): string {
    const spaced = type.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
    const lower = spaced.toLowerCase();
    return lower.charAt(0).toUpperCase() + lower.slice(1);
}

function shortenReference(value: string): string {
    return isGuid(value) ? value.slice(0, 8) : value;
}

export function describeAffectedEntity(token: string | null | undefined): AffectedEntityDisplay | null {
    if (token === null || token === undefined || token.trim() === '') {
        return null;
    }

    const separator = token.indexOf(':');
    if (separator <= 0) {
        return { label: token, reference: null, to: null };
    }

    const type = token.slice(0, separator);
    const value = token.slice(separator + 1);
    const meta = ENTITY_TYPES[type];
    if (meta) {
        return {
            label: meta.label,
            reference: value.trim() === '' ? null : shortenReference(value),
            to: meta.to ? meta.to(value) : null,
        };
    }

    return {
        label: humanizeType(type),
        reference: value.trim() === '' ? null : shortenReference(value),
        to: null,
    };
}
