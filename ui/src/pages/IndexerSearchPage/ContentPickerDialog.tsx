import { useMemo, useState } from 'react';
import { LinkSimpleIcon, DownloadSimpleIcon, MagnifyingGlassIcon } from '@phosphor-icons/react';

import { FilterBar } from '@/components/filters/FilterBar';
import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogBody,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { Input } from '@/components/ui/Input';
import { Skeleton } from '@/components/ui/Skeleton';
import { useContentList } from '@/hooks/useContent';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { magpieQlAnd, magpieQlString } from '@/lib/magpieQl';
import type { ContentListItem } from '@/types/content';

export type ContentPickerMode = 'match' | 'grab';

interface ContentPickerDialogProps {
    open: boolean;

    mode: ContentPickerMode;

    isSubmitting: boolean;

    onOpenChange: (open: boolean) => void;

    onSelect: (item: ContentListItem) => void;
}

export function ContentPickerDialog({ open, mode, isSubmitting, onOpenChange, onSelect }: ContentPickerDialogProps) {
    const [text, setText] = useState('');
    const [filter, setFilter] = useState('');
    const debouncedText = useDebouncedValue(text, 300);

    const composedFilter = useMemo(() => {
        const clauses: string[] = [];
        if (debouncedText.trim()) {
            clauses.push(`title contains ${magpieQlString(debouncedText.trim())}`);
        }
        if (filter.trim()) {
            clauses.push(`(${filter.trim()})`);
        }
        return magpieQlAnd(clauses);
    }, [debouncedText, filter]);

    const { data, isLoading } = useContentList(
        { filter: composedFilter || undefined, pageSize: 25 },
        { enabled: open },
    );
    const items = data?.items ?? [];
    const actionLabel = mode === 'grab' ? 'Grab' : 'Match';

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="content-picker" data-component="content-picker">
                <DialogHeader>
                    <DialogTitle>{mode === 'grab' ? 'Select content to grab into' : 'Select content'}</DialogTitle>
                    <DialogDescription>Choose the library item to associate this release with.</DialogDescription>
                </DialogHeader>
                <DialogBody className="content-picker__body">
                    <Input
                        value={text}
                        onChange={(event) => setText(event.target.value)}
                        placeholder="Search library"
                        aria-label="Search library"
                        leadingIcon={<MagnifyingGlassIcon weight="bold" aria-hidden="true" />}
                        data-action="content-picker-search"
                    />

                    <FilterBar value={filter} onChange={setFilter} context="content" collapsible />

                    {isLoading ? (
                        <div className="content-picker__loading">
                            <Skeleton />
                            <Skeleton />
                            <Skeleton />
                        </div>
                    ) : items.length === 0 ? (
                        <EmptyState
                            icon={<MagnifyingGlassIcon weight="thin" aria-hidden="true" />}
                            title="No library content found"
                            description="Change the search text or the filters to find the item to associate."
                            size="sm"
                        />
                    ) : (
                        <ul className="content-picker__list">
                            {items.map((item) => (
                                <li key={item.id} className="content-picker__option">
                                    {item.posterUrl ? (
                                        <img className="content-picker__poster" src={item.posterUrl} alt="" />
                                    ) : (
                                        <span className="content-picker__poster" aria-hidden="true" />
                                    )}
                                    <span className="content-picker__meta">
                                        <span className="content-picker__title">{item.title}</span>
                                        <span className="content-picker__sub">
                                            {item.levelLabel}
                                            {item.year ? ` · ${item.year}` : ''}
                                        </span>
                                    </span>
                                    <Button
                                        className="content-picker__action"
                                        variant="secondary"
                                        size="sm"
                                        disabled={isSubmitting}
                                        onClick={() => onSelect(item)}
                                        data-action="content-picker-select"
                                        data-content-id={item.id}
                                    >
                                        {mode === 'grab' ? (
                                            <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                                        ) : (
                                            <LinkSimpleIcon weight="bold" aria-hidden="true" />
                                        )}
                                        {actionLabel}
                                    </Button>
                                </li>
                            ))}
                        </ul>
                    )}
                </DialogBody>
                <DialogFooter>
                    <Button variant="ghost" onClick={() => onOpenChange(false)}>
                        Cancel
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

ContentPickerDialog.displayName = 'ContentPickerDialog';

export default ContentPickerDialog;
