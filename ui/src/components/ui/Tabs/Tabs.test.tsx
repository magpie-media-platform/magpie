import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { DetailTabsList } from './DetailTabsList';
import { DetailTabTrigger } from './DetailTabTrigger';
import { Tabs, TabsList, TabsTrigger, TabsContent } from './Tabs';

function focusElement(element: HTMLElement) {
    act(() => {
        element.focus();
    });
}

function pressKeys(keys: string) {
    const target = document.activeElement instanceof HTMLElement ? document.activeElement : document.body;
    const key = keys === '{ArrowRight}' ? 'ArrowRight' : keys === '{ArrowLeft}' ? 'ArrowLeft' : 'Enter';
    fireEvent.keyDown(target, { key, code: key });
}

describe('Tabs', () => {
    it('renders tabs with triggers and content', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        expect(screen.getByRole('tablist')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Tab 1' })).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Tab 2' })).toBeInTheDocument();
    });

    it('displays the default tab content', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        expect(screen.getByText('Content 1')).toBeInTheDocument();
        expect(screen.queryByText('Content 2')).not.toBeInTheDocument();
    });

    it('switches content when clicking a tab', async () => {
        const user = userEvent.setup();

        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        await user.click(screen.getByRole('tab', { name: 'Tab 2' }));

        expect(screen.queryByText('Content 1')).not.toBeInTheDocument();
        expect(screen.getByText('Content 2')).toBeInTheDocument();
    });

    it('holds then releases the region height on switch when holdHeightOnValueChange is set', async () => {
        const user = userEvent.setup();
        const frames: FrameRequestCallback[] = [];
        const rafSpy = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
            frames.push(cb);
            return frames.length;
        });
        const offsetSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(420);

        const { container } = render(
            <Tabs defaultValue="tab1" holdHeightOnValueChange>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const root = container.querySelector('[data-component="tabs"]') as HTMLElement;

        await user.click(screen.getByRole('tab', { name: 'Tab 2' }));
        expect(root.style.minHeight).toBe('420px');

        frames.forEach((cb) => cb(0));
        expect(root.style.minHeight).toBe('');

        offsetSpy.mockRestore();
        rafSpy.mockRestore();
    });

    it('does not pin a height on switch when holdHeightOnValueChange is unset', async () => {
        const user = userEvent.setup();
        const offsetSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(420);

        const { container } = render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const root = container.querySelector('[data-component="tabs"]') as HTMLElement;

        await user.click(screen.getByRole('tab', { name: 'Tab 2' }));
        expect(root.style.minHeight).toBe('');

        offsetSpy.mockRestore();
    });

    it('holds and re-applies the captured scroll until the incoming panel settles to the held height', async () => {
        const user = userEvent.setup();

        const frames: FrameRequestCallback[] = [];
        const rafSpy = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
            frames.push(cb);
            return frames.length;
        });

        let panelHeight = 445;
        const offsetSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
            this: HTMLElement,
        ) {
            return this.getAttribute('role') === 'tabpanel' ? panelHeight : 1200;
        });

        let resizeCb: ResizeObserverCallback = () => {};
        const observeSpy = vi.fn();
        const disconnectSpy = vi.fn();
        vi.stubGlobal(
            'ResizeObserver',
            class {
                constructor(cb: ResizeObserverCallback) {
                    resizeCb = cb;
                }
                observe = observeSpy;
                unobserve = vi.fn();
                disconnect = disconnectSpy;
            },
        );

        const scroller = document.createElement('div');
        document.body.appendChild(scroller);
        scroller.scrollTop = 1100;
        const realGetComputedStyle = window.getComputedStyle.bind(window);
        const gcsSpy = vi.spyOn(window, 'getComputedStyle').mockImplementation((el, pseudo) => {
            if (el === scroller) {
                return { overflowY: 'auto' } as CSSStyleDeclaration;
            }
            return realGetComputedStyle(el as Element, pseudo);
        });

        render(
            <Tabs defaultValue="tab1" holdHeightOnValueChange>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
            { container: scroller },
        );

        const root = scroller.querySelector('[data-component="tabs"]') as HTMLElement;

        await user.click(screen.getByRole('tab', { name: 'Tab 2' }));
        expect(root.style.minHeight).toBe('1200px');

        frames.forEach((cb) => cb(0));
        expect(observeSpy.mock.calls.filter(([element]) => element.getAttribute('role') === 'tabpanel')).toHaveLength(
            1,
        );
        expect(root.style.minHeight).toBe('1200px');

        scroller.scrollTop = 445;
        panelHeight = 1200;
        resizeCb([], {} as ResizeObserver);

        expect(scroller.scrollTop).toBe(1100);
        expect(root.style.minHeight).toBe('');
        expect(disconnectSpy).toHaveBeenCalled();

        gcsSpy.mockRestore();
        offsetSpy.mockRestore();
        rafSpy.mockRestore();
        vi.unstubAllGlobals();
        scroller.remove();
    });

    it('releases the hold via the backstop when a shorter tab never reaches the held height', async () => {
        const user = userEvent.setup();

        const frames: FrameRequestCallback[] = [];
        const rafSpy = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
            frames.push(cb);
            return frames.length;
        });

        const offsetSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
            this: HTMLElement,
        ) {
            return this.getAttribute('role') === 'tabpanel' ? 300 : 1200;
        });

        vi.stubGlobal(
            'ResizeObserver',
            class {
                observe = vi.fn();
                unobserve = vi.fn();
                disconnect = vi.fn();
            },
        );

        const { container } = render(
            <Tabs defaultValue="tab1" holdHeightOnValueChange>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const root = container.querySelector('[data-component="tabs"]') as HTMLElement;

        await user.click(screen.getByRole('tab', { name: 'Tab 2' }));
        expect(root.style.minHeight).toBe('1200px');

        const timers: Array<() => void> = [];
        const setTimeoutSpy = vi.spyOn(window, 'setTimeout').mockImplementation(((cb: () => void) => {
            timers.push(cb);
            return 1;
        }) as typeof window.setTimeout);

        frames.forEach((cb) => cb(0));
        expect(root.style.minHeight).toBe('1200px');

        timers.forEach((fn) => fn());
        expect(root.style.minHeight).toBe('');

        setTimeoutSpy.mockRestore();
        offsetSpy.mockRestore();
        rafSpy.mockRestore();
        vi.unstubAllGlobals();
    });

    it('disconnects the resize observer when unmounted mid-hold', async () => {
        const user = userEvent.setup();

        const frames: FrameRequestCallback[] = [];
        const rafSpy = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
            frames.push(cb);
            return frames.length;
        });

        const offsetSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
            this: HTMLElement,
        ) {
            return this.getAttribute('role') === 'tabpanel' ? 445 : 1200;
        });

        const disconnectSpy = vi.fn();
        vi.stubGlobal(
            'ResizeObserver',
            class {
                observe = vi.fn();
                unobserve = vi.fn();
                disconnect = disconnectSpy;
            },
        );

        const { unmount } = render(
            <Tabs defaultValue="tab1" holdHeightOnValueChange>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        await user.click(screen.getByRole('tab', { name: 'Tab 2' }));
        frames.forEach((cb) => cb(0));

        unmount();
        expect(disconnectSpy).toHaveBeenCalled();

        offsetSpy.mockRestore();
        rafSpy.mockRestore();
        vi.unstubAllGlobals();
    });

    it('sets aria-selected on active tab', async () => {
        const user = userEvent.setup();

        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const tab1 = screen.getByRole('tab', { name: 'Tab 1' });
        const tab2 = screen.getByRole('tab', { name: 'Tab 2' });

        expect(tab1).toHaveAttribute('aria-selected', 'true');
        expect(tab2).toHaveAttribute('aria-selected', 'false');

        await user.click(tab2);

        expect(tab1).toHaveAttribute('aria-selected', 'false');
        expect(tab2).toHaveAttribute('aria-selected', 'true');
    });

    it('moves focus and activation across tabs with the arrow keys', async () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                    <TabsTrigger value="tab3">Tab 3</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
                <TabsContent value="tab3">Content 3</TabsContent>
            </Tabs>,
        );

        const tab1 = screen.getByRole('tab', { name: 'Tab 1' });
        const tab2 = screen.getByRole('tab', { name: 'Tab 2' });
        expect(tab2).toHaveAttribute('data-state', 'inactive');

        focusElement(tab1);

        pressKeys('{ArrowRight}');
        await waitFor(() => {
            expect(tab2).toHaveFocus();
        });
        expect(tab2).toHaveAttribute('data-state', 'active');

        pressKeys('{ArrowRight}');
        await waitFor(() => {
            expect(screen.getByRole('tab', { name: 'Tab 3' })).toHaveFocus();
        });

        pressKeys('{ArrowLeft}');
        await waitFor(() => {
            expect(screen.getByRole('tab', { name: 'Tab 2' })).toHaveFocus();
        });
    });

    it('wraps focus when navigating past the ends', async () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const tab1 = screen.getByRole('tab', { name: 'Tab 1' });
        focusElement(tab1);

        pressKeys('{ArrowLeft}');
        await waitFor(() => {
            expect(screen.getByRole('tab', { name: 'Tab 2' })).toHaveFocus();
        });

        pressKeys('{ArrowRight}');
        await waitFor(() => {
            expect(screen.getByRole('tab', { name: 'Tab 1' })).toHaveFocus();
        });
    });

    it('calls onValueChange when tab changes', async () => {
        const user = userEvent.setup();
        const handleValueChange = vi.fn();

        render(
            <Tabs defaultValue="tab1" onValueChange={handleValueChange}>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        await user.click(screen.getByRole('tab', { name: 'Tab 2' }));

        expect(handleValueChange).toHaveBeenCalledWith('tab2');
    });

    it('supports controlled value', () => {
        const { rerender } = render(
            <Tabs value="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        expect(screen.getByText('Content 1')).toBeInTheDocument();

        rerender(
            <Tabs value="tab2">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        expect(screen.queryByText('Content 1')).not.toBeInTheDocument();
        expect(screen.getByText('Content 2')).toBeInTheDocument();
    });

    it('disables individual tabs when disabled prop is set', async () => {
        const user = userEvent.setup();
        const handleValueChange = vi.fn();

        render(
            <Tabs defaultValue="tab1" onValueChange={handleValueChange}>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2" disabled>
                        Tab 2
                    </TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const disabledTab = screen.getByRole('tab', { name: 'Tab 2' });
        expect(disabledTab).toHaveAttribute('data-disabled');

        await user.click(disabledTab);

        expect(handleValueChange).not.toHaveBeenCalled();
        expect(screen.getByText('Content 1')).toBeInTheDocument();
    });

    it('applies custom className to Tabs root', () => {
        render(
            <Tabs defaultValue="tab1" className="custom-tabs">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        const root = document.querySelector('.tabs');
        expect(root).toHaveClass('tabs');
        expect(root).toHaveClass('custom-tabs');
    });

    it('applies custom className to TabsList', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList className="custom-list">
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        const list = screen.getByRole('tablist');
        expect(list).toHaveClass('tabs-list');
        expect(list).toHaveClass('custom-list');
    });

    it('applies custom className to TabsTrigger', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1" className="custom-trigger">
                        Tab 1
                    </TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        const trigger = screen.getByRole('tab', { name: 'Tab 1' });
        expect(trigger).toHaveClass('tabs-trigger');
        expect(trigger).toHaveClass('custom-trigger');
    });

    it('applies custom className to TabsContent', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1" className="custom-content">
                    Content 1
                </TabsContent>
            </Tabs>,
        );

        const content = screen.getByRole('tabpanel');
        expect(content).toHaveClass('tabs-content');
        expect(content).toHaveClass('custom-content');
    });

    it('supports vertical orientation', () => {
        render(
            <Tabs defaultValue="tab1" orientation="vertical">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const tablist = screen.getByRole('tablist');
        expect(tablist).toHaveAttribute('aria-orientation', 'vertical');
    });

    it('associates tabpanel with its trigger via aria attributes', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        const trigger = screen.getByRole('tab', { name: 'Tab 1' });
        const panel = screen.getByRole('tabpanel');

        const triggerId = trigger.getAttribute('id');
        const panelId = panel.getAttribute('id');

        expect(trigger).toHaveAttribute('aria-controls', panelId);
        expect(panel).toHaveAttribute('aria-labelledby', triggerId);
    });

    it('activates tab on Enter/Space when focused', async () => {
        const handleValueChange = vi.fn();

        render(
            <Tabs defaultValue="tab1" onValueChange={handleValueChange}>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>,
        );

        const tab1 = screen.getByRole('tab', { name: 'Tab 1' });
        focusElement(tab1);

        pressKeys('{ArrowRight}');
        pressKeys('{Enter}');

        await waitFor(() => {
            expect(handleValueChange).toHaveBeenCalledWith('tab2');
        });
    });

    it('forwards ref to Tabs root', () => {
        const ref = vi.fn();

        render(
            <Tabs defaultValue="tab1" ref={ref}>
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        expect(ref).toHaveBeenCalled();
        expect(ref.mock.calls[0][0]).toBeInstanceOf(HTMLDivElement);
    });

    it('forwards ref to TabsList', () => {
        const ref = vi.fn();

        render(
            <Tabs defaultValue="tab1">
                <TabsList ref={ref}>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        expect(ref).toHaveBeenCalled();
        expect(ref.mock.calls[0][0]).toBeInstanceOf(HTMLDivElement);
    });

    it('forwards ref to TabsTrigger', () => {
        const ref = vi.fn();

        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1" ref={ref}>
                        Tab 1
                    </TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        expect(ref).toHaveBeenCalled();
        expect(ref.mock.calls[0][0]).toBeInstanceOf(HTMLButtonElement);
    });

    it('forwards ref to TabsContent', () => {
        const ref = vi.fn();

        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                </TabsList>
                <TabsContent value="tab1" ref={ref}>
                    Content 1
                </TabsContent>
            </Tabs>,
        );

        expect(ref).toHaveBeenCalled();
        expect(ref.mock.calls[0][0]).toBeInstanceOf(HTMLDivElement);
    });

    it('renders icons alongside tab labels', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">
                        <span data-testid="icon">📋</span>
                        Tab 1
                    </TabsTrigger>
                </TabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
            </Tabs>,
        );

        expect(screen.getByTestId('icon')).toBeInTheDocument();
    });
});

