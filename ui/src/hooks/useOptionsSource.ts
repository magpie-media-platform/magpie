import { useEffect, useMemo, useState } from 'react';
import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { useExtensions } from '@/extensions/useExtensions';
import { apiGet } from '@/lib/api';
import { type SettingSchemaOption, type SettingSchemaOptionsSource } from '@/hooks/useProviderSchema';

export interface OptionsSourceContext {
    extensionId: string | null;
    entityId: string | null;
    state: Record<string, string>;
}

interface RootFolderOptionSourceItem {
    name: string;
    path: string;
}

interface ContentTypeOptionSourceItem {
    slug: string;
    displayName: string;
}

type OptionsSourceFunction = (context: OptionsSourceContext) => unknown;

const JS_FUNCTION_DEBOUNCE_MS = 250;
const optionsStateRevisions = new WeakMap<Record<string, string>, number>();
let nextOptionsStateRevision = 1;

const optionsSourceQueryKeys = {
    resolve: (
        source: SettingSchemaOptionsSource | undefined | null,
        extensionId: string,
        entityId: string,
        settingsRevision: number,
    ) =>
        [
            'options-source',
            source?.kind ?? '',
            source?.functionName ?? '',
            extensionId,
            entityId,
            settingsRevision,
        ] as const,
};

function coerceOptions(result: unknown): SettingSchemaOption[] {
    if (!Array.isArray(result)) {
        return [];
    }
    const options: SettingSchemaOption[] = [];
    for (const entry of result) {
        if (entry === null || typeof entry !== 'object') {
            continue;
        }
        const candidate = entry as Partial<SettingSchemaOption>;
        if (typeof candidate.value === 'string' && typeof candidate.label === 'string') {
            options.push({
                value: candidate.value,
                label: candidate.label,
                description: typeof candidate.description === 'string' ? candidate.description : undefined,
            });
        }
    }
    return options;
}

function compareText(left: string, right: string): number {
    const normalizedLeft = left.toUpperCase();
    const normalizedRight = right.toUpperCase();
    return normalizedLeft < normalizedRight ? -1 : normalizedLeft > normalizedRight ? 1 : 0;
}

async function resolveRootFolders(): Promise<SettingSchemaOption[]> {
    const folders = await apiGet<RootFolderOptionSourceItem[]>('/root-folders');
    return folders
        .map((folder) => ({ value: folder.path, label: folder.name, description: folder.path }))
        .sort((left, right) => compareText(left.label, right.label) || compareText(left.value, right.value));
}

async function resolveContentTypes(): Promise<SettingSchemaOption[]> {
    const contentTypes = await apiGet<ContentTypeOptionSourceItem[]>('/content-types');
    return contentTypes
        .map((contentType) => ({
            value: contentType.slug,
            label: contentType.displayName,
            description: contentType.slug,
        }))
        .sort((left, right) => compareText(left.label, right.label) || compareText(left.value, right.value));
}

function resolveWindowFunction(functionName: string): { fn: OptionsSourceFunction; receiver: unknown } {
    const segments = functionName.split('.');
    if (segments.some((segment) => segment.length === 0)) {
        throw new Error(`Invalid options source function "${functionName}".`);
    }

    let receiver: unknown = window;
    for (const segment of segments.slice(0, -1)) {
        if ((typeof receiver !== 'object' && typeof receiver !== 'function') || receiver === null) {
            throw new Error(`Options source function "${functionName}" was not found.`);
        }
        receiver = (receiver as Record<string, unknown>)[segment];
    }

    if ((typeof receiver !== 'object' && typeof receiver !== 'function') || receiver === null) {
        throw new Error(`Options source function "${functionName}" was not found.`);
    }

    const fn = (receiver as Record<string, unknown>)[segments[segments.length - 1]];
    if (typeof fn !== 'function') {
        throw new Error(`Options source function "${functionName}" was not found.`);
    }
    return { fn: fn as OptionsSourceFunction, receiver };
}

async function resolveJSFunction(
    source: SettingSchemaOptionsSource,
    context: OptionsSourceContext,
): Promise<SettingSchemaOption[]> {
    if (!source.functionName) {
        throw new Error('A JSFunction options source requires a function name.');
    }
    const { fn, receiver } = resolveWindowFunction(source.functionName);
    const result = await fn.call(receiver, context);
    return coerceOptions(result);
}

async function resolveOptionsSource(
    source: SettingSchemaOptionsSource,
    context: OptionsSourceContext,
): Promise<SettingSchemaOption[]> {
    switch (source.kind) {
        case 'RootFolders':
            return resolveRootFolders();
        case 'ContentTypes':
            return resolveContentTypes();
        case 'JSFunction':
            return resolveJSFunction(source, context);
    }
}

function hashSettings(settings: Record<string, string>): string {
    const entries = Object.entries(settings)
        .filter(([, value]) => value !== undefined && value !== null)
        .map(([key, value]) => [key, String(value)] as const)
        .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
    return JSON.stringify(entries);
}

function getOptionsStateRevision(state: Record<string, string>): number {
    const existing = optionsStateRevisions.get(state);
    if (existing !== undefined) {
        return existing;
    }
    const revision = nextOptionsStateRevision;
    nextOptionsStateRevision += 1;
    optionsStateRevisions.set(state, revision);
    return revision;
}

interface DebouncedState {
    hash: string;
    revision: number;
    state: Record<string, string>;
}

function useDebouncedState(state: Record<string, string>): DebouncedState {
    const stateHash = hashSettings(state);
    const stateRevision = getOptionsStateRevision(state);
    const snapshot = useMemo(() => ({ ...state }), [state]);
    const [debounced, setDebounced] = useState<DebouncedState>(() => ({
        hash: stateHash,
        revision: stateRevision,
        state: snapshot,
    }));

    useEffect(() => {
        if (stateHash === debounced.hash) {
            return;
        }
        const timeoutId = window.setTimeout(() => {
            setDebounced({
                hash: stateHash,
                revision: stateRevision,
                state: snapshot,
            });
        }, JS_FUNCTION_DEBOUNCE_MS);
        return () => window.clearTimeout(timeoutId);
    }, [debounced.hash, snapshot, stateHash, stateRevision]);

    return debounced;
}

export function useOptionsSource(
    source: SettingSchemaOptionsSource | undefined | null,
    context: OptionsSourceContext,
    sourceEnabled = true,
): UseQueryResult<SettingSchemaOption[]> {
    const { state: extensionState } = useExtensions();
    const { extensionId, entityId, state } = context;
    const debounced = useDebouncedState(state);
    const assetsSettled = extensionState === 'ready' || extensionState === 'error';
    const enabled = sourceEnabled && !!source && (source.kind !== 'JSFunction' || assetsSettled);
    const resolvedContext = useMemo<OptionsSourceContext>(
        () => ({ extensionId, entityId, state: debounced.state }),
        [extensionId, entityId, debounced.state],
    );
    const queryKey = enabled
        ? optionsSourceQueryKeys.resolve(
              source,
              extensionId ?? '',
              entityId ?? '',
              source?.kind === 'JSFunction' ? debounced.revision : 0,
          )
        : ['options-source', 'inactive'];

    // eslint-disable-next-line @tanstack/query/exhaustive-deps -- the opaque revision keeps credentials out of query keys
    return useQuery<SettingSchemaOption[]>({
        queryKey,
        queryFn: () => (source ? resolveOptionsSource(source, resolvedContext) : Promise.resolve([])),
        enabled,
        staleTime: source?.kind === 'JSFunction' ? 0 : 60_000,
        retry: false,
    });
}
