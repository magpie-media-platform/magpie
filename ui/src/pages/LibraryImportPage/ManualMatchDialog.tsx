import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { EyeIcon, LinkSimpleIcon, MagnifyingGlassIcon, SparkleIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Pager } from '@/components/ui/Pager';
import { PreviewDrawer, type PreviewDrawerTarget } from '@/components/content/PreviewDrawer';
import { ProviderSearchWarnings } from '@/components/content/ProviderSearchWarnings';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';
import {
    useProviderMetadataCandidateSearch,
    type ProviderMetadataCandidate,
} from '@/hooks/useProviderMetadataCandidateSearch';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useSetLibraryImportMatch } from '@/hooks/useSetLibraryImportMatch';
import { useSetLibraryImportCandidateGroupMatch } from '@/hooks/useSetLibraryImportCandidateGroupMatch';
import {
    getParsedSignalNumber,
    getParsedSignalValue,
    PARSED_FILE_SIGNAL_TITLE,
    PARSED_FILE_SIGNAL_YEAR,
    type LibraryImportCandidate,
    type ParsedSignalBag,
} from '@/hooks/useLibraryImportCandidates';
import type { LibraryImportCandidateGroup } from '@/hooks/useLibraryImportCandidateGroups';
import { isApiError } from '@/lib/api';
import { candidateSearchErrorMessage } from '@/lib/errors';
import { escapeQueryString } from '@/lib/simpleFilterQuery';

import { fileName } from './importDisplay';
import './ManualMatchDialog.css';

const ANY_CONTENT_TYPE = '__any__';

interface MatchSearchRequest {
    queryText: string;

    title: string;

    year: string;

    contentType: string;
}

function candidateKey(candidate: ProviderMetadataCandidate): string {
    return `${candidate.providerType}:${candidate.externalIdSource}:${candidate.externalId}`;
}

function splitTitleYear(title: string, year: number | undefined): { title: string; year: string } {
    const trailing = title.match(/^(.+?)\s+(\d{4})$/);
    if (trailing) {
        const embedded = Number(trailing[2]);
        if (year === undefined) {
            return { title: trailing[1].trim(), year: String(embedded) };
        }
        if (year === embedded) {
            return { title: trailing[1].trim(), year: String(year) };
        }
    }
    return { title, year: year === undefined ? '' : String(year) };
}

function splitSignalTitleYear(parsedSignals: ParsedSignalBag | null | undefined): { title: string; year: string } {
    const title = getParsedSignalValue(parsedSignals, PARSED_FILE_SIGNAL_TITLE);
    const year = getParsedSignalNumber(parsedSignals, PARSED_FILE_SIGNAL_YEAR) ?? undefined;
    return splitTitleYear(title, year);
}

function buildMatchQuery(title: string, year: string, contentType: string): string {
    const clauses: string[] = [];
    if (contentType) {
        clauses.push(`contentType = "${escapeQueryString(contentType)}"`);
    }
    const trimmedTitle = title.trim();
    if (trimmedTitle) {
        clauses.push(`title contains "${escapeQueryString(trimmedTitle)}"`);
    }
    const trimmedYear = year.trim();
    if (/^\d{1,4}$/.test(trimmedYear)) {
        clauses.push(`(publishDate >= "${trimmedYear}-01-01" and publishDate <= "${trimmedYear}-12-31")`);
    }
    return clauses.join(' and ');
}

export type ManualMatchTarget =
    | { kind: 'candidate'; candidate: LibraryImportCandidate }
    | { kind: 'group'; group: LibraryImportCandidateGroup; rootFolderId?: string };

function targetKey(target: ManualMatchTarget): string {
    return target.kind === 'candidate' ? `candidate:${target.candidate.id}` : `group:${target.group.groupKey}`;
}

function targetLabel(target: ManualMatchTarget): string {
    return target.kind === 'candidate' ? fileName(target.candidate.filePath) : target.group.title;
}

function targetContentType(target: ManualMatchTarget): string {
    return (target.kind === 'candidate' ? target.candidate.contentType : target.group.contentType) ?? '';
}

function targetSeed(target: ManualMatchTarget): { title: string; year: string } {
    return target.kind === 'candidate'
        ? splitSignalTitleYear(target.candidate.parsedSignals)
        : splitTitleYear(target.group.title, undefined);
}

interface ManualMatchDialogProps {
    target: ManualMatchTarget | null;
    onClose: () => void;
    onMatched: () => void;
}

