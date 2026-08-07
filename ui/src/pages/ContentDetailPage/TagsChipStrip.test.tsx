import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { TagsChipStrip } from './TagsChipStrip';

const toast = vi.fn();
vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast }),
}));

const tagsByEntity = new Map<string, string[]>();

const apiGet = vi.fn(async (url: string) => {
    const match = url.match(/entityId=([^&]+)/);
    const entityId = match ? decodeURIComponent(match[1]) : '';
    const names = tagsByEntity.get(entityId) ?? [];
    return names.map((name) => ({ name }));
});

const apiPatch = vi.fn(async (path: string, body: unknown) => {
    const id = path.replace(/^\/content\//, '');
    const payload = body as { tags?: { add?: string[]; remove?: string[] } };
    const existing = tagsByEntity.get(id) ?? [];
    let next = [...existing];
    for (const add of payload.tags?.add ?? []) {
        if (!next.includes(add)) {
            next.push(add);
        }
    }
    for (const remove of payload.tags?.remove ?? []) {
        next = next.filter((name) => name !== remove);
    }
    tagsByEntity.set(id, next);
    return undefined;
});

vi.mock('@/lib/api', () => ({
    apiGet: (url: string) => apiGet(url),
    apiPost: vi.fn(),
    apiPut: vi.fn(),
    apiPatch: (path: string, body: unknown) => apiPatch(path, body),
    apiDelete: vi.fn(),
    buildUrl: (path: string, params?: Record<string, string | undefined>) => {
        if (!params) {
            return path;
        }
        const search = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                search.set(key, String(value));
            }
        }
        const qs = search.toString();
        return qs ? `${path}?${qs}` : path;
    },
}));

function renderStrip(contentId: string, canEdit = true) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <TagsChipStrip contentId={contentId} canEdit={canEdit} />
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('TagsChipStrip', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        tagsByEntity.clear();
    });

    it('shows a newly added tag chip after the add succeeds (mutation refetches the strip key)', async () => {
        const contentId = 'content-1';
        tagsByEntity.set(contentId, ['existing']);
        const user = userEvent.setup();

        renderStrip(contentId);

        await screen.findByTestId('tags-chip-strip-link-existing');

        await user.click(screen.getByTestId('tags-chip-strip-add'));
        const input = await screen.findByTestId('tags-chip-strip-input');
        await user.type(input, 'fresh-tag{Enter}');

        await waitFor(() => {
            expect(apiPatch).toHaveBeenCalledWith('/content/content-1', { tags: { add: ['fresh-tag'] } });
        });

        await waitFor(() => {
            expect(screen.getByTestId('tags-chip-strip-link-fresh-tag')).toBeInTheDocument();
        });
        expect(screen.getByTestId('tags-chip-strip-link-existing')).toBeInTheDocument();
    });

    it('reads tags for the supplied content id via the entity query', async () => {
        tagsByEntity.set('content-42', ['from-server']);

        renderStrip('content-42');

        await screen.findByTestId('tags-chip-strip-link-from-server');
        expect(apiGet).toHaveBeenCalledWith(expect.stringContaining('entityId=content-42'));
    });

    it('links tag chips to the all-content library list with a tag filter', async () => {
        tagsByEntity.set('content-42', ['Action Picks']);

        renderStrip('content-42');

        const tagLink = await screen.findByRole('link', { name: 'Action Picks' });
        expect(tagLink).toHaveAttribute('href', `/library/all?filter=${encodeURIComponent('tag has "Action Picks"')}`);
    });

    it('keeps the navigable chips but hides add and remove affordances when the viewer cannot edit', async () => {
        tagsByEntity.set('content-42', ['Action Picks']);

        renderStrip('content-42', false);

        await screen.findByTestId('tags-chip-strip-link-Action Picks');
        expect(screen.queryByTestId('tags-chip-strip-add')).not.toBeInTheDocument();
        expect(screen.queryByTestId('tags-chip-strip-remove-Action Picks')).not.toBeInTheDocument();
    });

    it('centers the add glyph on an integer icon box rather than a nudge transform', async () => {
        tagsByEntity.set('content-42', ['Action Picks']);

        renderStrip('content-42');

        const addButton = await screen.findByTestId('tags-chip-strip-add');
        expect(addButton.querySelectorAll('svg')).toHaveLength(1);
        expect(addButton.getAttribute('style')).toBeNull();

        const tokens = readFileSync(resolve(process.cwd(), 'src/styles/tokens.css'), 'utf8');
        const css = readFileSync(resolve(process.cwd(), 'src/components/ui/TagsChipStrip/TagsChipStrip.css'), 'utf8');
        const add = css.match(/\.tags-chip-strip__add\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups?.declarations;

        expect(tokens).toContain('--icon-inline-xs: 16px;');
        expect(add).toContain('align-items: center;');
        expect(add).toContain('justify-content: center;');
        expect(add).toContain('width: var(--space-xl);');
        expect(add).toContain('height: var(--space-xl);');
        expect(add).toContain('width: var(--icon-inline-xs);');
        expect(add).toContain('height: var(--icon-inline-xs);');
        expect(add).not.toContain('transform:');
    });

    it('declares in css one icon-sizing idiom for both the add and the remove glyph', () => {
        const css = readFileSync(resolve(process.cwd(), 'src/components/ui/TagsChipStrip/TagsChipStrip.css'), 'utf8');
        const remove = css.match(/\.tags-chip-strip__chip-remove\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups
            ?.declarations;

        expect(remove).toContain('width: var(--icon-inline-xs);');
        expect(remove).toContain('height: var(--icon-inline-xs);');
        expect(remove).not.toContain('font-size:');
    });

    it('renders nothing when the viewer cannot edit and there are no tags', async () => {
        const { container } = renderStrip('content-empty', false);

        await waitFor(() => {
            expect(screen.queryByTestId('tags-chip-strip')).not.toBeInTheDocument();
        });
        expect(container).toBeEmptyDOMElement();
    });
});
