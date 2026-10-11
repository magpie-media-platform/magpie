import { LocalContentLink } from '@/components/content/LocalContentPreview';
import { type ColumnDef } from '@tanstack/react-table';
import { CaretDownIcon, CheckIcon, PlusIcon } from '@phosphor-icons/react';

import { DisabledTooltip } from '@/auth/DisabledTooltip';
import { ContentPoster } from '@/components/content/ContentCardPrimitives';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuTrigger,
} from '@/components/ui/DropdownMenu';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { type ApiRootFolder } from '@/hooks/useRootFolders';
import { formatPublishDate } from '@/lib/dateFormatting';
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import type { TemporalPrecision } from '@/types/contentMetadata';

export interface AddableCandidate {
    key: string;
    providerType: string;
    externalIdSource: string;
    externalId: string;
    contentType: string;
    levelSlug?: string;
    title: string;
    year?: string;
    yearValue?: number;
    releaseDate?: string;
    releaseDatePrecision?: TemporalPrecision;
    overview?: string | null;
    thumbnailUrl?: string | null;
    inLibrary: boolean;
    existingContentHierarchyItemId?: string;
}

export interface RowActionContext {
    addingKey: string | null;
    foldersLoading: boolean;
    foldersForContentType: (contentType: string) => ApiRootFolder[];
    onAdd: (candidate: AddableCandidate, rootFolderId: string) => void;
    onPreview: (candidate: AddableCandidate) => void;
}

interface RowActionsProps {
    candidate: AddableCandidate;
    ctx: RowActionContext;
}

export function RowActions({ candidate, ctx }: RowActionsProps) {
    if (candidate.inLibrary) {
        return (
            <div className="library-add-page__row-actions">
                <div className="library-add-page__result-buttons">
                    <Button
                        variant="primary"
                        size="sm"
                        disabled
                        data-action="in-library"
                        data-result-key={candidate.key}
                    >
                        <CheckIcon weight="bold" aria-hidden="true" />
                        In Library
                    </Button>
                </div>
            </div>
        );
    }

    const availableFolders = ctx.foldersForContentType(candidate.contentType);
    const hasFolders = availableFolders.length > 0;
    const multipleFolders = availableFolders.length > 1;

    const isAdding = ctx.addingKey === candidate.key;
    const isAnyAdding = ctx.addingKey !== null;

    const addBlockedReason =
        !hasFolders && !ctx.foldersLoading ? 'Configure a root folder for this content type before adding.' : null;
    const addDisabled = isAnyAdding || !hasFolders || ctx.foldersLoading;

    const addLabel = (
        <>
            <PlusIcon weight="bold" aria-hidden="true" />
            Add
            {multipleFolders && <CaretDownIcon weight="bold" aria-hidden="true" />}
        </>
    );

    return (
        <div className="library-add-page__row-actions">
            <div className="library-add-page__result-buttons">
                {multipleFolders ? (
                    <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                            <Button
                                variant="primary"
                                size="sm"
                                loading={isAdding}
                                disabled={addDisabled}
                                data-action="add-content"
                                data-result-key={candidate.key}
                            >
                                {addLabel}
                            </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end" sideOffset={4}>
                            <DropdownMenuLabel>Add to root folder</DropdownMenuLabel>
                            {availableFolders.map((folder) => (
                                <DropdownMenuItem
                                    key={folder.id}
                                    onSelect={() => ctx.onAdd(candidate, folder.id)}
                                    data-action="add-to-folder"
                                    data-folder-id={folder.id}
                                >
                                    {folder.name}
                                </DropdownMenuItem>
                            ))}
                        </DropdownMenuContent>
                    </DropdownMenu>
                ) : (
                    <DisabledTooltip disabled={addBlockedReason !== null} message={addBlockedReason ?? ''}>
                        <Button
                            variant="primary"
                            size="sm"
                            onClick={() => hasFolders && ctx.onAdd(candidate, availableFolders[0].id)}
                            loading={isAdding}
                            disabled={addDisabled}
                            data-action="add-content"
                            data-result-key={candidate.key}
                        >
                            {addLabel}
                        </Button>
                    </DisabledTooltip>
                )}
            </div>
        </div>
    );
}

