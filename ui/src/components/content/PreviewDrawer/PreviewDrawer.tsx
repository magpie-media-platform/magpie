import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react';
import { useLocalContentPreview } from '@/hooks/useLocalContentPreview';
import {
    PlusIcon,
    CheckIcon,
    CaretDownIcon,
    FilmStripIcon,
    InfoIcon,
    WarningCircleIcon,
    UserIcon,
} from '@phosphor-icons/react';

import { ContentPoster } from '@/components/content/ContentCardPrimitives';
import { Avatar } from '@/components/ui/Avatar';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuTrigger,
} from '@/components/ui/DropdownMenu';
import { EmptyState } from '@/components/ui/EmptyState';
import { Sheet, SheetContent, SheetHeader, SheetFooter, SheetTitle, SheetDescription } from '@/components/ui/Sheet';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tooltip } from '@/components/ui/Tooltip';
import { PreviewPlayer, type PreviewClip } from '@/components/ui/PreviewPlayer';
import {
    usePreviewByExternalId,
    type ContentPreview,
    type ContentPreviewRelatedSubject,
} from '@/hooks/usePreviewByExternalId';
import { useLibraryTypes, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { isNotFoundError } from '@/lib/errors';
import { returnFocusOnClose } from '@/lib/returnFocus';
import { formatTimeSpanDuration } from '@/lib/formatTime';
import { resolveSubjectKindLabel, resolveSubjectKindPluralLabel } from '@/types';

import './PreviewDrawer.css';

export interface PreviewDrawerTarget {
    providerType: string;
    externalIdKey: string;
    externalId: string;
    contentType: string;
    levelSlug?: string;
    fallbackTitle: string;
    fallbackYear?: string;
}

export interface PreviewDrawerProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    target: PreviewDrawerTarget | null;
    isAdded?: boolean;
    isAdding?: boolean;
    addDisabledReason?: string | null;
    addOptions?: Array<{ id: string; label: string }>;
    onAdd?: (target: PreviewDrawerTarget, optionId?: string) => void;
    localContentId?: string;
    localIdentity?: 'hierarchy' | 'item';
    onOpenContent?: (contentId: string) => void;
    onCloseAutoFocus?: (event: Event) => void;
}

function pickHero(preview: ContentPreview): PreviewClip | undefined {
    const clips = preview.previews ?? [];
    if (clips.length === 0) {
        return undefined;
    }
    const primary = clips[0];
    return {
        url: primary.url,
        kind: primary.kind,
        title: primary.title,
        embedKind: primary.embedKind,
    };
}

function pickArtwork(preview: ContentPreview): string | undefined {
    return preview.images?.[0]?.url;
}

function groupRelatedSubjects(subjects: ContentPreviewRelatedSubject[]) {
    const grouped = new Map<string, ContentPreviewRelatedSubject[]>();
    for (const subject of subjects) {
        const kind = subject.kind.trim() || 'other';
        const group = grouped.get(kind) ?? [];
        group.push(subject);
        grouped.set(kind, group);
    }
    return Array.from(grouped, ([kind, group]) => ({ kind, subjects: group.slice(0, 12) }));
}

function subjectKindGroupLabel(kind: string, count: number, subjectTypes: LibraryTypeDefinition[]) {
    const type = subjectTypes.find((subjectType) => subjectType.slug === kind);
    return count === 1
        ? resolveSubjectKindLabel(kind, type?.name)
        : resolveSubjectKindPluralLabel(kind, type?.pluralName, type?.name);
}

