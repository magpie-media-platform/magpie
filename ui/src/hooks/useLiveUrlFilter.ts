import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { NavigationType, useLocation, useNavigationType } from 'react-router-dom';

interface UseLiveUrlFilterOptions {
    value: string;
    onCommit: (value: string, replace: boolean, state: { liveFilterCommit: string }) => void;
    externalKey?: string;
    replace?: boolean;
    delay?: number;
}

export function useLiveUrlFilter({
    value,
    onCommit,
    externalKey = '',
    replace = false,
    delay = 300,
}: UseLiveUrlFilterOptions) {
    const location = useLocation();
    const navigationType = useNavigationType();
    const id = useId();
    const scope = location.pathname;
    const [draft, setDraft] = useState({ scope, value });
    const visibleValue = draft.scope === scope ? draft.value : value;
    const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const sessionRef = useRef(false);
    const pendingCommitsRef = useRef(new Set<string>());
    const commitSequenceRef = useRef(0);
    const revisionRef = useRef(0);
    const draftValueRef = useRef(visibleValue);
    const editedRef = useRef(false);
    const currentScopeRef = useRef(scope);
    const onCommitRef = useRef(onCommit);
    const sourceValueRef = useRef(value);
    const observedRef = useRef({ key: location.key, pathname: scope, search: location.search, value, externalKey });
    onCommitRef.current = onCommit;
    sourceValueRef.current = value;
    currentScopeRef.current = scope;

    const cancelPending = useCallback(() => {
        if (timerRef.current !== null) {
            clearTimeout(timerRef.current);
            timerRef.current = null;
        }
    }, []);

    const createCommitState = useCallback(() => {
        const commit = `${id}:${++commitSequenceRef.current}`;
        pendingCommitsRef.current.add(commit);
        return { liveFilterCommit: commit };
    }, [id]);

    const commitValue = useCallback(
        (nextValue: string, force = false) => {
            if (!force && nextValue === sourceValueRef.current) {
                return;
            }
            onCommitRef.current(nextValue, replace || sessionRef.current, createCommitState());
            sessionRef.current = true;
        },
        [createCommitState, replace],
    );

    useEffect(() => {
        const previous = observedRef.current;
        const changed =
            previous.key !== location.key || previous.pathname !== scope || previous.search !== location.search;
        observedRef.current = { key: location.key, pathname: scope, search: location.search, value, externalKey };
        if (!changed && previous.value === value && previous.externalKey === externalKey) {
            return;
        }
        const commit = (location.state as { liveFilterCommit?: string } | null)?.liveFilterCommit;
        if (
            changed &&
            previous.pathname === scope &&
            navigationType !== NavigationType.Pop &&
            commit &&
            pendingCommitsRef.current.delete(commit)
        ) {
            return;
        }
        if (
            changed &&
            previous.pathname === scope &&
            navigationType !== NavigationType.Pop &&
            previous.search !== location.search &&
            previous.value === value &&
            previous.externalKey === externalKey
        ) {
            if (timerRef.current === null) {
                sessionRef.current = false;
            }
            return;
        }
        if (!changed && previous.externalKey === externalKey && editedRef.current) {
            if (timerRef.current === null) {
                commitValue(draftValueRef.current);
            }
            return;
        }
        if (changed || previous.value !== value || previous.externalKey !== externalKey) {
            cancelPending();
            pendingCommitsRef.current.clear();
            sessionRef.current = false;
            editedRef.current = false;
            revisionRef.current += 1;
            draftValueRef.current = value;
            setDraft({ scope, value });
        }
    }, [
        cancelPending,
        commitValue,
        externalKey,
        location.key,
        location.search,
        location.state,
        navigationType,
        scope,
        value,
    ]);

    useEffect(() => cancelPending, [cancelPending]);

    const change = useCallback(
        (nextValue: string, immediate = false, force = false) => {
            cancelPending();
            revisionRef.current += 1;
            draftValueRef.current = nextValue;
            editedRef.current = true;
            setDraft({ scope, value: nextValue });
            if (immediate) {
                commitValue(nextValue, force);
                return;
            }
            timerRef.current = setTimeout(() => {
                timerRef.current = null;
                if (currentScopeRef.current !== scope) {
                    return;
                }
                commitValue(nextValue);
            }, delay);
        },
        [cancelPending, commitValue, delay, scope],
    );

    const getRevision = useCallback(() => revisionRef.current, []);

    return { value: visibleValue, change, cancelPending, getRevision, createCommitState };
}
