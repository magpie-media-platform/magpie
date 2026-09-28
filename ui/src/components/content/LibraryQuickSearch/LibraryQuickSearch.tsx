import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { ArrowRightIcon, GlobeIcon, ListBulletsIcon, MagnifyingGlassIcon, XIcon } from '@phosphor-icons/react';

import { DisabledTooltip, useAuth } from '@/auth';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { Dialog, DialogClose, DialogContent, DialogTitle, DialogTrigger } from '@/components/ui/Dialog';
import { useContentList } from '@/hooks/useContent';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { setContentFilter } from '@/lib/contentColumnFilters';
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { SERVER_SORT_FIELD } from '@/pages/LibraryPage/libraryPageConstants';
import type { ContentListItem } from '@/types';
import type { LibraryAddPreloadState } from '@/types/navigation';

import { LocalContentPreviewProvider, useOpenLocalContentPreview } from '../LocalContentPreview';

import './LibraryQuickSearch.css';

const SEARCH_DELAY_MS = 250;
const RESULT_LIMIT = 8;

export function LibraryQuickSearch() {
    const [open, setOpen] = useState(false);
    const { key } = useLocation();

    useEffect(() => {
        setOpen(false);
    }, [key]);

    return (
        <LocalContentPreviewProvider>
            <Dialog open={open} onOpenChange={setOpen}>
                <DialogTrigger asChild>
                    <Button
                        variant="ghost"
                        className="library-quick-search__trigger"
                        aria-label="Search library"
                        data-action="open-library-search"
                    >
                        <MagnifyingGlassIcon size={20} weight="bold" aria-hidden="true" />
                        <span className="library-quick-search__trigger-label" aria-hidden="true">
                            Search library
                        </span>
                    </Button>
                </DialogTrigger>
                <DialogContent className="library-quick-search" aria-describedby={undefined} hideCloseButton>
                    <LibraryQuickSearchPanel />
                </DialogContent>
            </Dialog>
        </LocalContentPreviewProvider>
    );
}

