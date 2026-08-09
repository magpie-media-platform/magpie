import { type ColumnDef } from '@tanstack/react-table';
import { CaretDownIcon, CheckIcon, PlusIcon } from '@phosphor-icons/react';
import { Link } from 'react-router-dom';

import { DisabledTooltip } from '@/auth/DisabledTooltip';
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
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';

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
    overview?: string | null;
    thumbnailUrl?: string | null;
    inLibrary: boolean;
    existingContentHierarchyItemId?: string;
}

export interface RowActionContext {
    addingKey: string | null;
    addedKeys: Set<string>;
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
    const inLibrary = candidate.inLibrary;
    if (inLibrary) {
        return null;
    }

    const availableFolders = ctx.foldersForContentType(candidate.contentType);
    const hasFolders = availableFolders.length > 0;
    const multipleFolders = availableFolders.length > 1;

    const isAdding = ctx.addingKey === candidate.key;
    const isAnyAdding = ctx.addingKey !== null;
    const isAdded = ctx.addedKeys.has(candidate.key);

    const addBlockedReason =
        !hasFolders && !ctx.foldersLoading ? 'Configure a root folder for this content type before adding.' : null;
    const addDisabled = isAnyAdding || isAdded || !hasFolders || ctx.foldersLoading;

    const addLabel = isAdded ? (
        <>
            <CheckIcon weight="bold" aria-hidden="true" />
            Added
        </>
    ) : (
        <>
            <PlusIcon weight="bold" aria-hidden="true" />
            Add
            {multipleFolders && <CaretDownIcon weight="bold" aria-hidden="true" />}
        </>
    );

    return (
        <div className="library-add-page__row-actions">
            <div className="library-add-page__result-buttons">
                {multipleFolders && !isAdded ? (
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

function PosterCell({ candidate }: { candidate: AddableCandidate }) {
    const image = candidate.thumbnailUrl ? (
        <img
            src={candidate.thumbnailUrl}
            alt=""
            className="library-add-page__poster"
            loading="lazy"
            data-testid="library-add-page__result-image"
        />
    ) : (
        <div className="library-add-page__poster library-add-page__poster--placeholder" aria-hidden="true" />
    );
    return image;
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
                    <Link
                        to={`/library/${candidate.existingContentHierarchyItemId}`}
                        className="library-add-page__title-link"
                        data-action="open-library-item"
                    >
                        {candidate.title}
                    </Link>
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

const PUBLISHED_DATE_FORMAT = new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'UTC',
});

export function formatPublished(releaseDate: string | undefined): string {
    if (!releaseDate) {
        return '--';
    }
    const parsed = new Date(releaseDate);
    return Number.isNaN(parsed.getTime()) ? '--' : PUBLISHED_DATE_FORMAT.format(parsed);
}

function PublishedCell({ candidate }: { candidate: AddableCandidate }) {
    return <span className="library-add-page__published">{formatPublished(candidate.releaseDate)}</span>;
}

export function SourceCell({ candidate }: { candidate: AddableCandidate }) {
    return (
        <div className="library-add-page__source-cell">
            {candidate.inLibrary && (
                <Badge
                    size="sm"
                    variant="success"
                    badgeStyle="outline"
                    data-testid="library-add-page__in-library-badge"
                >
                    <CheckIcon weight="bold" aria-hidden="true" />
                    In library
                </Badge>
            )}
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
            cell: ({ row }) => <PosterCell candidate={row.original} />,
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
