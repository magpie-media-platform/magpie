import type { DetailRow } from '@/components/ui/DetailsPanel';
import { formatShortDate } from '@/lib/dateFormatting';
import { formatTimeSpanDuration } from '@/lib/formatTime';
import type { MaterializedMetadata } from '@/types/contentMetadata';
import type { LibraryMetadataFieldDefinition, LibraryStandardMetadataFieldDefinition } from '@/types/library';

export function nonEmptyMetadataText(value: string | null | undefined): string | undefined {
    const text = value?.trim();
    return text || undefined;
}

export function sortByOrdinal<T extends { ordinal?: number | null }>(items: T[] | null | undefined): T[] {
    return [...(items ?? [])].sort((left, right) => (left.ordinal ?? 0) - (right.ordinal ?? 0));
}

function formatCustomValue(value: string, field: LibraryMetadataFieldDefinition): string {
    const type = field.dataType?.trim().toLowerCase();
    if (type === 'date') {
        return formatShortDate(value, value);
    }
    if (type === 'duration' || field.format?.trim().toLowerCase() === 'duration') {
        return formatDisplayDuration(value);
    }
    return value;
}

function formatDisplayDuration(value: string): string {
    const duration = formatTimeSpanDuration(value);
    if (!duration) {
        return value;
    }

    const hours = duration.match(/^(\d+)h(?: (\d+)m)?$/);
    if (hours) {
        return `${hours[1]} h${hours[2] ? ` ${hours[2].padStart(2, '0')} m` : ''}`;
    }

    return duration.replace('m', ' m');
}

export function buildMaterializedDetailRows(
    metadata: MaterializedMetadata | null | undefined,
    standardFields: LibraryStandardMetadataFieldDefinition[] | undefined,
    customFields: LibraryMetadataFieldDefinition[] | undefined,
): DetailRow[] {
    if (!metadata) {
        return [];
    }

    const definitions = new Map((standardFields ?? []).map((field) => [field.field.toLowerCase(), field]));
    const rows: DetailRow[] = [];
    const addText = (
        fieldName: string,
        key: keyof MaterializedMetadata,
        fallbackLabel: string,
        requireSupport = true,
    ) => {
        const definition = definitions.get(fieldName.toLowerCase());
        const value = nonEmptyMetadataText(metadata[key] as string | null | undefined);
        if ((!requireSupport || definition) && value) {
            rows.push({ key, label: definition?.displayName?.trim() || fallbackLabel, value });
        }
    };
    const addDate = (fieldName: string, key: keyof MaterializedMetadata, fallbackLabel: string) => {
        const definition = definitions.get(fieldName.toLowerCase());
        const value = nonEmptyMetadataText(metadata[key] as string | null | undefined);
        if (definition && value) {
            rows.push({ key, label: definition.displayName?.trim() || fallbackLabel, value: formatShortDate(value) });
        }
    };

    addText('OriginalTitle', 'originalTitle', 'Original title', false);
    addText('SortTitle', 'sortTitle', 'Sort title', false);
    addText('Language', 'language', 'Language');

    const durationDefinition = definitions.get('duration');
    const duration = nonEmptyMetadataText(metadata.duration);
    if (durationDefinition && duration) {
        rows.push({
            key: 'duration',
            label: durationDefinition.displayName?.trim() || 'Duration',
            value: formatDisplayDuration(duration),
        });
    }

    const sequenceDefinition = definitions.get('sequencenumber');
    if (sequenceDefinition && metadata.sequenceNumber !== null && metadata.sequenceNumber !== undefined) {
        rows.push({
            key: 'sequenceNumber',
            label: sequenceDefinition.displayName?.trim() || 'Sequence number',
            value: String(metadata.sequenceNumber),
        });
    }

    addText('ProductionStatus', 'productionStatus', 'Production status');
    addDate('Established', 'established', 'Established');
    addDate('Dissolved', 'dissolved', 'Dissolved');
    addDate('BirthDate', 'birthDate', 'Birth date');
    addDate('DeathDate', 'deathDate', 'Death date');

    const values = new Map(
        (metadata.customMetadata ?? [])
            .map((entry) => [entry.key.trim().toLowerCase(), nonEmptyMetadataText(entry.value)] as const)
            .filter((entry): entry is readonly [string, string] => !!entry[0] && !!entry[1]),
    );
    for (const field of customFields ?? []) {
        const key = field.key.trim();
        const value = values.get(key.toLowerCase());
        if (key && value) {
            rows.push({
                key: `custom:${key}`,
                label: field.displayName?.trim() || key,
                value: formatCustomValue(value, field),
            });
        }
    }

    return rows;
}

export interface AlternateTextRow {
    id: string;
    kind: string;
    language?: string;
    text: string;
}

/** The alternate texts, in materialized order, for their own section. */
export function buildAlternateTextRows(metadata: MaterializedMetadata | null | undefined): AlternateTextRow[] {
    return sortByOrdinal(metadata?.alternateTexts).flatMap((alternate) => {
        const text = nonEmptyMetadataText(alternate.text);
        if (!text) {
            return [];
        }
        return [
            {
                id: alternate.id,
                kind: nonEmptyMetadataText(alternate.kind) ?? 'Alternate text',
                language: nonEmptyMetadataText(alternate.language),
                text,
            },
        ];
    });
}

export interface AwardEntry {
    label: string;
    results: string[];
}

export interface AwardGroup {
    key: string;
    body: string;
    entries: AwardEntry[];
}

interface AwardBody {
    label: string;
    entries: AwardEntry[];
    mergeable: Map<string, AwardEntry>;
}

function formatAwardResult(status: string | undefined, year: number | null | undefined): string | undefined {
    const yearText = year === null || year === undefined ? undefined : String(year);
    const won = status?.toLowerCase() === 'won';
    const statusText = status && !(won && yearText) ? `(${status})` : undefined;
    return [yearText, statusText].filter(Boolean).join(' ') || undefined;
}

export function buildAwardGroups(metadata: MaterializedMetadata | null | undefined): AwardGroup[] {
    const bodies = new Map<string, AwardBody>();

    for (const award of sortByOrdinal(metadata?.awards)) {
        const body = nonEmptyMetadataText(award.body);
        const name = nonEmptyMetadataText(award.name);
        if (!body && !name) {
            continue;
        }

        const bodyLabel = body ?? (name as string);
        const bodyKey = bodyLabel.toLowerCase();
        const group: AwardBody = bodies.get(bodyKey) ?? { label: bodyLabel, entries: [], mergeable: new Map() };
        bodies.set(bodyKey, group);

        const label = [
            body ? name : undefined,
            nonEmptyMetadataText(award.category),
            nonEmptyMetadataText(award.language),
            nonEmptyMetadataText(award.country),
        ]
            .filter((value): value is string => !!value)
            .join(' · ');
        const result = formatAwardResult(nonEmptyMetadataText(award.status), award.year);
        if (!result) {
            group.entries.push({ label, results: [] });
            continue;
        }

        const merged = group.mergeable.get(label.toLowerCase());
        if (merged) {
            merged.results.push(result);
            continue;
        }

        const entry: AwardEntry = { label, results: [result] };
        group.mergeable.set(label.toLowerCase(), entry);
        group.entries.push(entry);
    }

    return [...bodies].map(([bodyKey, group]) => ({
        key: `award-body:${bodyKey}`,
        body: group.label,
        entries: group.entries,
    }));
}
