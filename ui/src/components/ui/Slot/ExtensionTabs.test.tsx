import { render } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { ExtensionTabContents, useTabSlotExtensions } from './ExtensionTabs';

import type { UIExtension } from '@/extensions/types';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

import { Tabs, TabsList } from '@/components/ui/Tabs';

function renderInTabs(triggers: React.ReactNode, contents: React.ReactNode, defaultTab = 'overview') {
    return render(
        <Tabs value={defaultTab}>
            <TabsList>{triggers}</TabsList>
            {contents}
        </Tabs>,
    );
}

const defaultContext = {
    apiBaseUrl: '/api',
    userId: 'user-1',
    username: 'testuser',
    role: 'Admin',
};

function makeExtension(overrides: Partial<UIExtension> = {}): UIExtension {
    return {
        id: 'ext-1',
        extensionId: 'my-ext',
        name: 'Reviews',
        slot: 'content.details.tabs',
        modifier: 'After',
        tagName: 'my-reviews-panel',
        priority: 100,
        attributes: { 'tab-label': 'Reviews' },
        ...overrides,
    };
}

describe('ExtensionTabContents', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
    });

    it('renders nothing when no extensions are registered', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });

        const { container } = renderInTabs(null, <ExtensionTabContents slot="content.details.tabs" />);

        const panels = container.querySelectorAll('[role="tabpanel"]');
        expect(panels).toHaveLength(0);
    });

    it('renders a TabsContent with web component for each After extension', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [makeExtension({ id: 'ext-1', tagName: 'my-reviews-panel' })],
            isReady: true,
            isLoading: false,
        });

        const { container } = renderInTabs(null, <ExtensionTabContents slot="content.details.tabs" />, 'ext-ext-1');

        const webComponent = container.querySelector('my-reviews-panel');
        expect(webComponent).toBeInTheDocument();
    });

    it('passes magpie context attributes to web components', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [makeExtension({ id: 'ext-1', tagName: 'my-reviews-panel' })],
            isReady: true,
            isLoading: false,
        });

        const { container } = renderInTabs(null, <ExtensionTabContents slot="content.details.tabs" />, 'ext-ext-1');

        const widget = container.querySelector('my-reviews-panel');
        expect(widget).toHaveAttribute('data-magpie-api-base', '/api');
        expect(widget).toHaveAttribute('data-magpie-user-id', 'user-1');
        expect(widget).toHaveAttribute('data-magpie-username', 'testuser');
        expect(widget).toHaveAttribute('data-magpie-role', 'Admin');
        expect(widget).toHaveAttribute('data-magpie-extension-id', 'my-ext');
        expect(widget).toHaveAttribute('data-magpie-extension-name', 'Reviews');
    });

    it('passes custom attributes from extension to web component', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                makeExtension({
                    id: 'ext-1',
                    tagName: 'my-widget',
                    attributes: { 'tab-label': 'Reviews', 'data-theme': 'dark' },
                }),
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = renderInTabs(null, <ExtensionTabContents slot="content.details.tabs" />, 'ext-ext-1');

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-theme', 'dark');
        expect(widget).toHaveAttribute('tab-label', 'Reviews');
    });

    it('omits user context when not authenticated', () => {
        mockUseExtensions.mockReturnValue({
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
        });

        mockUseSlotExtensions.mockReturnValue({
            extensions: [makeExtension({ id: 'ext-1', tagName: 'my-widget' })],
            isReady: true,
            isLoading: false,
        });

        const { container } = renderInTabs(null, <ExtensionTabContents slot="content.details.tabs" />, 'ext-ext-1');

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-magpie-api-base', '/api');
        expect(widget).not.toHaveAttribute('data-magpie-user-id');
        expect(widget).not.toHaveAttribute('data-magpie-username');
        expect(widget).not.toHaveAttribute('data-magpie-role');
    });

    it('sets data-slot-extension attribute on web components', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [makeExtension({ id: 'ext-unique-id', tagName: 'my-widget' })],
            isReady: true,
            isLoading: false,
        });

        const { container } = renderInTabs(
            null,
            <ExtensionTabContents slot="content.details.tabs" />,
            'ext-ext-unique-id',
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-slot-extension', 'ext-unique-id');
    });
});

describe('useTabSlotExtensions', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
    });

    it('warns in dev mode when slot does not end with .tabs', () => {
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });

        function TestComponent() {
            useTabSlotExtensions('content.details.header');
            return null;
        }

        render(<TestComponent />);

        expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('content.details.header'));
        expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('.tabs'));
        warnSpy.mockRestore();
    });

    it('does not warn when slot ends with .tabs', () => {
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });

        function TestComponent() {
            useTabSlotExtensions('content.details.tabs');
            return null;
        }

        render(<TestComponent />);

        expect(warnSpy).not.toHaveBeenCalled();
        warnSpy.mockRestore();
    });

    it('filters to only After modifier extensions', () => {
        const afterExt = makeExtension({ id: 'ext-1', modifier: 'After' });
        const beforeExt = makeExtension({ id: 'ext-2', modifier: 'Before' });
        const replaceExt = makeExtension({ id: 'ext-3', modifier: 'Replace' });

        mockUseSlotExtensions.mockReturnValue({
            extensions: [afterExt, beforeExt, replaceExt],
            isReady: true,
            isLoading: false,
        });

        let hookResult: ReturnType<typeof useTabSlotExtensions> | null = null;
        function TestComponent() {
            hookResult = useTabSlotExtensions('content.details.tabs');
            return null;
        }

        render(<TestComponent />);

        expect(hookResult!.tabs).toHaveLength(1);
        expect(hookResult!.tabs[0].id).toBe('ext-1');
    });

    it('returns empty tabs when not ready', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: false,
            isLoading: true,
        });

        let hookResult: ReturnType<typeof useTabSlotExtensions> | null = null;
        function TestComponent() {
            hookResult = useTabSlotExtensions('content.details.tabs');
            return null;
        }

        render(<TestComponent />);

        expect(hookResult!.tabs).toHaveLength(0);
        expect(hookResult!.isLoading).toBe(true);
    });
});
