import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import {
    MagnifyingGlass,
    House,
    Books,
    DownloadSimple,
    CalendarBlank,
    Funnel,
    Star,
    ClockCountdown,
    Gear,
    Users,
    Rss,
    Television,
    HardDrive,
    Flask,
    Tag,
    PuzzlePiece,
    Database,
    Info,
} from '@phosphor-icons/react';

import { Role, useAuth } from '@/auth';
import './CommandPalette.css';

interface Command {
    id: string;
    label: string;
    icon: ReactNode;
    to: string;
    group: string;
    minRole?: Role;
}

const ALL_COMMANDS: Command[] = [
    { id: 'dashboard', label: 'Dashboard', icon: <House weight="duotone" />, to: '/', group: 'Navigation' },
    { id: 'library', label: 'Library', icon: <Books weight="duotone" />, to: '/library', group: 'Navigation' },
    {
        id: 'downloads',
        label: 'Downloads',
        icon: <DownloadSimple weight="duotone" />,
        to: '/downloads',
        group: 'Navigation',
    },
    {
        id: 'calendar',
        label: 'Calendar',
        icon: <CalendarBlank weight="duotone" />,
        to: '/calendar',
        group: 'Navigation',
    },
    {
        id: 'filters',
        label: 'Saved Filters',
        icon: <Funnel weight="duotone" />,
        to: '/filters',
        group: 'Navigation',
    },
    { id: 'wishlists', label: 'Wishlists', icon: <Star weight="duotone" />, to: '/wishlists', group: 'Navigation' },
    {
        id: 'my-jobs',
        label: 'My Jobs',
        icon: <ClockCountdown weight="duotone" />,
        to: '/my-jobs',
        group: 'Navigation',
    },
    { id: 'about', label: 'About', icon: <Info weight="duotone" />, to: '/about', group: 'Navigation' },
    {
        id: 'admin-general',
        label: 'General Settings',
        icon: <Gear weight="duotone" />,
        to: '/admin/general',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-users',
        label: 'Users',
        icon: <Users weight="duotone" />,
        to: '/admin/users',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-indexers',
        label: 'Indexers',
        icon: <Rss weight="duotone" />,
        to: '/admin/indexers',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-download-clients',
        label: 'Download Clients',
        icon: <DownloadSimple weight="duotone" />,
        to: '/admin/download-clients',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-media-servers',
        label: 'Media Servers',
        icon: <Television weight="duotone" />,
        to: '/admin/media-servers',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-root-folders',
        label: 'Root Folders',
        icon: <HardDrive weight="duotone" />,
        to: '/admin/root-folders',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-quality-tiers',
        label: 'Quality Tiers',
        icon: <Flask weight="duotone" />,
        to: '/admin/quality-tiers',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-tags',
        label: 'Tags',
        icon: <Tag weight="duotone" />,
        to: '/admin/tags',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-jobs',
        label: 'Jobs',
        icon: <ClockCountdown weight="duotone" />,
        to: '/admin/jobs',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-extensions',
        label: 'Extensions',
        icon: <PuzzlePiece weight="duotone" />,
        to: '/admin/extensions',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-backups',
        label: 'Backups',
        icon: <Database weight="duotone" />,
        to: '/admin/backups',
        group: 'Admin',
        minRole: Role.Admin,
    },
    {
        id: 'admin-system',
        label: 'System Status',
        icon: <Gear weight="duotone" />,
        to: '/admin/system',
        group: 'Admin',
        minRole: Role.Admin,
    },
];

function filterCommands(commands: Command[], query: string, userRole: Role): Command[] {
    return commands.filter((cmd) => {
        if (cmd.minRole !== undefined && userRole < cmd.minRole) {
            return false;
        }
        if (query.trim() === '') {
            return true;
        }
        return cmd.label.toLowerCase().includes(query.toLowerCase());
    });
}

function groupCommands(commands: Command[]): Record<string, Command[]> {
    return commands.reduce<Record<string, Command[]>>((acc, cmd) => {
        const list = acc[cmd.group] ?? [];
        list.push(cmd);
        acc[cmd.group] = list;
        return acc;
    }, {});
}

