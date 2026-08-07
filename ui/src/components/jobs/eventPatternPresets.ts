import { magpieEventNames } from '@/lib/magpieEventNames';

interface EventPatternPreset {
    label: string;
    value: string;
}

function escapeRegex(value: string): string {
    return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

const eventNamePresets = [
    { label: 'Content added', eventName: magpieEventNames.contentHierarchyItemCreated },
    { label: 'Content updated', eventName: magpieEventNames.contentHierarchyItemUpdated },
    { label: 'Content removed', eventName: magpieEventNames.contentHierarchyItemDeleted },
    { label: 'Download completed', eventName: magpieEventNames.downloadCompleted },
    { label: 'Download failed', eventName: magpieEventNames.downloadFailed },
    { label: 'File imported', eventName: magpieEventNames.downloadImported },
    { label: 'Metadata refreshed', eventName: magpieEventNames.metadataRefreshed },
] as const;

export const eventPatternExample = {
    eventName: magpieEventNames.contentHierarchyItemCreated,
    pattern: escapeRegex(magpieEventNames.contentHierarchyItemCreated),
} as const;

export const eventPatternPresets: EventPatternPreset[] = [
    { label: 'All events', value: '.*' },
    ...eventNamePresets.map(({ label, eventName }) => ({ label, value: escapeRegex(eventName) })),
];

export function eventPatternLabel(pattern: string): string {
    return eventPatternPresets.find((preset) => preset.value === pattern)?.label ?? pattern;
}
