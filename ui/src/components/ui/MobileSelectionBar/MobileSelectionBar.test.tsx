import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { AppShellMain } from '@/components/ui/AppShell';
import { Tabs, TabsContent } from '@/components/ui/Tabs';
import type { BulkSelectionState } from '@/types/bulkSelection';

import { MobileSelectionBar } from './MobileSelectionBar';

const emptySelection: BulkSelectionState = {
    allPages: false,
    hasSelection: false,
    headerChecked: false,
    selectedCount: 0,
    pageSelectedCount: 0,
    totalCount: 40,
    showAcrossPagesPrompt: false,
    isRowChecked: () => false,
    toggleRow: vi.fn(),
    toggleHeader: vi.fn(),
    selectAllAcrossPages: vi.fn(),
    clear: vi.fn(),
};

afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
});

describe('MobileSelectionBar scroll clearance', () => {
    it.each([
        { shell: 'app', Main: AppShellMain, removedTab: 'deny', afterRemoval: '0px', afterSwitch: '194px' },
        { shell: 'app', Main: AppShellMain, removedTab: 'mime', afterRemoval: '84px', afterSwitch: '84px' },
    ])(
        'preserves peer clearance in the $shell shell when the $removedTab tab bar unmounts',
        ({ Main, removedTab, afterRemoval, afterSwitch }) => {
            // Arrange
            let denyHeight = 56;
            let mimeHeight = 124;
            const callbacks = new Map<Element, ResizeObserverCallback>();
            vi.stubGlobal(
                'ResizeObserver',
                class {
                    observe: (element: Element) => void;
                    constructor(callback: ResizeObserverCallback) {
                        this.observe = (element: Element) => {
                            if (element.classList.contains('mobile-selection-bar')) callbacks.set(element, callback);
                        };
                    }
                    disconnect = vi.fn();
                },
            );
            vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
                if (this.classList.contains('mobile-selection-bar')) {
                    const inactive = this.closest('[role="tabpanel"]')?.getAttribute('data-state') === 'inactive';
                    const height = inactive ? 0 : this.dataset.testid === 'deny-bar' ? denyHeight : mimeHeight;
                    return { top: 784 - height, bottom: 784, height } as DOMRect;
                }
                return { top: 60, bottom: 784, height: 724 } as DOMRect;
            });
            const denyBar = (
                <MobileSelectionBar data-testid="deny-bar" selection={emptySelection} itemNounPlural="rules" />
            );
            const mimeBar = (
                <MobileSelectionBar data-testid="mime-bar" selection={emptySelection} itemNounPlural="types" />
            );

            // Act
            const { rerender, unmount } = render(
                <Main>
                    <Tabs value="deny">
                        <TabsContent value="deny" forceMount>
                            {denyBar}
                        </TabsContent>
                        <TabsContent value="mime" forceMount>
                            {mimeBar}
                        </TabsContent>
                    </Tabs>
                </Main>,
            );
            const main = screen.getByRole('main');
            const denyResize = callbacks.get(screen.getByTestId('deny-bar'))!;
            const mimeResize = callbacks.get(screen.getByTestId('mime-bar'))!;

            // Assert
            expect(screen.getByTestId('mime-bar').closest('[role="tabpanel"]')).toHaveAttribute(
                'data-state',
                'inactive',
            );
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 56px');

            // Act
            rerender(
                <Main>
                    <Tabs value="mime">
                        <TabsContent value="deny" forceMount>
                            {denyBar}
                        </TabsContent>
                        <TabsContent value="mime" forceMount>
                            {mimeBar}
                        </TabsContent>
                    </Tabs>
                </Main>,
            );
            act(() => {
                mimeResize([], {} as ResizeObserver);
                denyResize([], {} as ResizeObserver);
            });

            // Assert
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 124px');

            // Act
            denyHeight = 84;
            mimeHeight = 194;
            rerender(
                <Main>
                    <Tabs value="deny">
                        <TabsContent value="deny" forceMount>
                            {denyBar}
                        </TabsContent>
                        <TabsContent value="mime" forceMount>
                            {mimeBar}
                        </TabsContent>
                    </Tabs>
                </Main>,
            );
            act(() => {
                denyResize([], {} as ResizeObserver);
                mimeResize([], {} as ResizeObserver);
                window.dispatchEvent(new Event('resize'));
            });

            // Assert
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 84px');

            // Act
            rerender(
                <Main>
                    <Tabs value="deny">
                        <TabsContent value="deny" forceMount>
                            {removedTab === 'deny' ? null : denyBar}
                        </TabsContent>
                        <TabsContent value="mime" forceMount>
                            {removedTab === 'mime' ? null : mimeBar}
                        </TabsContent>
                    </Tabs>
                </Main>,
            );

            // Assert
            expect(main.style.getPropertyValue('--mobile-selection-bar-clearance')).toBe(afterRemoval);

            // Act
            rerender(
                <Main>
                    <Tabs value={removedTab === 'deny' ? 'mime' : 'deny'}>
                        <TabsContent value="deny" forceMount>
                            {removedTab === 'deny' ? null : denyBar}
                        </TabsContent>
                        <TabsContent value="mime" forceMount>
                            {removedTab === 'mime' ? null : mimeBar}
                        </TabsContent>
                    </Tabs>
                </Main>,
            );
            act(() => {
                mimeResize([], {} as ResizeObserver);
                denyResize([], {} as ResizeObserver);
            });

            // Assert
            expect(main.style.getPropertyValue('--mobile-selection-bar-clearance')).toBe(afterSwitch);

            // Act
            unmount();
            act(() => {
                denyResize([], {} as ResizeObserver);
                mimeResize([], {} as ResizeObserver);
            });

            // Assert
            expect(main.style.getPropertyValue('--mobile-selection-bar-clearance')).toBe('');
        },
    );

    it('keeps separate scroll containers independent when a peer container unmounts', () => {
        // Arrange
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
            const first = this.closest('[aria-label="First list"]') !== null;
            if (this.classList.contains('mobile-selection-bar')) {
                return first
                    ? ({ top: 544, bottom: 600, height: 56 } as DOMRect)
                    : ({ top: 760, bottom: 900, height: 140 } as DOMRect);
            }
            return { top: 0, bottom: first ? 600 : 900, height: first ? 600 : 900 } as DOMRect;
        });

        // Act
        const { rerender, unmount } = render(
            <>
                <AppShellMain key="first" aria-label="First list">
                    <MobileSelectionBar selection={emptySelection} itemNounPlural="items" />
                </AppShellMain>
                <AppShellMain key="second" aria-label="Second list">
                    <MobileSelectionBar selection={emptySelection} itemNounPlural="items" />
                </AppShellMain>
            </>,
        );
        const first = screen.getByRole('main', { name: 'First list' });
        const second = screen.getByRole('main', { name: 'Second list' });

        // Assert
        expect(first).toHaveStyle('--mobile-selection-bar-clearance: 56px');
        expect(second).toHaveStyle('--mobile-selection-bar-clearance: 140px');

        // Act
        rerender(
            <>
                <AppShellMain key="second" aria-label="Second list">
                    <MobileSelectionBar selection={emptySelection} itemNounPlural="items" />
                </AppShellMain>
            </>,
        );
        act(() => {
            window.dispatchEvent(new Event('resize'));
        });

        // Assert
        expect(first.style.getPropertyValue('--mobile-selection-bar-clearance')).toBe('');
        expect(second).toHaveStyle('--mobile-selection-bar-clearance: 140px');

        // Act
        unmount();

        // Assert
        expect(second.style.getPropertyValue('--mobile-selection-bar-clearance')).toBe('');
    });

    it.each([['app', AppShellMain]])(
        'reserves the actual fixed-bar overlap in the %s shell as contents change',
        (_shell, Main) => {
            // Arrange
            let barTop = 728;
            let barHeight = 56;
            let notifyResize: ResizeObserverCallback = () => {};
            const observe = vi.fn();
            const disconnect = vi.fn();
            vi.stubGlobal(
                'ResizeObserver',
                class {
                    constructor(callback: ResizeObserverCallback) {
                        notifyResize = callback;
                    }
                    observe = observe;
                    disconnect = disconnect;
                },
            );
            vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
                if (this.classList.contains('mobile-selection-bar')) {
                    return { top: barTop, bottom: barTop + barHeight, height: barHeight } as DOMRect;
                }
                if (this.tagName === 'MAIN') {
                    return { top: 60, bottom: 784, height: 724 } as DOMRect;
                }
                return { top: 0, bottom: 0, height: 0 } as DOMRect;
            });

            // Act
            const { rerender } = render(
                <Main>
                    <button type="button">Last page</button>
                    <MobileSelectionBar selection={emptySelection} itemNounPlural="items" />
                </Main>,
            );
            // Assert
            const main = screen.getByRole('main');
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 56px');
            expect(observe).toHaveBeenCalledWith(main);
            expect(observe).toHaveBeenCalledWith(screen.getByRole('region', { name: 'Selection actions' }));

            // Act
            barTop = 660;
            barHeight = 124;
            rerender(
                <Main>
                    <button type="button">Last page</button>
                    <MobileSelectionBar
                        selection={{ ...emptySelection, hasSelection: true, selectedCount: 1 }}
                        itemNounPlural="items"
                    >
                        <button type="button">Wrapped action</button>
                    </MobileSelectionBar>
                </Main>,
            );
            act(() => notifyResize([], {} as ResizeObserver));

            // Assert
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 124px');
            expect(screen.getByRole('button', { name: 'Last page' })).toBeInTheDocument();

            // Act
            barTop = 590;
            barHeight = 194;
            rerender(
                <Main>
                    <button type="button">Last page</button>
                    <MobileSelectionBar
                        selection={{
                            ...emptySelection,
                            hasSelection: true,
                            selectedCount: 20,
                            pageSelectedCount: 20,
                            showAcrossPagesPrompt: true,
                        }}
                        itemNounPlural="items"
                    >
                        <button type="button">Wrapped action</button>
                    </MobileSelectionBar>
                </Main>,
            );
            act(() => notifyResize([], {} as ResizeObserver));

            // Assert
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 194px');
            expect(screen.getByRole('button', { name: 'Select all 40 across all pages' })).toBeInTheDocument();

            // Act
            barTop = 844;
            barHeight = 0;
            act(() => {
                window.dispatchEvent(new Event('resize'));
            });

            // Assert
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 0px');

            // Act
            rerender(
                <Main>
                    <button type="button">Last page</button>
                </Main>,
            );

            // Assert
            expect(main.style.getPropertyValue('--mobile-selection-bar-clearance')).toBe('');
            expect(disconnect).toHaveBeenCalled();

            // Act
            barTop = 700;
            barHeight = 84;
            rerender(
                <Main>
                    <button type="button">Different route's last page</button>
                    <MobileSelectionBar selection={emptySelection} itemNounPlural="items" />
                </Main>,
            );

            // Assert
            expect(main).toHaveStyle('--mobile-selection-bar-clearance: 84px');
            expect(screen.getByRole('button', { name: "Different route's last page" })).toBeInTheDocument();
        },
    );
});
