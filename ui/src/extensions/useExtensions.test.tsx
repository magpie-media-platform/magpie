import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExtensionProvider } from './ExtensionProvider';
import type { UIManifest } from './types';
import { useExtensions, useSlotExtensions } from './useExtensions';

let injectedElements: HTMLElement[] = [];
let originalAppendChild: typeof document.head.appendChild;

const mockFetch = vi.fn();

function mockJsonResponse(
    body: unknown,
    status = 200,
): {
    ok: boolean;
    status: number;
    headers: Headers;
    json: () => Promise<unknown>;
} {
    return {
        ok: status >= 200 && status < 300,
        status,
        headers: new Headers({ 'content-type': 'application/json' }),
        json: () => Promise.resolve(body),
    };
}

function mockErrorResponse(
    status: number,
    statusText: string,
): {
    ok: false;
    status: number;
    statusText: string;
    headers: Headers;
    text: () => Promise<string>;
} {
    return {
        ok: false,
        status,
        statusText,
        headers: new Headers({ 'content-type': 'text/plain' }),
        text: () => Promise.resolve(statusText),
    };
}

describe('useExtensions', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        injectedElements = [];

        vi.stubGlobal('fetch', mockFetch);

        originalAppendChild = document.head.appendChild.bind(document.head);
        document.head.appendChild = <T extends Node>(node: T): T => {
            if (node instanceof HTMLElement) {
                injectedElements.push(node);
            }
            return originalAppendChild(node);
        };
    });

    afterEach(() => {
        for (const el of injectedElements) {
            el.remove();
        }
        injectedElements = [];
        document.head.appendChild = originalAppendChild;
        vi.unstubAllGlobals();
    });

    function TestUseExtensions() {
        const { state, isLoading, isReady, isError, error, retry, apiBaseUrl, userId, username, role } =
            useExtensions();

        return (
            <div>
                <span data-testid="state">{state}</span>
                <span data-testid="is-loading">{String(isLoading)}</span>
                <span data-testid="is-ready">{String(isReady)}</span>
                <span data-testid="is-error">{String(isError)}</span>
                <span data-testid="error">{error ?? 'null'}</span>
                <span data-testid="api-base">{apiBaseUrl}</span>
                <span data-testid="user-id">{userId ?? 'null'}</span>
                <span data-testid="username">{username ?? 'null'}</span>
                <span data-testid="role">{role ?? 'null'}</span>
                <button data-testid="retry" onClick={() => retry()}>
                    Retry
                </button>
            </div>
        );
    }

    it('returns loading state initially', async () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockImplementationOnce(
            () =>
                new Promise((resolve) => {
                    setTimeout(() => {
                        resolve(mockJsonResponse(manifest));
                    }, 200);
                }),
        );

        render(
            <ExtensionProvider userId="user-123">
                <TestUseExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('loading');
        });

        expect(screen.getByTestId('is-loading')).toHaveTextContent('true');
        expect(screen.getByTestId('is-ready')).toHaveTextContent('false');
        expect(screen.getByTestId('is-error')).toHaveTextContent('false');
    });

    it('returns ready state after loading', async () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        render(
            <ExtensionProvider userId="user-123">
                <TestUseExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('ready');
        });

        expect(screen.getByTestId('is-loading')).toHaveTextContent('false');
        expect(screen.getByTestId('is-ready')).toHaveTextContent('true');
        expect(screen.getByTestId('is-error')).toHaveTextContent('false');
    });

    it('reads as loading while an automatic retry is still pending', async () => {
        mockFetch.mockResolvedValueOnce(mockErrorResponse(500, 'Internal Server Error'));

        render(
            <ExtensionProvider userId="user-123">
                <TestUseExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('error');
        });

        expect(screen.getByTestId('is-loading')).toHaveTextContent('true');
        expect(screen.getByTestId('is-ready')).toHaveTextContent('false');
        expect(screen.getByTestId('is-error')).toHaveTextContent('false');
        expect(screen.getByTestId('error').textContent).not.toBe('null');
    });

    it('exposes context values', async () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        render(
            <ExtensionProvider apiBaseUrl="/custom-api" userId="user-123" username="testuser" role="Manager">
                <TestUseExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('is-ready')).toHaveTextContent('true');
        });

        expect(screen.getByTestId('api-base')).toHaveTextContent('/custom-api');
        expect(screen.getByTestId('user-id')).toHaveTextContent('user-123');
        expect(screen.getByTestId('username')).toHaveTextContent('testuser');
        expect(screen.getByTestId('role')).toHaveTextContent('Manager');
    });

    it('provides getExtensionsForSlot function', async () => {
        const manifest: UIManifest = {
            assets: [],
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Dashboard Widget',
                    slot: 'dashboard.main',
                    modifier: 'After',
                    tagName: 'dashboard-widget',
                    priority: 100,
                    attributes: null,
                },
                {
                    id: 'ext-2',
                    extensionId: 'my-ext',
                    name: 'Content Widget',
                    slot: 'content.list',
                    modifier: 'Before',
                    tagName: 'content-widget',
                    priority: 50,
                    attributes: null,
                },
            ],
        };

        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        function TestGetExtensions() {
            const { isReady, getExtensionsForSlot } = useExtensions();
            const dashboardExts = getExtensionsForSlot('dashboard.main');
            const contentExts = getExtensionsForSlot('content.list');

            return (
                <div>
                    <span data-testid="is-ready">{String(isReady)}</span>
                    <span data-testid="dashboard-count">{dashboardExts.length}</span>
                    <span data-testid="content-count">{contentExts.length}</span>
                    {dashboardExts.map((ext) => (
                        <span key={ext.id} data-testid={`dashboard-${ext.id}`}>
                            {ext.name}
                        </span>
                    ))}
                </div>
            );
        }

        render(
            <ExtensionProvider userId="user-123">
                <TestGetExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('is-ready')).toHaveTextContent('true');
        });

        expect(screen.getByTestId('dashboard-count')).toHaveTextContent('1');
        expect(screen.getByTestId('content-count')).toHaveTextContent('1');
        expect(screen.getByTestId('dashboard-ext-1')).toHaveTextContent('Dashboard Widget');
    });

    it('fetches the manifest again and becomes ready after retry', async () => {
        mockFetch.mockResolvedValueOnce(mockErrorResponse(503, 'Service Unavailable'));

        render(
            <ExtensionProvider userId="user-123">
                <TestUseExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('error');
        });

        mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));
        fireEvent.click(screen.getByTestId('retry'));
        await waitFor(() => expect(screen.getByTestId('is-ready')).toHaveTextContent('true'));
        expect(mockFetch).toHaveBeenCalledTimes(2);
    });
});

