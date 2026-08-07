import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, type MockInstance, vi } from 'vitest';

import { UnsavedChangesBlocker, UnsavedChangesGuardProvider, useUnsavedChangesGuard } from './useUnsavedChangesGuard';

describe('useUnsavedChangesGuard', () => {
    let confirmSpy: MockInstance<typeof window.confirm>;

    beforeEach(() => {
        confirmSpy = vi.spyOn(window, 'confirm');
    });

    afterEach(() => {
        confirmSpy.mockRestore();
    });

    describe('beforeunload listener', () => {
        it('registers beforeunload when active', () => {
            const addSpy = vi.spyOn(window, 'addEventListener');

            renderHook(() => useUnsavedChangesGuard(true), { wrapper: UnsavedChangesGuardProvider });

            const call = addSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeDefined();

            addSpy.mockRestore();
        });

        it('does not register beforeunload when inactive', () => {
            const addSpy = vi.spyOn(window, 'addEventListener');

            renderHook(() => useUnsavedChangesGuard(false), { wrapper: UnsavedChangesGuardProvider });

            const call = addSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeUndefined();

            addSpy.mockRestore();
        });

        it('removes beforeunload on unmount', () => {
            const removeSpy = vi.spyOn(window, 'removeEventListener');

            const { unmount } = renderHook(() => useUnsavedChangesGuard(true), {
                wrapper: UnsavedChangesGuardProvider,
            });
            unmount();

            const call = removeSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeDefined();

            removeSpy.mockRestore();
        });

        it('removes beforeunload when active flips to false', () => {
            const removeSpy = vi.spyOn(window, 'removeEventListener');

            const { rerender } = renderHook(({ active }) => useUnsavedChangesGuard(active), {
                wrapper: UnsavedChangesGuardProvider,
                initialProps: { active: true },
            });
            rerender({ active: false });

            const call = removeSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeDefined();

            removeSpy.mockRestore();
        });

        it('calls preventDefault on beforeunload events', () => {
            renderHook(() => useUnsavedChangesGuard(true), { wrapper: UnsavedChangesGuardProvider });

            const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
            const preventSpy = vi.spyOn(event, 'preventDefault');

            window.dispatchEvent(event);

            expect(preventSpy).toHaveBeenCalled();
        });
    });

    describe('window.history is never patched', () => {
        it('leaves pushState and replaceState untouched while active', () => {
            const originalPushState = window.history.pushState;
            const originalReplaceState = window.history.replaceState;

            const { unmount } = renderHook(() => useUnsavedChangesGuard(true), {
                wrapper: UnsavedChangesGuardProvider,
            });

            expect(window.history.pushState).toBe(originalPushState);
            expect(window.history.replaceState).toBe(originalReplaceState);

            unmount();

            expect(window.history.pushState).toBe(originalPushState);
            expect(window.history.replaceState).toBe(originalReplaceState);
        });
    });
});