describe('TabsList fit', () => {
    it('shares the row equally while the labels hold', () => {
        render(
            <Tabs defaultValue="tab1">
                <TabsList>
                    <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                    <TabsTrigger value="tab2">Tab 2</TabsTrigger>
                </TabsList>
            </Tabs>,
        );

        expect(screen.getByRole('tablist')).toHaveAttribute('data-fit', 'equal');
    });
});

describe('DetailTabsList', () => {
    const strip = () => (
        <TooltipProvider>
            <Tabs defaultValue="tab1">
                <DetailTabsList>
                    <DetailTabTrigger value="tab1" label="Tab 1" icon={<span>📋</span>} />
                    <DetailTabTrigger value="tab2" label="Tab 2" icon={<span>📁</span>} count={3} />
                </DetailTabsList>
                <TabsContent value="tab1">Content 1</TabsContent>
                <TabsContent value="tab2">Content 2</TabsContent>
            </Tabs>
        </TooltipProvider>
    );

    it('shows labels in the detail strip while the tabs hold', () => {
        render(strip());

        const list = screen.getByRole('tablist');
        expect(list).toHaveClass('tabs-list--detail');
        expect(list).toHaveAttribute('data-fit', 'labels');
        expect(screen.getByText('Tab 1')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Tab 2 (3)' })).toBeInTheDocument();
        expect(screen.getByText('3')).toBeInTheDocument();
    });
});