describe('useSlotExtensions', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        injectedElements = [];

        vi.stubGlobal('fetch', mockFetch);

        originalAppendChild = document.head.appendChild.bind(document.head);
        document.head.appendChild = <T extends Node>(node: T): T => {
            if (node instanceof HTMLElement) {
                injectedElements.push(node);
            }
            return originalAppendChild(node);
        };
    });

    afterEach(() => {
        for (const el of injectedElements) {
            el.remove();
        }
        injectedElements = [];
        document.head.appendChild = originalAppendChild;
        vi.unstubAllGlobals();
    });

    it('returns extensions for specific slot', async () => {
        const manifest: UIManifest = {
            assets: [],
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Widget A',
                    slot: 'dashboard.widgets',
                    modifier: 'After',
                    tagName: 'widget-a',
                    priority: 200,
                    attributes: null,
                },
                {
                    id: 'ext-2',
                    extensionId: 'my-ext',
                    name: 'Widget B',
                    slot: 'dashboard.widgets',
                    modifier: 'Before',
                    tagName: 'widget-b',
                    priority: 50,
                    attributes: { 'data-size': 'large' },
                },
                {
                    id: 'ext-3',
                    extensionId: 'other-ext',
                    name: 'Other Widget',
                    slot: 'content.details.header',
                    modifier: 'After',
                    tagName: 'other-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
        };

        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        function TestSlotExtensions() {
            const { extensions, isReady, isLoading } = useSlotExtensions('dashboard.widgets');

            return (
                <div>
                    <span data-testid="is-loading">{String(isLoading)}</span>
                    <span data-testid="is-ready">{String(isReady)}</span>
                    <span data-testid="count">{extensions.length}</span>
                    {extensions.map((ext) => (
                        <span key={ext.id} data-testid={`ext-${ext.id}`}>
                            {ext.name}
                        </span>
                    ))}
                </div>
            );
        }

        render(
            <ExtensionProvider userId="user-123">
                <TestSlotExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('is-ready')).toHaveTextContent('true');
        });

        expect(screen.getByTestId('count')).toHaveTextContent('2');

        const extElements = screen.getAllByTestId(/^ext-ext-/);
        expect(extElements[0]).toHaveTextContent('Widget B');
        expect(extElements[1]).toHaveTextContent('Widget A');
    });

    it('returns empty array while loading', async () => {
        const manifest: UIManifest = {
            assets: [],
            extensions: [
                {
                    id: 'ext-1',
                    extensionId: 'my-ext',
                    name: 'Widget',
                    slot: 'test.slot',
                    modifier: 'After',
                    tagName: 'my-widget',
                    priority: 100,
                    attributes: null,
                },
            ],
        };

        mockFetch.mockImplementationOnce(
            () =>
                new Promise((resolve) => {
                    setTimeout(() => {
                        resolve(mockJsonResponse(manifest));
                    }, 50);
                }),
        );

        function TestSlotExtensions() {
            const { extensions, isLoading } = useSlotExtensions('test.slot');
            return (
                <div>
                    <span data-testid="is-loading">{String(isLoading)}</span>
                    <span data-testid="count">{extensions.length}</span>
                </div>
            );
        }

        render(
            <ExtensionProvider userId="user-123">
                <TestSlotExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('is-loading')).toHaveTextContent('true');
        });
        expect(screen.getByTestId('count')).toHaveTextContent('0');

        await waitFor(() => {
            expect(screen.getByTestId('count')).toHaveTextContent('1');
        });
    });

    it('returns empty array for unknown slot', async () => {
        const manifest: UIManifest = {
            assets: [],
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
        };

        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        function TestSlotExtensions() {
            const { extensions, isReady } = useSlotExtensions('unknown.slot');
            return (
                <div>
                    <span data-testid="is-ready">{String(isReady)}</span>
                    <span data-testid="count">{extensions.length}</span>
                </div>
            );
        }

        render(
            <ExtensionProvider userId="user-123">
                <TestSlotExtensions />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('is-ready')).toHaveTextContent('true');
        });

        expect(screen.getByTestId('count')).toHaveTextContent('0');
    });
});
