import { useMutation } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';

import { apiGet, buildUrl } from '@/lib/api';

export interface ProviderMetadataCandidate {
    providerType: string;

    externalIdSource: string;

    externalId: string;

    contentType: string;

    level?: string | null;

    title?: string | null;

    year?: number | null;

    releaseDate?: string | null;

    overview?: string | null;

    language?: string | null;

    thumbnailUrl?: string | null;

    inLibrary?: boolean;

    existingContentHierarchyItemId?: string | null;
}

export interface ProviderMetadataCandidateSearchResult {
    continuation?: string | null;

    nextContinuation?: string | null;

    candidates: ProviderMetadataCandidate[];

    pageItemCount: number;

    excludedCount: number;

    pageInfo: ProviderSearchPageInfo;

    warnings?: string[];
}

interface ProviderSearchPageInfo {
    requestedPage: number;

    hasMore: boolean;
}

export interface ProviderMetadataCandidateSearchResponse {
    continuation?: string | null;

    nextContinuation?: string | null;

    matches: Array<{
        candidate: Omit<ProviderMetadataCandidate, 'inLibrary' | 'existingContentHierarchyItemId'>;

        inLibrary: boolean;

        existingContentHierarchyItemId?: string | null;
    }>;

    pageItemCount: number;

    existsInLibraryCount: number;

    pageInfo: ProviderSearchPageInfo;

    warnings?: string[];
}

interface ProviderMetadataCandidateSearchRequest {
    query: string;

    contentType?: string;

    page?: number;

    resume?: boolean;

    continuation?: string;
}

interface MutationRequest extends ProviderMetadataCandidateSearchRequest {
    generation: number;
}

interface SearchExecution {
    identity: string;
    pages: Map<number, string>;
}

interface CompletedSearch {
    query: string;
    page: number;
    execution: SearchExecution;
    data: ProviderMetadataCandidateSearchResult;
}

export function normalizeProviderMetadataCandidateSearchResult(
    response: ProviderMetadataCandidateSearchResponse,
): ProviderMetadataCandidateSearchResult {
    return {
        continuation: response.continuation,
        nextContinuation: response.nextContinuation,
        candidates: response.matches.map((match) => ({
            ...match.candidate,
            inLibrary: match.inLibrary,
            existingContentHierarchyItemId: match.existingContentHierarchyItemId,
        })),
        pageItemCount: response.pageItemCount,
        excludedCount: response.existsInLibraryCount,
        pageInfo: response.pageInfo,
        warnings: response.warnings,
    };
}

async function searchProviderMetadataCandidates(
    request: ProviderMetadataCandidateSearchRequest,
    signal: AbortSignal,
): Promise<ProviderMetadataCandidateSearchResult> {
    const url = buildUrl('/content-search', {
        query: request.query,
        contentType: request.contentType,
        page: request.page,
        continuation: request.continuation,
    });
    const response = await apiGet<ProviderMetadataCandidateSearchResponse>(url, { signal });
    return normalizeProviderMetadataCandidateSearchResult(response);
}

export function useProviderMetadataCandidateSearch() {
    const execution = useRef<SearchExecution | null>(null);
    const activeRequest = useRef<AbortController | null>(null);
    const generation = useRef(0);
    const mountEpoch = useRef(0);
    const mounted = useRef(false);
    const completedRef = useRef<CompletedSearch | null>(null);
    const [completed, setCompleted] = useState<CompletedSearch | null>(null);
    const [status, setStatus] = useState<'idle' | 'pending' | 'success' | 'error'>('idle');
    const mutation = useMutation({
        mutationFn: async (request: MutationRequest) => {
            try {
                if (request.generation !== generation.current || !mounted.current) {
                    throw new DOMException('The search was cancelled.', 'AbortError');
                }
                activeRequest.current?.abort();
                const page = request.page ?? 1;
                const identity = JSON.stringify([request.query, request.contentType ?? null]);
                if (page === 1 && !request.resume) {
                    execution.current = { identity, pages: new Map() };
                }
                const current = execution.current;
                if (!current || current.identity !== identity) {
                    throw new Error('This page belongs to another search. Start a new search.');
                }
                const continuation = current.pages.get(page);
                if (page > 1 && !continuation) {
                    throw new Error('This search page is unavailable. Start a new search.');
                }
                const controller = new AbortController();
                activeRequest.current = controller;
                try {
                    const response = await searchProviderMetadataCandidates(
                        { ...request, continuation },
                        controller.signal,
                    );
                    if (
                        activeRequest.current !== controller ||
                        controller.signal.aborted ||
                        request.generation !== generation.current ||
                        !mounted.current
                    ) {
                        throw new DOMException('The search was cancelled.', 'AbortError');
                    }
                    if (response.continuation) current.pages.set(page, response.continuation);
                    if (response.nextContinuation) current.pages.set(page + 1, response.nextContinuation);
                    const nextCompleted = { query: request.query, page, execution: current, data: response };
                    completedRef.current = nextCompleted;
                    setCompleted(nextCompleted);
                    setStatus('success');
                    return response;
                } finally {
                    if (activeRequest.current === controller) activeRequest.current = null;
                }
            } catch (error) {
                if (request.generation === generation.current && mounted.current) setStatus('error');
                throw error;
            }
        },
    });
    const mutationRef = useRef(mutation);
    mutationRef.current = mutation;
    const executeMutationAsync = mutation.mutateAsync;
    const executeMutation = mutation.mutate;
    const mutateAsync = useCallback(
        (request: ProviderMetadataCandidateSearchRequest) => {
            setStatus('pending');
            return executeMutationAsync({ ...request, generation: ++generation.current });
        },
        [executeMutationAsync],
    );
    const mutate = useCallback(
        (request: ProviderMetadataCandidateSearchRequest, options?: Parameters<typeof executeMutation>[1]) => {
            setStatus('pending');
            executeMutation({ ...request, generation: ++generation.current }, options);
        },
        [executeMutation],
    );
    const cancel = useCallback(() => {
        generation.current++;
        const controller = activeRequest.current;
        activeRequest.current = null;
        controller?.abort();
        execution.current = completedRef.current?.execution ?? null;
        mutationRef.current.reset();
        const previous = completedRef.current;
        setStatus(previous ? 'success' : 'idle');
        return previous ? { query: previous.query, page: previous.page } : null;
    }, []);
    const reset = useCallback(() => {
        generation.current++;
        const controller = activeRequest.current;
        activeRequest.current = null;
        controller?.abort();
        execution.current = null;
        completedRef.current = null;
        setCompleted(null);
        setStatus('idle');
        mutationRef.current.reset();
    }, []);
    useEffect(() => {
        mounted.current = true;
        const epoch = ++mountEpoch.current;
        const epochRef = mountEpoch;
        const generationRef = generation;
        const requestRef = activeRequest;
        return () => {
            mounted.current = false;
            // Wait for an effect replay before treating cleanup as a real unmount.
            queueMicrotask(() => {
                if (epochRef.current !== epoch) return;
                generationRef.current++;
                const controller = requestRef.current;
                requestRef.current = null;
                controller?.abort();
            });
        };
    }, []);
    return {
        ...mutation,
        mutate,
        mutateAsync,
        status,
        isPending: status === 'pending',
        isSuccess: status === 'success',
        isError: status === 'error',
        isIdle: status === 'idle',
        data: completed?.data ?? mutation.data,
        cancel,
        reset,
    };
}
