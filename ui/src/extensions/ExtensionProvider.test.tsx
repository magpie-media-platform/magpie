import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useContext } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ExtensionContext, ExtensionProvider } from './ExtensionProvider';
import type { UIManifest } from './types';

const mockFetch = vi.fn();
global.fetch = mockFetch;

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

let injectedElements: HTMLElement[] = [];
const originalAppendChild = document.head.appendChild.bind(document.head);
document.head.appendChild = <T extends Node>(node: T): T => {
    if (node instanceof HTMLElement) {
        injectedElements.push(node);
    }
    return originalAppendChild(node);
};

function TestConsumer() {
    const ctx = useContext(ExtensionContext);
    return (
        <div>
            <span data-testid="state">{ctx.state.state}</span>
            <span data-testid="api-base">{ctx.context.apiBaseUrl}</span>
            <span data-testid="user-id">{ctx.context.userId ?? 'null'}</span>
            <span data-testid="username">{ctx.context.username ?? 'null'}</span>
            <span data-testid="role">{ctx.context.role ?? 'null'}</span>
            <span data-testid="theme">{ctx.context.theme ?? 'null'}</span>
            <span data-testid="error">{ctx.state.error?.message ?? 'null'}</span>
        </div>
    );
}

describe('ExtensionProvider', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        injectedElements = [];
    });

    afterEach(() => {
        for (const el of injectedElements) {
            el.remove();
        }
        injectedElements = [];
    });

    it('provides default context values', () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        render(
            <ExtensionProvider>
                <TestConsumer />
            </ExtensionProvider>,
        );

        expect(screen.getByTestId('api-base')).toHaveTextContent('/api');
        expect(screen.getByTestId('user-id')).toHaveTextContent('null');
        expect(screen.getByTestId('username')).toHaveTextContent('null');
        expect(screen.getByTestId('role')).toHaveTextContent('null');
        expect(screen.getByTestId('theme')).toHaveTextContent('null');
    });

    it('provides custom context values', async () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        render(
            <ExtensionProvider apiBaseUrl="/custom-api" userId="user-123" username="testuser" role="Admin">
                <TestConsumer />
            </ExtensionProvider>,
        );

        expect(screen.getByTestId('api-base')).toHaveTextContent('/custom-api');
        expect(screen.getByTestId('user-id')).toHaveTextContent('user-123');
        expect(screen.getByTestId('username')).toHaveTextContent('testuser');
        expect(screen.getByTestId('role')).toHaveTextContent('Admin');
    });

    it('provides theme in context', () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        render(
            <ExtensionProvider theme="dark">
                <TestConsumer />
            </ExtensionProvider>,
        );

        expect(screen.getByTestId('theme')).toHaveTextContent('dark');
    });

    it('transitions through loading states', async () => {
        const manifest: UIManifest = { assets: [], extensions: [] };

        mockFetch.mockImplementationOnce(
            () =>
                new Promise((resolve) => {
                    setTimeout(() => {
                        resolve(mockJsonResponse(manifest));
                    }, 10);
                }),
        );

        render(
            <ExtensionProvider>
                <TestConsumer />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('loading');
        });

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('ready');
        });
    });

    it('handles fetch errors', async () => {
        mockFetch.mockResolvedValueOnce(mockErrorResponse(500, 'Internal Server Error'));

        render(
            <ExtensionProvider>
                <TestConsumer />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('error');
        });

        expect(screen.getByTestId('error').textContent).not.toBe('null');
    });

    it('provides getExtensionsForSlot function', async () => {
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

        function SlotConsumer() {
            const ctx = useContext(ExtensionContext);
            const extensions = ctx.getExtensionsForSlot('dashboard.main');
            return (
                <div>
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
            <ExtensionProvider>
                <SlotConsumer />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('count')).toHaveTextContent('1');
        });

        expect(screen.getByTestId('ext-ext-1')).toHaveTextContent('Widget');
    });

    it('retry resets loader and re-fetches after error', async () => {
        const user = userEvent.setup();

        mockFetch.mockResolvedValueOnce(mockErrorResponse(503, 'Service Unavailable'));

        function RetryConsumer() {
            const ctx = useContext(ExtensionContext);
            return (
                <div>
                    <span data-testid="state">{ctx.state.state}</span>
                    <button data-testid="retry" onClick={() => ctx.retry()}>
                        Retry
                    </button>
                </div>
            );
        }

        render(
            <ExtensionProvider>
                <RetryConsumer />
            </ExtensionProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('error');
        });

        mockFetch.mockResolvedValueOnce(mockJsonResponse({ assets: [], extensions: [] }));

        await user.click(screen.getByTestId('retry'));

        await waitFor(() => {
            expect(screen.getByTestId('state')).toHaveTextContent('ready');
        });

        expect(mockFetch).toHaveBeenCalledTimes(2);
    });

    it('renders children', () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockResolvedValueOnce(mockJsonResponse(manifest));

        render(
            <ExtensionProvider>
                <div data-testid="child">Hello</div>
            </ExtensionProvider>,
        );

        expect(screen.getByTestId('child')).toHaveTextContent('Hello');
    });

    it('updates context when props change', async () => {
        const manifest: UIManifest = { assets: [], extensions: [] };
        mockFetch.mockResolvedValue(mockJsonResponse(manifest));

        const { rerender } = render(
            <ExtensionProvider userId="user-1" username="alice">
                <TestConsumer />
            </ExtensionProvider>,
        );

        expect(screen.getByTestId('user-id')).toHaveTextContent('user-1');
        expect(screen.getByTestId('username')).toHaveTextContent('alice');

        rerender(
            <ExtensionProvider userId="user-2" username="bob">
                <TestConsumer />
            </ExtensionProvider>,
        );

        expect(screen.getByTestId('user-id')).toHaveTextContent('user-2');
        expect(screen.getByTestId('username')).toHaveTextContent('bob');
    });
});

describe('ExtensionContext default value', () => {
    it('provides default values when used outside provider', () => {
        render(<TestConsumer />);

        expect(screen.getByTestId('state')).toHaveTextContent('idle');
        expect(screen.getByTestId('api-base')).toHaveTextContent('/api');
    });
});