export function PreviewDrawer({
    open,
    onOpenChange,
    target,
    isAdded = false,
    isAdding = false,
    addDisabledReason,
    addOptions,
    onAdd,
    localContentId,
    localIdentity,
    onOpenContent,
    onCloseAutoFocus,
}: PreviewDrawerProps) {
    const { subjectTypes } = useLibraryTypes();
    const previewMutation = usePreviewByExternalId();
    const { mutate: runPreview, reset: resetPreview } = previewMutation;
    const localPreview = useLocalContentPreview(open ? localContentId : undefined, localIdentity);
    const { data, error, isPending } = localContentId ? localPreview : previewMutation;

    const providerType = target?.providerType;
    const externalIdKey = target?.externalIdKey;
    const externalId = target?.externalId;
    const contentType = target?.contentType;
    const levelSlug = target?.levelSlug;

    const openerRef = useRef<HTMLElement | null>(null);
    useLayoutEffect(() => {
        if (open) openerRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    }, [open]);

    useEffect(() => {
        if (open && !localContentId && providerType && externalIdKey && externalId && contentType) {
            runPreview({
                providerType,
                externalIdKey,
                externalId,
                contentType,
                ...(levelSlug ? { levelSlug } : {}),
            });
        } else if (!open) {
            resetPreview();
        }
    }, [
        open,
        localContentId,
        providerType,
        externalIdKey,
        externalId,
        contentType,
        levelSlug,
        runPreview,
        resetPreview,
    ]);

    const preview = data;
    const title = preview?.title?.trim() || target?.fallbackTitle || 'Preview';
    const year = preview?.year != null ? String(preview.year) : target?.fallbackYear;
    const hero = preview ? pickHero(preview) : undefined;
    const artwork = preview ? pickArtwork(preview) : undefined;
    const runtime = formatTimeSpanDuration(preview?.duration);
    const relatedSubjectGroups = groupRelatedSubjects(preview?.relatedSubjects ?? []);
    const hasDetails =
        !!hero ||
        !!artwork ||
        !!runtime ||
        !!preview?.language ||
        !!preview?.contentRatings.length ||
        !!preview?.overview ||
        !!preview?.genres.length ||
        relatedSubjectGroups.length > 0;
    const notFound = isNotFoundError(error);
    const openLabel = `Open ${title}`;
    const openLabelRef = useRef<HTMLSpanElement>(null);
    const [openTooltip, setOpenTooltip] = useState(false);
    // The tooltip shows the full label only when the ellipsis hides part of it.
    const changeOpenTooltip = (next: boolean) => {
        const label = openLabelRef.current;
        setOpenTooltip(next && !!label && label.scrollWidth > label.clientWidth);
    };

    return (
        <Sheet open={open} onOpenChange={onOpenChange}>
            <SheetContent
                side="right"
                className="preview-drawer"
                data-testid="preview-drawer"
                onCloseAutoFocus={onCloseAutoFocus ?? returnFocusOnClose(openerRef)}
                onClick={(event) => {
                    // Keep this click out of the source overlay's deferred outside-click handler.
                    if (localContentId) event.stopPropagation();
                }}
            >
                <SheetHeader className="preview-drawer__header">
                    <SheetTitle className="preview-drawer__title">
                        {title}
                        {year && <span className="preview-drawer__year">{year}</span>}
                    </SheetTitle>
                    {preview?.productionStatus ? (
                        <SheetDescription className="preview-drawer__status">
                            {preview.productionStatus}
                        </SheetDescription>
                    ) : (
                        <SheetDescription className="sr-only">Preview details for {title}.</SheetDescription>
                    )}
                </SheetHeader>

                <div className="preview-drawer__body">
                    {isPending && (
                        <div className="preview-drawer__loading" data-testid="preview-drawer-loading">
                            <Skeleton className="preview-drawer__skeleton preview-drawer__skeleton--hero" />
                            <Skeleton className="preview-drawer__skeleton" />
                            <Skeleton className="preview-drawer__skeleton" />
                        </div>
                    )}

                    {!isPending && notFound && (
                        <EmptyState
                            icon={<FilmStripIcon weight="duotone" />}
                            title={localContentId ? 'Item unavailable' : 'No metadata found'}
                            description={
                                localContentId
                                    ? 'This library item is no longer available.'
                                    : 'No metadata found for this item. It can still be added, then refreshed later.'
                            }
                            size="sm"
                            data-testid="preview-drawer-not-found"
                        />
                    )}

                    {!isPending && error && !notFound && (
                        <EmptyState
                            icon={<WarningCircleIcon weight="duotone" />}
                            title="Preview failed"
                            description={
                                localContentId
                                    ? 'Close the preview and try again.'
                                    : 'Try again or add the item without a preview.'
                            }
                            size="sm"
                            data-testid="preview-drawer-error"
                        />
                    )}

                    {!isPending && preview && !hasDetails && (
                        <EmptyState
                            icon={<InfoIcon weight="duotone" />}
                            title="No more details"
                            description="This item has no more details."
                            size="sm"
                            data-testid="preview-drawer-no-details"
                        />
                    )}

                    {!isPending && preview && hasDetails && (
                        <div className="preview-drawer__content">
                            {hero ? (
                                <PreviewPlayer preview={hero} posterUrl={artwork} />
                            ) : artwork ? (
                                <div
                                    className="preview-drawer__artwork-frame"
                                    style={
                                        {
                                            '--preview-artwork-image': `url(${JSON.stringify(artwork)})`,
                                        } as CSSProperties
                                    }
                                    data-testid="preview-drawer-artwork-frame"
                                >
                                    <ContentPoster
                                        className="preview-drawer__artwork"
                                        posterUrl={artwork}
                                        placeholderClassName="preview-drawer__artwork--placeholder"
                                        alt=""
                                        loading="lazy"
                                        data-testid="preview-drawer-artwork"
                                    />
                                </div>
                            ) : null}

                            <div className="preview-drawer__meta">
                                {runtime && (
                                    <Badge size="sm" variant="default">
                                        {runtime}
                                    </Badge>
                                )}
                                {preview.language && (
                                    <Badge size="sm" variant="default">
                                        {preview.language}
                                    </Badge>
                                )}
                                {preview.contentRatings.map((r) => (
                                    <Badge key={`${r.body}-${r.rating}`} size="sm" variant="info" badgeStyle="outline">
                                        {r.body}: {r.rating}
                                    </Badge>
                                ))}
                            </div>

                            {preview.overview && <p className="preview-drawer__overview">{preview.overview}</p>}

                            {preview.genres.length > 0 && (
                                <div className="preview-drawer__genres" data-testid="preview-drawer-genres">
                                    {preview.genres.map((genre) => (
                                        <Badge key={genre} size="sm" variant="default">
                                            {genre}
                                        </Badge>
                                    ))}
                                </div>
                            )}

                            {relatedSubjectGroups.length > 0 && (
                                <div data-testid="preview-drawer-related-subjects">
                                    {relatedSubjectGroups.map(({ kind, subjects }) => (
                                        <section
                                            className="preview-drawer__related-subject-group"
                                            data-testid="preview-drawer-subject-group"
                                            data-subject-kind={kind}
                                            key={kind}
                                        >
                                            <h3 className="preview-drawer__section-title">
                                                {subjectKindGroupLabel(kind, subjects.length, subjectTypes)}
                                            </h3>
                                            <ul className="preview-drawer__related-subject-list" role="list">
                                                {subjects.map((subject, index) => (
                                                    <li
                                                        className="preview-drawer__related-subject-item"
                                                        key={`${subject.name}-${subject.relationshipKind}-${index}`}
                                                    >
                                                        <Avatar
                                                            size="md"
                                                            src={subject.imageUrl ?? undefined}
                                                            alt={subject.name}
                                                            fallback={<UserIcon weight="bold" aria-hidden="true" />}
                                                        />
                                                        <span className="preview-drawer__related-subject-name">
                                                            {subject.name}
                                                        </span>
                                                        <span className="preview-drawer__relationship-qualifier">
                                                            {subject.relationshipQualifier || subject.relationshipKind}
                                                        </span>
                                                    </li>
                                                ))}
                                            </ul>
                                        </section>
                                    ))}
                                </div>
                            )}
                        </div>
                    )}
                </div>

                {localContentId && onOpenContent && (
                    <SheetFooter className="preview-drawer__footer">
                        <Tooltip content={openLabel} open={openTooltip} onOpenChange={changeOpenTooltip}>
                            <Button
                                variant="primary"
                                className="preview-drawer__open"
                                onClick={() => localPreview.data && onOpenContent(localPreview.data.localContentId)}
                                disabled={!preview || isPending || !!error}
                                data-action="open-content"
                            >
                                <span ref={openLabelRef} className="preview-drawer__open-label">
                                    {openLabel}
                                </span>
                            </Button>
                        </Tooltip>
                    </SheetFooter>
                )}
                {!localContentId && onAdd && (
                    <SheetFooter className="preview-drawer__footer">
                        {!isAdded && addDisabledReason && (
                            <p
                                className="preview-drawer__add-hint"
                                role="status"
                                data-testid="preview-drawer__add-hint"
                            >
                                {addDisabledReason}
                            </p>
                        )}
                        {!isAdded && addOptions && addOptions.length > 1 ? (
                            <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                    <Button
                                        variant="primary"
                                        loading={isAdding}
                                        disabled={!target || isAdding}
                                        data-action="preview-drawer-add"
                                    >
                                        <PlusIcon weight="bold" aria-hidden="true" />
                                        Add to library
                                        <CaretDownIcon weight="bold" aria-hidden="true" />
                                    </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end" sideOffset={4}>
                                    <DropdownMenuLabel>Add to root folder</DropdownMenuLabel>
                                    {addOptions.map((option) => (
                                        <DropdownMenuItem
                                            key={option.id}
                                            onSelect={() => target && onAdd(target, option.id)}
                                            data-action="preview-drawer-add-to-folder"
                                            data-folder-id={option.id}
                                        >
                                            {option.label}
                                        </DropdownMenuItem>
                                    ))}
                                </DropdownMenuContent>
                            </DropdownMenu>
                        ) : (
                            <Button
                                variant="primary"
                                onClick={() => target && onAdd(target, addOptions?.[0]?.id)}
                                loading={isAdding}
                                disabled={!target || isAdded || isAdding || (!isAdded && !!addDisabledReason)}
                                data-action="preview-drawer-add"
                            >
                                {isAdded ? (
                                    <>
                                        <CheckIcon weight="bold" aria-hidden="true" />
                                        Added
                                    </>
                                ) : (
                                    <>
                                        <PlusIcon weight="bold" aria-hidden="true" />
                                        Add to library
                                    </>
                                )}
                            </Button>
                        )}
                    </SheetFooter>
                )}
            </SheetContent>
        </Sheet>
    );
}

PreviewDrawer.displayName = 'PreviewDrawer';

export default PreviewDrawer;
