import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { Slot } from './Slot';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

describe('Slot', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
    });

    it('renders children when no extensions are registered', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });

        render(
            <Slot slot="dashboard.main">
                <div data-testid="default-content">Default Content</div>
            </Slot>,
        );

        expect(screen.getByTestId('default-content')).toHaveTextContent('Default Content');
    });

    it('renders children during loading state', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: false,
            isLoading: true,
        });

        render(
            <Slot slot="dashboard.main">
                <div data-testid="default-content">Default Content</div>
            </Slot>,
        );

        expect(screen.getByTestId('default-content')).toBeInTheDocument();
        expect(screen.getByTestId('default-content').closest('[data-slot]')).toHaveAttribute(
            'data-slot-state',
            'loading',
        );
    });

    it('sets data-slot attribute with slot path', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="content.details.header">
                <div>Content</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        expect(slotDiv).toHaveAttribute('data-slot', 'content.details.header');
    });

    it('renders Before extensions before default content', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Before Widget',
                    slot: 'dashboard.main',
                    modifier: 'Before',
                    tagName: 'before-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default-content">Default</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        const children = slotDiv?.children;

        expect(children).toHaveLength(2);
        expect(children?.[0].tagName.toLowerCase()).toBe('before-widget');
        expect(children?.[1]).toHaveAttribute('data-testid', 'default-content');
    });

    it('renders After extensions after default content', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'After Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'after-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default-content">Default</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        const children = slotDiv?.children;

        expect(children).toHaveLength(2);
        expect(children?.[0]).toHaveAttribute('data-testid', 'default-content');
        expect(children?.[1].tagName.toLowerCase()).toBe('after-widget');
    });

    it('Replace modifier replaces default content', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Replacement',
                    slot: 'dashboard.main',
                    modifier: 'Replace',
                    tagName: 'replacement-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default-content">Default</div>
            </Slot>,
        );

        expect(screen.queryByTestId('default-content')).not.toBeInTheDocument();

        const slotDiv = container.querySelector('[data-slot]');
        expect(slotDiv?.querySelector('replacement-widget')).toBeInTheDocument();
    });

    it('only first Replace extension wins (by priority) and warns about conflict', () => {
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'ext-a',
                    name: 'First Replace',
                    slot: 'dashboard.main',
                    modifier: 'Replace',
                    tagName: 'first-replace',
                    priority: 50,
                    attributes: null,
                },
                {
                    id: 'ext-2',
                    extensionId: 'ext-b',
                    name: 'Second Replace',
                    slot: 'dashboard.main',
                    modifier: 'Replace',
                    tagName: 'second-replace',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Default</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        expect(slotDiv?.querySelector('first-replace')).toBeInTheDocument();
        expect(slotDiv?.querySelector('second-replace')).not.toBeInTheDocument();

        expect(warnSpy).toHaveBeenCalledWith(
            expect.stringContaining('[Slot] Multiple Replace modifiers for slot "dashboard.main"'),
        );
        expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('"First Replace" (kept)'));
        expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('"Second Replace" (ignored)'));

        warnSpy.mockRestore();
    });

    it('combines Before, After, and Replace modifiers correctly', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'before-1',
                    extensionId: 'ext-a',
                    name: 'Before',
                    slot: 'dashboard.main',
                    modifier: 'Before',
                    tagName: 'before-widget',
                    priority: 100,
                    attributes: null,
                },
                {
                    id: 'replace-1',
                    extensionId: 'ext-b',
                    name: 'Replace',
                    slot: 'dashboard.main',
                    modifier: 'Replace',
                    tagName: 'replace-widget',
                    priority: 100,
                    attributes: null,
                },
                {
                    id: 'after-1',
                    extensionId: 'ext-c',
                    name: 'After',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'after-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default-content">Default</div>
            </Slot>,
        );

        expect(screen.queryByTestId('default-content')).not.toBeInTheDocument();

        const slotDiv = container.querySelector('[data-slot]');
        const children = slotDiv?.children;

        expect(children).toHaveLength(3);
        expect(children?.[0].tagName.toLowerCase()).toBe('before-widget');
        expect(children?.[1].tagName.toLowerCase()).toBe('replace-widget');
        expect(children?.[2].tagName.toLowerCase()).toBe('after-widget');
    });

    it('passes magpie context attributes to web components', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext-id',
                    name: 'Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-magpie-api-base', '/api');
        expect(widget).toHaveAttribute('data-magpie-user-id', 'user-123');
        expect(widget).toHaveAttribute('data-magpie-username', 'testuser');
        expect(widget).toHaveAttribute('data-magpie-role', 'Admin');
        expect(widget).toHaveAttribute('data-magpie-extension-id', 'my-ext-id');
        expect(widget).toHaveAttribute('data-magpie-extension-name', 'Widget');
    });

    it('passes custom attributes to web components', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: {
                        'data-custom-setting': 'value',
                        'data-feature-flag': 'enabled',
                    },
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-custom-setting', 'value');
        expect(widget).toHaveAttribute('data-feature-flag', 'enabled');
    });

    it('omits user context attributes when not authenticated', () => {
        mockUseExtensions.mockReturnValue({
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
        });

        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-magpie-api-base', '/api');
        expect(widget).not.toHaveAttribute('data-magpie-user-id');
        expect(widget).not.toHaveAttribute('data-magpie-username');
        expect(widget).not.toHaveAttribute('data-magpie-role');
    });

    it('applies custom className to slot container', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main" className="custom-class">
                <div>Content</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        expect(slotDiv).toHaveClass('slot', 'custom-class');
    });

    it('sets data-slot-state to ready when extensions are loaded', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Content</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        expect(slotDiv).toHaveAttribute('data-slot-state', 'ready');
    });

    it('renders multiple Before extensions in order', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'before-1',
                    extensionId: 'ext-a',
                    name: 'First Before',
                    slot: 'dashboard.main',
                    modifier: 'Before',
                    tagName: 'before-one',
                    priority: 50,
                    attributes: null,
                },
                {
                    id: 'before-2',
                    extensionId: 'ext-b',
                    name: 'Second Before',
                    slot: 'dashboard.main',
                    modifier: 'Before',
                    tagName: 'before-two',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default">Default</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        const children = slotDiv?.children;

        expect(children).toHaveLength(3);
        expect(children?.[0].tagName.toLowerCase()).toBe('before-one');
        expect(children?.[1].tagName.toLowerCase()).toBe('before-two');
        expect(children?.[2]).toHaveAttribute('data-testid', 'default');
    });

    it('renders multiple After extensions in order', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'after-1',
                    extensionId: 'ext-a',
                    name: 'First After',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'after-one',
                    priority: 50,
                    attributes: null,
                },
                {
                    id: 'after-2',
                    extensionId: 'ext-b',
                    name: 'Second After',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'after-two',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default">Default</div>
            </Slot>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        const children = slotDiv?.children;

        expect(children).toHaveLength(3);
        expect(children?.[0]).toHaveAttribute('data-testid', 'default');
        expect(children?.[1].tagName.toLowerCase()).toBe('after-one');
        expect(children?.[2].tagName.toLowerCase()).toBe('after-two');
    });

    it('catches extension crash with ErrorBoundary instead of unmounting slot', () => {
        vi.spyOn(console, 'error').mockImplementation(() => {});

        const crashyAttributes = new Proxy(
            {},
            {
                ownKeys() {
                    throw new Error('Extension crashed');
                },
            },
        );

        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'good-ext',
                    extensionId: 'ext-a',
                    name: 'Good Widget',
                    slot: 'dashboard.main',
                    modifier: 'Before',
                    tagName: 'good-widget',
                    priority: 50,
                    attributes: null,
                },
                {
                    id: 'bad-ext',
                    extensionId: 'ext-b',
                    name: 'Bad Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'bad-widget',
                    priority: 100,
                    attributes: crashyAttributes,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default-content">Default</div>
            </Slot>,
        );

        expect(container.querySelector('[data-slot="dashboard.main"]')).toBeInTheDocument();

        expect(container.querySelector('good-widget')).toBeInTheDocument();
        expect(screen.getByTestId('default-content')).toBeInTheDocument();

        expect(screen.getByRole('alert')).toBeInTheDocument();
        expect(screen.getByText('Failed to render page')).toBeInTheDocument();
    });

    it('passes entity context attributes to web components when provided', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Widget',
                    slot: 'content.details.header',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="content.details.header" entityId="42" entityType="Episode">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-magpie-entity-id', '42');
        expect(widget).toHaveAttribute('data-magpie-entity-type', 'Episode');
    });

    it('omits entity context attributes when not provided', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).not.toHaveAttribute('data-magpie-entity-id');
        expect(widget).not.toHaveAttribute('data-magpie-entity-type');
    });

    it('extension attributes cannot override core data-magpie-* attributes', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext-id',
                    name: 'Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: {
                        'data-magpie-api-base': 'https://evil.example.com/api',
                        'data-magpie-extension-id': 'spoofed-id',
                        'data-magpie-user-id': 'spoofed-user',
                        'data-custom-setting': 'value',
                    },
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-magpie-api-base', '/api');
        expect(widget).toHaveAttribute('data-magpie-extension-id', 'my-ext-id');
        expect(widget).toHaveAttribute('data-magpie-user-id', 'user-123');
        expect(widget).toHaveAttribute('data-custom-setting', 'value');
    });

    it('sets data-slot-extension attribute on extension components', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-unique-id',
                    extensionId: 'my-ext',
                    name: 'Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-slot-extension', 'ext-unique-id');
    });

    it('includes entity context in data-slot-extension when entity props are provided', () => {
        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'ext-id',
                    extensionId: 'my-ext',
                    name: 'Widget',
                    slot: 'content.details.header',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="content.details.header" entityId="42" entityType="Episode">
                <div>Default</div>
            </Slot>,
        );

        const widget = container.querySelector('my-widget');
        expect(widget).toHaveAttribute('data-slot-extension', 'ext-id:42:Episode');
    });

    it('renders both extensions when same id is registered twice with different configs', () => {
        const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

        mockUseSlotExtensions.mockReturnValue({
            extensions: [
                {
                    id: 'shared-id',
                    extensionId: 'my-ext',
                    name: 'Widget A',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget-a',
                    priority: 50,
                    attributes: { 'data-mode': 'view' },
                },
                {
                    id: 'shared-id',
                    extensionId: 'my-ext',
                    name: 'Widget B',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'my-widget-b',
                    priority: 100,
                    attributes: { 'data-mode': 'edit' },
                },
            ],
            isReady: true,
            isLoading: false,
        });

        const { container } = render(
            <Slot slot="dashboard.main">
                <div data-testid="default">Default</div>
            </Slot>,
        );

        expect(container.querySelector('my-widget-a')).toBeInTheDocument();
        expect(container.querySelector('my-widget-b')).toBeInTheDocument();
        expect(warnSpy).not.toHaveBeenCalled();

        warnSpy.mockRestore();
    });
});