describe('UnsavedChangesBlocker', () => {
    let confirmSpy: MockInstance<typeof window.confirm>;

    beforeEach(() => {
        confirmSpy = vi.spyOn(window, 'confirm');
    });

    afterEach(() => {
        confirmSpy.mockRestore();
    });

    it('blocks in-app navigation when dirty and the user cancels', async () => {
        confirmSpy.mockReturnValue(false);

        function GuardedPage() {
            useUnsavedChangesGuard(true);
            return (
                <div>
                    <h1>Form</h1>
                    <Link to="/other">Go elsewhere</Link>
                </div>
            );
        }

        const router = createMemoryRouter(
            [
                {
                    element: (
                        <UnsavedChangesGuardProvider>
                            <UnsavedChangesBlocker />
                            <Outlet />
                        </UnsavedChangesGuardProvider>
                    ),
                    children: [
                        { path: '/', element: <GuardedPage /> },
                        { path: '/other', element: <h1>Other</h1> },
                    ],
                },
            ],
            { initialEntries: ['/'] },
        );

        render(<RouterProvider router={router} />);

        fireEvent.click(screen.getByRole('link', { name: 'Go elsewhere' }));

        await waitFor(() => {
            expect(confirmSpy).toHaveBeenCalledOnce();
        });
        expect(screen.getByRole('heading', { name: 'Form' })).toBeInTheDocument();
    });

    it('proceeds with in-app navigation when dirty and the user confirms', async () => {
        confirmSpy.mockReturnValue(true);

        function GuardedPage() {
            useUnsavedChangesGuard(true);
            return (
                <div>
                    <h1>Form</h1>
                    <Link to="/other">Go elsewhere</Link>
                </div>
            );
        }

        const router = createMemoryRouter(
            [
                {
                    element: (
                        <UnsavedChangesGuardProvider>
                            <UnsavedChangesBlocker />
                            <Outlet />
                        </UnsavedChangesGuardProvider>
                    ),
                    children: [
                        { path: '/', element: <GuardedPage /> },
                        { path: '/other', element: <h1>Other</h1> },
                    ],
                },
            ],
            { initialEntries: ['/'] },
        );

        render(<RouterProvider router={router} />);

        fireEvent.click(screen.getByRole('link', { name: 'Go elsewhere' }));

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Other' })).toBeInTheDocument();
        });
        expect(confirmSpy).toHaveBeenCalledOnce();
    });

    it('does not prompt or block navigation when nothing is dirty', async () => {
        confirmSpy.mockReturnValue(false);

        function GuardedPage() {
            useUnsavedChangesGuard(false);
            return (
                <div>
                    <h1>Form</h1>
                    <Link to="/other">Go elsewhere</Link>
                </div>
            );
        }

        const router = createMemoryRouter(
            [
                {
                    element: (
                        <UnsavedChangesGuardProvider>
                            <UnsavedChangesBlocker />
                            <Outlet />
                        </UnsavedChangesGuardProvider>
                    ),
                    children: [
                        { path: '/', element: <GuardedPage /> },
                        { path: '/other', element: <h1>Other</h1> },
                    ],
                },
            ],
            { initialEntries: ['/'] },
        );

        render(<RouterProvider router={router} />);

        fireEvent.click(screen.getByRole('link', { name: 'Go elsewhere' }));

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Other' })).toBeInTheDocument();
        });
        expect(confirmSpy).not.toHaveBeenCalled();
    });

    it('blocks browser back navigation when dirty and the user cancels', async () => {
        confirmSpy.mockReturnValue(false);

        function GuardedPage() {
            useUnsavedChangesGuard(true);
            return <h1>Form</h1>;
        }

        const router = createMemoryRouter(
            [
                {
                    element: (
                        <UnsavedChangesGuardProvider>
                            <UnsavedChangesBlocker />
                            <Outlet />
                        </UnsavedChangesGuardProvider>
                    ),
                    children: [
                        { path: '/', element: <GuardedPage /> },
                        { path: '/other', element: <h1>Other</h1> },
                    ],
                },
            ],
            { initialEntries: ['/other', '/'], initialIndex: 1 },
        );

        render(<RouterProvider router={router} />);

        await act(async () => {
            await router.navigate(-1);
        });

        await waitFor(() => {
            expect(confirmSpy).toHaveBeenCalledOnce();
        });
        expect(screen.getByRole('heading', { name: 'Form' })).toBeInTheDocument();
    });

    it('allows browser forward navigation when dirty and the user confirms', async () => {
        confirmSpy.mockReturnValue(true);

        function GuardedPage() {
            useUnsavedChangesGuard(true);
            return <h1>Form</h1>;
        }

        const router = createMemoryRouter(
            [
                {
                    element: (
                        <UnsavedChangesGuardProvider>
                            <UnsavedChangesBlocker />
                            <Outlet />
                        </UnsavedChangesGuardProvider>
                    ),
                    children: [
                        { path: '/', element: <GuardedPage /> },
                        { path: '/other', element: <h1>Other</h1> },
                    ],
                },
            ],
            { initialEntries: ['/', '/other'], initialIndex: 0 },
        );

        render(<RouterProvider router={router} />);

        await act(async () => {
            await router.navigate(1);
        });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Other' })).toBeInTheDocument();
        });
        expect(confirmSpy).toHaveBeenCalledOnce();
    });
});