export function TitleHeading({
    candidate,
    onPreview,
}: {
    candidate: AddableCandidate;
    onPreview: (candidate: AddableCandidate) => void;
}) {
    return (
        <h3 className="library-add-page__title">
            {candidate.inLibrary ? (
                candidate.existingContentHierarchyItemId ? (
                    <LocalContentLink
                        contentId={candidate.existingContentHierarchyItemId}
                        className="library-add-page__title-link"
                        data-action="open-library-item"
                    >
                        {candidate.title}
                    </LocalContentLink>
                ) : (
                    candidate.title
                )
            ) : (
                <button
                    type="button"
                    className="library-add-page__title-link"
                    onClick={() => onPreview(candidate)}
                    data-action="preview-content"
                    data-result-key={candidate.key}
                >
                    {candidate.title}
                </button>
            )}
            {candidate.year && <span className="library-add-page__title-year">{candidate.year}</span>}
        </h3>
    );
}

function TitleCell({
    candidate,
    onPreview,
}: {
    candidate: AddableCandidate;
    onPreview: (candidate: AddableCandidate) => void;
}) {
    return (
        <div className="library-add-page__title-cell">
            <TitleHeading candidate={candidate} onPreview={onPreview} />
            {candidate.overview && <p className="library-add-page__title-overview">{candidate.overview}</p>}
        </div>
    );
}

export function TypeCell({ candidate }: { candidate: AddableCandidate }) {
    const { contentTypes } = useLibraryTypes();
    const label = resolveContentTypeLabel(
        { level: candidate.levelSlug, contentType: candidate.contentType },
        contentTypes,
    );
    if (!label) {
        return <span className="library-add-page__muted-cell">-</span>;
    }
    return (
        <Badge variant="default" size="sm">
            {label}
        </Badge>
    );
}

export function formatPublished({ releaseDate, releaseDatePrecision }: AddableCandidate): string {
    return formatPublishDate(releaseDate, '--', releaseDatePrecision);
}

function PublishedCell({ candidate }: { candidate: AddableCandidate }) {
    return <span className="library-add-page__published">{formatPublished(candidate)}</span>;
}

export function SourceCell({ candidate }: { candidate: AddableCandidate }) {
    return (
        <div className="library-add-page__source-cell">
            <Badge size="sm" variant="info" badgeStyle="outline">
                {candidate.providerType}
            </Badge>
        </div>
    );
}

interface BuildAddableColumnsConfig {
    rowActionContext: RowActionContext;
}

export function buildAddableColumns({ rowActionContext }: BuildAddableColumnsConfig): ColumnDef<AddableCandidate>[] {
    return [
        {
            id: 'poster',
            header: '',
            enableSorting: false,
            size: 64,
            cell: ({ row }) => (
                <ContentPoster
                    posterUrl={row.original.thumbnailUrl}
                    alt=""
                    className="library-add-page__poster"
                    placeholderClassName="library-add-page__poster--placeholder"
                    placeholder={null}
                    data-testid="library-add-page__result-image"
                />
            ),
        },
        {
            id: 'title',
            accessorFn: (row) => row.title,
            header: 'Title',
            sortingFn: (a, b) => a.original.title.localeCompare(b.original.title),
            cell: ({ row }) => <TitleCell candidate={row.original} onPreview={rowActionContext.onPreview} />,
        },
        {
            id: 'type',
            accessorFn: (row) => row.levelSlug ?? row.contentType,
            header: 'Type',
            size: 110,
            cell: ({ row }) => <TypeCell candidate={row.original} />,
        },
        {
            id: 'published',
            accessorFn: (row) => row.yearValue,
            header: 'Published',
            size: 110,
            sortUndefined: 'last',
            cell: ({ row }) => <PublishedCell candidate={row.original} />,
        },
        {
            id: 'source',
            accessorFn: (row) => row.providerType,
            header: 'Source',
            size: 180,
            enableSorting: false,
            cell: ({ row }) => <SourceCell candidate={row.original} />,
        },
        {
            id: 'action',
            header: '',
            enableSorting: false,
            size: 110,
            cell: ({ row }) => <RowActions candidate={row.original} ctx={rowActionContext} />,
        },
    ];
}