function LibraryQuickSearchPanel() {
    const [query, setQuery] = useState('');
    const [activeIndex, setActiveIndex] = useState(0);
    const title = query.trim();
    const debouncedTitle = useDebouncedValue(title, SEARCH_DELAY_MS);
    const search = useContentList(
        {
            filter: setContentFilter('', 'title', debouncedTitle),
            pageSize: RESULT_LIMIT,
            sortBy: SERVER_SORT_FIELD.title,
            sortDirection: 'Ascending',
        },
        { enabled: debouncedTitle !== '' },
    );
    const openPreview = useOpenLocalContentPreview();
    const navigate = useNavigate();
    const { hasPermission } = useAuth();
    const { contentTypes } = useLibraryTypes();
    const typeIconName = useTypeIconName();
    const listRef = useRef<HTMLDivElement>(null);
    const listboxId = useId();

    const settled = title !== '' && title === debouncedTitle;
    const matches = settled && search.isSuccess ? search.data.items : [];
    const activeMatch = matches[Math.min(activeIndex, matches.length - 1)];
    const titleFilter = setContentFilter('', 'title', title);
    const canSearchProviders = hasPermission('content.add');

    useEffect(() => {
        setActiveIndex(0);
    }, [debouncedTitle]);

    useEffect(() => {
        listRef.current?.querySelector('[data-active="true"]')?.scrollIntoView({ block: 'nearest' });
    }, [activeIndex]);

    const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (matches.length === 0) {
            return;
        }
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            setActiveIndex((index) => (index + 1) % matches.length);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            setActiveIndex((index) => (index - 1 + matches.length) % matches.length);
        } else if (event.key === 'Enter' && activeMatch) {
            event.preventDefault();
            openPreview(activeMatch.id, event.currentTarget);
        } else if (event.key === 'Escape') {
            setQuery('');
        }
    };

    const describeMatch = (match: ContentListItem) => {
        const level = resolveContentTypeLabel(match, contentTypes);
        return [match.parentId && match.ordinal != null ? `${level} ${match.ordinal}` : level, match.year]
            .filter(Boolean)
            .join(' · ');
    };

    let body;
    if (title === '') {
        body = <p className="library-quick-search__state">Type a title to search the library.</p>;
    } else if (!settled || search.isPending) {
        body = (
            <p className="library-quick-search__state" role="status">
                Searching the library…
            </p>
        );
    } else if (search.isError) {
        body = (
            <Alert variant="error" title="The library search failed" className="library-quick-search__error">
                <Button variant="secondary" size="sm" onClick={() => void search.refetch()}>
                    Try again
                </Button>
            </Alert>
        );
    } else if (matches.length === 0) {
        body = (
            <p className="library-quick-search__state" role="status">
                No library titles match &ldquo;{title}&rdquo;.
            </p>
        );
    } else {
        body = (
            <div
                ref={listRef}
                id={listboxId}
                role="listbox"
                aria-label="Library matches"
                className="library-quick-search__results"
            >
                {matches.map((match, index) => (
                    <button
                        key={match.id}
                        id={`${listboxId}-${index}`}
                        type="button"
                        role="option"
                        className="library-quick-search__result"
                        aria-selected={match === activeMatch}
                        data-active={match === activeMatch}
                        onMouseEnter={() => setActiveIndex(index)}
                        onClick={(event) => openPreview(match.id, event.currentTarget)}
                    >
                        <span className="library-quick-search__result-icon" aria-hidden="true">
                            <CdnPhosphorIcon name={typeIconName(match.contentType)} weight="duotone" />
                        </span>
                        <span className="library-quick-search__result-text">
                            <span className="library-quick-search__result-title">{match.title}</span>
                            <span className="library-quick-search__result-details">{describeMatch(match)}</span>
                        </span>
                        <ArrowRightIcon size={16} aria-hidden="true" />
                    </button>
                ))}
            </div>
        );
    }

    return (
        <>
            <div className="library-quick-search__top">
                <DialogTitle className="library-quick-search__title">Search library</DialogTitle>
                <div className="library-quick-search__search">
                    <MagnifyingGlassIcon size={18} aria-hidden="true" />
                    <input
                        className="library-quick-search__input"
                        type="search"
                        role="combobox"
                        aria-label="Library title"
                        aria-autocomplete="list"
                        aria-controls={listboxId}
                        aria-expanded={matches.length > 0}
                        aria-activedescendant={activeMatch ? `${listboxId}-${matches.indexOf(activeMatch)}` : undefined}
                        placeholder="Search titles"
                        autoComplete="off"
                        spellCheck={false}
                        value={query}
                        onChange={(event) => setQuery(event.target.value)}
                        onKeyDown={handleKeyDown}
                    />
                </div>
                <DialogClose className="overlay-close" aria-label="Close">
                    <XIcon size={20} weight="bold" aria-hidden="true" />
                </DialogClose>
            </div>
            <p className="library-quick-search__scope">Searches titles at every level of the library.</p>
            {body}
            {title !== '' && (
                <div className="library-quick-search__actions">
                    <Link
                        className="library-quick-search__action"
                        to={`/library/all?${new URLSearchParams({ filter: titleFilter, levels: 'all' }).toString()}`}
                        data-action="view-all-library-matches"
                    >
                        <ListBulletsIcon size={18} aria-hidden="true" />
                        All library results
                    </Link>
                    <DisabledTooltip action="content.add">
                        <Button
                            variant="ghost"
                            className="library-quick-search__action"
                            disabled={!canSearchProviders}
                            data-action="search-providers"
                            onClick={() => {
                                const state: LibraryAddPreloadState = { filterQuery: titleFilter };
                                void navigate('/library/add', { state });
                            }}
                        >
                            <GlobeIcon size={18} aria-hidden="true" />
                            Search providers
                        </Button>
                    </DisabledTooltip>
                </div>
            )}
            <div className="library-quick-search__footer" aria-hidden="true">
                <span>↑↓ Select · ↵ Open · Esc Close</span>
                <span>Providers are searched only on request</span>
            </div>
        </>
    );
}