export function ManualMatchDialog({ target, onClose, onMatched }: ManualMatchDialogProps) {
    const { toast } = useToast();
    const candidateSearch = useProviderMetadataCandidateSearch();
    const setCandidateMatch = useSetLibraryImportMatch();
    const setGroupMatch = useSetLibraryImportCandidateGroupMatch();
    const { contentTypes, isLoading: registryLoading } = useLibraryTypes();

    const titleInputRef = useRef<HTMLInputElement>(null);

    const [title, setTitle] = useState<string>('');
    const [year, setYear] = useState<string>('');
    const [contentType, setContentType] = useState<string>('');
    const [page, setPage] = useState<number>(1);
    const [candidateSearchError, setCandidateSearchError] = useState<string | null>(null);
    const [matchingKey, setMatchingKey] = useState<string | null>(null);

    const [previewTarget, setPreviewTarget] = useState<PreviewDrawerTarget | null>(null);
    const [previewOpen, setPreviewOpen] = useState(false);

    const seededForRef = useRef<string | null>(null);
    const lastSearchRef = useRef<MatchSearchRequest | null>(null);
    const resetCandidateSearch = candidateSearch.reset;
    useEffect(() => {
        if (target && seededForRef.current !== targetKey(target)) {
            seededForRef.current = targetKey(target);
            const seeded = targetSeed(target);
            setTitle(seeded.title);
            setYear(seeded.year);
            setContentType(targetContentType(target));
            setPage(1);
            setCandidateSearchError(null);
            lastSearchRef.current = null;
            resetCandidateSearch();
        } else if (!target) {
            seededForRef.current = null;
        }
    }, [target, resetCandidateSearch]);

    const handleYearChange = useCallback((raw: string) => {
        setYear(raw.replace(/\D/g, '').slice(0, 4));
    }, []);

    const runCandidateSearch = useCallback(
        async (request: MatchSearchRequest, candidateSearchPage: number) => {
            setCandidateSearchError(null);
            lastSearchRef.current = request;

            try {
                await candidateSearch.mutateAsync({
                    query: request.queryText,
                    page: candidateSearchPage,
                });
            } catch (error) {
                setCandidateSearchError(candidateSearchErrorMessage(error));
            }
        },
        [candidateSearch],
    );

    const runSearch = useCallback(() => {
        setPage(1);
        return runCandidateSearch(
            { queryText: buildMatchQuery(title, year, contentType), title, year, contentType },
            1,
        );
    }, [title, year, contentType, runCandidateSearch]);

    const goToPage = useCallback(
        (nextPage: number) => {
            const clampedPage = Math.max(1, nextPage);
            setPage(clampedPage);
            if (lastSearchRef.current) {
                void runCandidateSearch(lastSearchRef.current, clampedPage);
            }
        },
        [runCandidateSearch],
    );

    const handlePreview = useCallback((candidate: ProviderMetadataCandidate) => {
        setPreviewTarget({
            providerType: candidate.providerType,
            externalIdKey: candidate.externalIdSource,
            externalId: candidate.externalId,
            contentType: candidate.contentType,
            levelSlug: candidate.level ?? undefined,
            fallbackTitle: candidate.title?.trim() || 'Untitled',
            fallbackYear: candidate.year != null ? String(candidate.year) : undefined,
        });
        setPreviewOpen(true);
    }, []);

    const handleMatch = useCallback(
        async (candidate: ProviderMetadataCandidate) => {
            if (!target) {
                return;
            }
            const key = candidateKey(candidate);
            setMatchingKey(key);
            try {
                const matchFields = {
                    providerType: candidate.providerType,
                    externalIdKey: candidate.externalIdSource,
                    externalId: candidate.externalId,
                    title: candidate.title?.trim() || 'Untitled',
                    subtitle: candidate.year != null ? String(candidate.year) : undefined,
                    imageUrl: candidate.thumbnailUrl ?? undefined,
                    levelSlug: candidate.level ?? undefined,
                };
                if (target.kind === 'candidate') {
                    await setCandidateMatch.mutateAsync({ id: target.candidate.id, ...matchFields });
                } else {
                    await setGroupMatch.mutateAsync({
                        groupKey: target.group.groupKey,
                        rootFolderId: target.rootFolderId,
                        ...matchFields,
                    });
                }
                toast({
                    title: 'Match set',
                    description:
                        target.kind === 'candidate'
                            ? `"${candidate.title?.trim() || 'Untitled'}" is now matched to this file.`
                            : `"${candidate.title?.trim() || 'Untitled'}" is now matched to every candidate in this group. Import it when ready.`,
                    variant: 'success',
                });
                onMatched();
                onClose();
            } catch (error) {
                if (isApiError(error) && error.status === 404) {
                    toast({
                        title: target.kind === 'group' ? 'Group is gone' : 'File is gone',
                        description:
                            target.kind === 'group'
                                ? 'The queue was out of date. Magpie refreshed it and removed this group.'
                                : 'The queue was out of date. Magpie refreshed it and removed this file.',
                        variant: 'error',
                    });
                    onClose();
                } else if (isApiError(error) && error.status === 400) {
                    toast({
                        title: 'Failed to set match',
                        description: 'This result is missing the identifiers Magpie needs. Pick a different result.',
                        variant: 'error',
                    });
                } else {
                    toast({
                        title: 'Failed to set match',
                        description: 'Magpie could not set the match. Please try again.',
                        variant: 'error',
                    });
                }
            } finally {
                setMatchingKey(null);
            }
        },
        [target, setCandidateMatch, setGroupMatch, toast, onMatched, onClose],
    );

    const contentTypeLabel = useMemo(() => {
        if (!contentType) {
            return 'Any';
        }
        return contentTypes.find((t) => t.slug === contentType)?.name ?? contentType;
    }, [contentTypes, contentType]);

    const activeData = candidateSearch.data;
    const candidates = useMemo(() => activeData?.candidates ?? [], [activeData]);
    const warnings = activeData?.warnings ?? [];
    const searchPending = candidateSearch.isPending;
    const hasSearched = candidateSearch.isSuccess || candidateSearch.isError || searchPending;
    const canGoPrev = page > 1;
    const canGoNext = activeData?.pageInfo.hasMore ?? false;
    const hasResultsForPaging =
        !candidateSearchError && !searchPending && hasSearched && (candidates.length > 0 || canGoPrev || canGoNext);

    return (
        <>
            <Dialog open={target !== null} onOpenChange={(open) => !open && onClose()}>
                <DialogContent
                    data-component="manual-match-dialog"
                    onOpenAutoFocus={(event) => {
                        event.preventDefault();
                        const input = titleInputRef.current;
                        if (input) {
                            input.focus({ preventScroll: true });
                            const end = input.value.length;
                            input.setSelectionRange(end, end);
                        }
                    }}
                >
                    <DialogHeader>
                        <DialogTitle>Find a match</DialogTitle>
                        <DialogDescription className="manual-match-dialog__description">
                            {target
                                ? target.kind === 'candidate'
                                    ? `Search for a match for "${targetLabel(target)}". Matching does not import the file.`
                                    : `Search for a match for "${targetLabel(target)}". Matching applies to every candidate in this group.`
                                : ''}
                        </DialogDescription>
                    </DialogHeader>

                    <DialogBody className="manual-match-dialog__body">
                        <section className="manual-match-dialog__section">
                            <div className="manual-match-dialog__search-row">
                                <div className="manual-match-dialog__fields">
                                    <div className="manual-match-dialog__field manual-match-dialog__field--title">
                                        <Label htmlFor="manual-match-title">Title</Label>
                                        <Input
                                            ref={titleInputRef}
                                            id="manual-match-title"
                                            size="sm"
                                            value={title}
                                            onChange={(e) => setTitle(e.target.value)}
                                            placeholder="Title to search for"
                                            data-field="title"
                                        />
                                    </div>
                                    <div className="manual-match-dialog__field manual-match-dialog__field--year">
                                        <Label htmlFor="manual-match-year">Release year</Label>
                                        <Input
                                            id="manual-match-year"
                                            size="sm"
                                            type="number"
                                            inputMode="numeric"
                                            value={year}
                                            onChange={(e) => handleYearChange(e.target.value)}
                                            placeholder="1999"
                                            data-field="year"
                                        />
                                    </div>
                                    <div className="manual-match-dialog__field manual-match-dialog__field--content-type">
                                        <Label htmlFor="manual-match-content-type">Content type</Label>
                                        <Select
                                            value={contentType || ANY_CONTENT_TYPE}
                                            onValueChange={(v) => setContentType(v === ANY_CONTENT_TYPE ? '' : v)}
                                            disabled={registryLoading}
                                        >
                                            <SelectTrigger id="manual-match-content-type" size="sm" placeholder="Any">
                                                {contentTypeLabel}
                                            </SelectTrigger>
                                            <SelectContent>
                                                <SelectItem value={ANY_CONTENT_TYPE}>Any</SelectItem>
                                                {contentTypes.map((t) => (
                                                    <SelectItem key={t.slug} value={t.slug}>
                                                        {t.name}
                                                    </SelectItem>
                                                ))}
                                            </SelectContent>
                                        </Select>
                                    </div>
                                </div>
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={runSearch}
                                    loading={searchPending}
                                    data-action="search"
                                >
                                    <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                                    Search
                                </Button>
                            </div>
                        </section>

                        {candidateSearchError && (
                            <EmptyState
                                icon={<WarningCircleIcon weight="duotone" />}
                                title="Search failed"
                                description={candidateSearchError}
                                size="sm"
                                data-testid="manual-match-dialog__error"
                            />
                        )}

                        {!candidateSearchError && searchPending && (
                            <div className="manual-match-dialog__results" data-testid="manual-match-dialog__loading">
                                <Skeleton style={{ height: 48 }} />
                                <Skeleton style={{ height: 48 }} />
                                <Skeleton style={{ height: 48 }} />
                            </div>
                        )}

                        {!candidateSearchError && !searchPending && hasSearched && candidates.length === 0 && (
                            <EmptyState
                                icon={<SparkleIcon weight="duotone" />}
                                title="No matches"
                                description={
                                    warnings.length > 0
                                        ? 'No usable matches were returned. Review the provider warnings and adjust the search.'
                                        : 'No matches were found for that search. Adjust the filter and try again.'
                                }
                                size="sm"
                                data-testid="manual-match-dialog__empty"
                            />
                        )}

                        {!candidateSearchError && !searchPending && hasSearched && warnings.length > 0 && (
                            <ProviderSearchWarnings warnings={warnings} hasResults={candidates.length > 0} />
                        )}

                        {!candidateSearchError && !searchPending && candidates.length > 0 && (
                            <ul className="manual-match-dialog__results" data-testid="manual-match-dialog__results">
                                {candidates.map((candidate) => {
                                    const key = candidateKey(candidate);
                                    const candidateTitle = candidate.title?.trim() || 'Untitled';
                                    return (
                                        <li
                                            key={key}
                                            className="manual-match-dialog__candidate"
                                            data-candidate-key={key}
                                        >
                                            <div className="manual-match-dialog__candidate-info">
                                                <span className="manual-match-dialog__candidate-title">
                                                    {candidateTitle}
                                                    {candidate.year != null && (
                                                        <span className="manual-match-dialog__candidate-year">
                                                            {candidate.year}
                                                        </span>
                                                    )}
                                                </span>
                                                <Badge size="sm" variant="info" badgeStyle="outline">
                                                    {candidate.providerType}
                                                </Badge>
                                            </div>
                                            <div className="manual-match-dialog__candidate-actions">
                                                <Button
                                                    variant="secondary"
                                                    size="sm"
                                                    onClick={() => handlePreview(candidate)}
                                                    data-action="preview"
                                                >
                                                    <EyeIcon weight="bold" aria-hidden="true" />
                                                    Detail
                                                </Button>
                                                <Button
                                                    variant="primary"
                                                    size="sm"
                                                    onClick={() => handleMatch(candidate)}
                                                    loading={matchingKey === key}
                                                    disabled={matchingKey !== null}
                                                    data-action="match"
                                                >
                                                    <LinkSimpleIcon weight="bold" aria-hidden="true" />
                                                    Match
                                                </Button>
                                            </div>
                                        </li>
                                    );
                                })}
                            </ul>
                        )}

                        {hasResultsForPaging && (
                            <Pager
                                className="manual-match-dialog__pagination"
                                aria-label="Match result pages"
                                data-testid="manual-match-dialog__pagination"
                                page={page}
                                canPrevious={canGoPrev}
                                canNext={canGoNext}
                                onFirst={() => goToPage(1)}
                                onPrevious={() => goToPage(page - 1)}
                                onNext={() => goToPage(page + 1)}
                                onLast={() => goToPage(page)}
                            />
                        )}
                    </DialogBody>
                </DialogContent>
            </Dialog>

            <PreviewDrawer open={previewOpen} onOpenChange={setPreviewOpen} target={previewTarget} />
        </>
    );
}

ManualMatchDialog.displayName = 'ManualMatchDialog';

export default ManualMatchDialog;