export function CommandPalette() {
    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState('');
    const [activeIndex, setActiveIndex] = useState(0);
    const navigate = useNavigate();
    const { user } = useAuth();
    const inputRef = useRef<HTMLInputElement>(null);
    const listRef = useRef<HTMLDivElement>(null);

    const userRole = user?.role ?? Role.Viewer;
    const filtered = filterCommands(ALL_COMMANDS, query, userRole);
    const groups = groupCommands(filtered);

    const handleSelect = useCallback(
        (cmd: Command) => {
            setOpen(false);
            navigate(cmd.to);
        },
        [navigate],
    );

    const handleKeyDown = useCallback(
        (e: KeyboardEvent<HTMLDivElement>) => {
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                setActiveIndex((i) => (i + 1) % Math.max(filtered.length, 1));
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setActiveIndex((i) => (i - 1 + Math.max(filtered.length, 1)) % Math.max(filtered.length, 1));
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const cmd = filtered[activeIndex];
                if (cmd) {
                    handleSelect(cmd);
                }
            }
        },
        [filtered, activeIndex, handleSelect],
    );

    useEffect(() => {
        const handleGlobalKeyDown = (e: globalThis.KeyboardEvent) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
                e.preventDefault();
                setOpen((prev) => !prev);
            }
        };
        window.addEventListener('keydown', handleGlobalKeyDown);
        return () => window.removeEventListener('keydown', handleGlobalKeyDown);
    }, []);

    useEffect(() => {
        if (open) {
            setQuery('');
            setActiveIndex(0);
        }
    }, [open]);

    useEffect(() => {
        setActiveIndex(0);
    }, [query]);

    useEffect(() => {
        const list = listRef.current;
        if (!list) {
            return;
        }
        const active = list.querySelector<HTMLElement>('[data-active="true"]');
        if (active) {
            active.scrollIntoView({ block: 'nearest' });
        }
    }, [activeIndex]);

    return (
        <DialogPrimitive.Root open={open} onOpenChange={setOpen}>
            <DialogPrimitive.Portal>
                <DialogPrimitive.Overlay className="command-palette-overlay" />
                <DialogPrimitive.Content
                    className="command-palette"
                    aria-label="Command palette"
                    onKeyDown={handleKeyDown}
                    onOpenAutoFocus={(e) => {
                        e.preventDefault();
                        inputRef.current?.focus();
                    }}
                >
                    <div className="command-palette__search">
                        <MagnifyingGlass className="command-palette__search-icon" size={18} aria-hidden="true" />
                        <input
                            ref={inputRef}
                            className="command-palette__input"
                            type="text"
                            placeholder="Search pages and settings..."
                            value={query}
                            onChange={(e) => setQuery(e.target.value)}
                            role="combobox"
                            aria-label="Search commands"
                            aria-expanded={filtered.length > 0}
                            aria-controls="command-palette-listbox"
                            aria-activedescendant={
                                filtered[activeIndex] ? `command-palette-option-${filtered[activeIndex].id}` : undefined
                            }
                            autoComplete="off"
                            spellCheck={false}
                        />
                        <kbd className="command-palette__esc-hint" aria-hidden="true">
                            Esc
                        </kbd>
                    </div>

                    <div ref={listRef} className="command-palette__results" id="command-palette-listbox" role="listbox">
                        {filtered.length === 0 ? (
                            <div className="command-palette__empty">No results for &ldquo;{query}&rdquo;</div>
                        ) : (
                            Object.entries(groups).map(([groupName, cmds]) => (
                                <div key={groupName} className="command-palette__group">
                                    <div className="command-palette__group-label" aria-hidden="true">
                                        {groupName}
                                    </div>
                                    {cmds.map((cmd) => {
                                        const index = filtered.indexOf(cmd);
                                        const isActive = index === activeIndex;
                                        return (
                                            <button
                                                key={cmd.id}
                                                id={`command-palette-option-${cmd.id}`}
                                                className="command-palette__item"
                                                role="option"
                                                data-active={isActive}
                                                aria-selected={isActive}
                                                onMouseEnter={() => setActiveIndex(index)}
                                                onClick={() => handleSelect(cmd)}
                                                type="button"
                                            >
                                                <span className="command-palette__item-icon" aria-hidden="true">
                                                    {cmd.icon}
                                                </span>
                                                <span className="command-palette__item-label">{cmd.label}</span>
                                            </button>
                                        );
                                    })}
                                </div>
                            ))
                        )}
                    </div>

                    <div className="command-palette__footer" aria-hidden="true">
                        <span>
                            <kbd>↑</kbd>
                            <kbd>↓</kbd> navigate
                        </span>
                        <span>
                            <kbd>↵</kbd> select
                        </span>
                        <span>
                            <kbd>Esc</kbd> close
                        </span>
                    </div>
                </DialogPrimitive.Content>
            </DialogPrimitive.Portal>
        </DialogPrimitive.Root>
    );
}

export default CommandPalette;
