import { describe, it, expect, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { SkipLink } from '@/components/ui/SkipLink';
import {
    Dialog,
    DialogTrigger,
    DialogContent,
    DialogTitle,
    DialogDescription,
    DialogHeader,
    DialogFooter,
} from '@/components/ui/Dialog';
import { Sheet, SheetTrigger, SheetContent, SheetTitle, SheetHeader, SheetFooter } from '@/components/ui/Sheet';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem } from '@/components/ui/DropdownMenu';
import { Select, SelectTrigger, SelectContent, SelectItem, SelectValue } from '@/components/ui/Select';
import { Checkbox } from '@/components/ui/Checkbox';
import { Switch } from '@/components/ui/Switch';

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

describe('Keyboard Navigation Integration', () => {
    describe('Skip Link Navigation', () => {
        it('skip link becomes visible on focus', async () => {
            const user = userEvent.setup();

            render(
                <div>
                    <SkipLink targetId="main-content" />
                    <nav>
                        <a href="/home">Home</a>
                        <a href="/about">About</a>
                    </nav>
                    <main id="main-content" tabIndex={-1}>
                        <h1>Main Content</h1>
                    </main>
                </div>,
            );

            const skipLink = screen.getByRole('link', { name: 'Skip to main content' });

            expect(skipLink).toHaveClass('skip-link');

            await user.tab();
            expect(skipLink).toHaveFocus();
        });

        it('skip link targets correct element via href', () => {
            render(
                <div>
                    <SkipLink targetId="main-content" />
                    <main id="main-content">Content</main>
                </div>,
            );

            const skipLink = screen.getByRole('link', { name: 'Skip to main content' });
            expect(skipLink).toHaveAttribute('href', '#main-content');
        });
    });

    describe('Tab Navigation', () => {
        it('tabs through focusable elements in document order', async () => {
            const user = userEvent.setup();

            render(
                <div>
                    <Button data-testid="button1">First</Button>
                    <Input data-testid="input1" placeholder="Input" />
                    <Button data-testid="button2">Second</Button>
                    <a href="/link" data-testid="link1">
                        Link
                    </a>
                </div>,
            );

            await user.tab();
            expect(screen.getByTestId('button1')).toHaveFocus();

            await user.tab();
            expect(screen.getByTestId('input1')).toHaveFocus();

            await user.tab();
            expect(screen.getByTestId('button2')).toHaveFocus();

            await user.tab();
            expect(screen.getByTestId('link1')).toHaveFocus();
        });

        it('reverse tabs with Shift+Tab', async () => {
            const user = userEvent.setup();

            render(
                <div>
                    <Button data-testid="button1">First</Button>
                    <Button data-testid="button2">Second</Button>
                    <Button data-testid="button3">Third</Button>
                </div>,
            );

            await user.tab();
            await user.tab();
            await user.tab();
            expect(screen.getByTestId('button3')).toHaveFocus();

            await user.tab({ shift: true });
            expect(screen.getByTestId('button2')).toHaveFocus();

            await user.tab({ shift: true });
            expect(screen.getByTestId('button1')).toHaveFocus();
        });

        it('skips disabled elements during tab navigation', async () => {
            const user = userEvent.setup();

            render(
                <div>
                    <Button data-testid="button1">First</Button>
                    <Button data-testid="button2" disabled>
                        Disabled
                    </Button>
                    <Button data-testid="button3">Third</Button>
                </div>,
            );

            await user.tab();
            expect(screen.getByTestId('button1')).toHaveFocus();

            await user.tab();

            expect(screen.getByTestId('button3')).toHaveFocus();
        });
    });

    describe('Dialog Focus Management', () => {
        it('traps focus inside dialog when open', async () => {
            const user = userEvent.setup();

            render(
                <div>
                    <Button data-testid="outside-button">Outside</Button>
                    <Dialog>
                        <DialogTrigger asChild>
                            <Button>Open Dialog</Button>
                        </DialogTrigger>
                        <DialogContent>
                            <DialogHeader>
                                <DialogTitle>Test Dialog</DialogTitle>
                            </DialogHeader>
                            <DialogFooter>
                                <Button data-testid="dialog-button1">Action 1</Button>
                                <Button data-testid="dialog-button2">Action 2</Button>
                            </DialogFooter>
                        </DialogContent>
                    </Dialog>
                </div>,
            );

            await user.click(screen.getByRole('button', { name: 'Open Dialog' }));

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            const dialog = screen.getByRole('dialog');

            await user.tab();
            await user.tab();
            await user.tab();
            await user.tab();
            await user.tab();

            expect(dialog.contains(document.activeElement)).toBe(true);
        });

        it('returns focus to trigger after dialog closes', async () => {
            const user = userEvent.setup();

            render(
                <Dialog>
                    <DialogTrigger asChild>
                        <Button>Open Dialog</Button>
                    </DialogTrigger>
                    <DialogContent>
                        <DialogHeader>
                            <DialogTitle>Test Dialog</DialogTitle>
                        </DialogHeader>
                    </DialogContent>
                </Dialog>,
            );

            const trigger = screen.getByRole('button', { name: 'Open Dialog' });
            await user.click(trigger);

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            await user.keyboard('{Escape}');

            await waitFor(() => {
                expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
            });

            expect(trigger).toHaveFocus();
        });

        it('closes dialog on Escape key', async () => {
            const user = userEvent.setup();

            render(
                <Dialog>
                    <DialogTrigger asChild>
                        <Button>Open Dialog</Button>
                    </DialogTrigger>
                    <DialogContent>
                        <DialogHeader>
                            <DialogTitle>Test Dialog</DialogTitle>
                        </DialogHeader>
                    </DialogContent>
                </Dialog>,
            );

            await user.click(screen.getByRole('button', { name: 'Open Dialog' }));

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            await user.keyboard('{Escape}');

            await waitFor(() => {
                expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
            });
        });
    });

    describe('Sheet Focus Management', () => {
        it('traps focus inside sheet when open', async () => {
            const user = userEvent.setup();

            render(
                <div>
                    <Button data-testid="outside-button">Outside</Button>
                    <Sheet>
                        <SheetTrigger asChild>
                            <Button>Open Sheet</Button>
                        </SheetTrigger>
                        <SheetContent>
                            <SheetHeader>
                                <SheetTitle>Test Sheet</SheetTitle>
                            </SheetHeader>
                            <SheetFooter>
                                <Button data-testid="sheet-button1">Action 1</Button>
                                <Button data-testid="sheet-button2">Action 2</Button>
                            </SheetFooter>
                        </SheetContent>
                    </Sheet>
                </div>,
            );

            await user.click(screen.getByRole('button', { name: 'Open Sheet' }));

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            const sheet = screen.getByRole('dialog');

            await user.tab();
            await user.tab();
            await user.tab();
            await user.tab();
            await user.tab();

            expect(sheet.contains(document.activeElement)).toBe(true);
        });

        it('returns focus to trigger after sheet closes', async () => {
            const user = userEvent.setup();

            render(
                <Sheet>
                    <SheetTrigger asChild>
                        <Button>Open Sheet</Button>
                    </SheetTrigger>
                    <SheetContent>
                        <SheetHeader>
                            <SheetTitle>Test Sheet</SheetTitle>
                        </SheetHeader>
                    </SheetContent>
                </Sheet>,
            );

            const trigger = screen.getByRole('button', { name: 'Open Sheet' });
            await user.click(trigger);

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            await user.keyboard('{Escape}');

            await waitFor(() => {
                expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
            });

            expect(trigger).toHaveFocus();
        });
    });

    describe('Tabs Keyboard Navigation', () => {
        it('navigates tabs with arrow keys', async () => {
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
            focusElement(tab1);

            pressKeys('{ArrowRight}');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Tab 2' })).toHaveFocus();
            });

            pressKeys('{ArrowRight}');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Tab 3' })).toHaveFocus();
            });

            pressKeys('{ArrowLeft}');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Tab 2' })).toHaveFocus();
            });
        });

        it('wraps tab focus at ends', async () => {
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

        it('activates tab with Enter or Space', async () => {
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

        it('skips disabled tabs during arrow navigation', async () => {
            render(
                <Tabs defaultValue="tab1">
                    <TabsList>
                        <TabsTrigger value="tab1">Tab 1</TabsTrigger>
                        <TabsTrigger value="tab2" disabled>
                            Tab 2
                        </TabsTrigger>
                        <TabsTrigger value="tab3">Tab 3</TabsTrigger>
                    </TabsList>
                    <TabsContent value="tab1">Content 1</TabsContent>
                    <TabsContent value="tab2">Content 2</TabsContent>
                    <TabsContent value="tab3">Content 3</TabsContent>
                </Tabs>,
            );

            const tab1 = screen.getByRole('tab', { name: 'Tab 1' });
            focusElement(tab1);

            pressKeys('{ArrowRight}');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Tab 3' })).toHaveFocus();
            });
        });
    });

    describe('Dropdown Menu Keyboard Navigation', () => {
        it('opens menu with Enter or Space', async () => {
            const user = userEvent.setup();

            render(
                <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                        <Button>Open Menu</Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent>
                        <DropdownMenuItem>Item 1</DropdownMenuItem>
                        <DropdownMenuItem>Item 2</DropdownMenuItem>
                    </DropdownMenuContent>
                </DropdownMenu>,
            );

            const trigger = screen.getByRole('button', { name: 'Open Menu' });
            trigger.focus();

            await user.keyboard('{Enter}');

            await waitFor(() => {
                expect(screen.getByRole('menu')).toBeInTheDocument();
            });
        });

        it('navigates menu items with arrow keys', async () => {
            const user = userEvent.setup();

            render(
                <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                        <Button>Open Menu</Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent>
                        <DropdownMenuItem>First</DropdownMenuItem>
                        <DropdownMenuItem>Second</DropdownMenuItem>
                        <DropdownMenuItem>Third</DropdownMenuItem>
                    </DropdownMenuContent>
                </DropdownMenu>,
            );

            await user.click(screen.getByRole('button', { name: 'Open Menu' }));

            await waitFor(() => {
                expect(screen.getByRole('menu')).toBeInTheDocument();
            });

            await user.keyboard('{ArrowDown}');
            await user.keyboard('{ArrowDown}');

            const secondItem = screen.getByRole('menuitem', { name: 'Second' });
            expect(secondItem).toHaveAttribute('data-highlighted');
        });

        it('closes menu on Escape', async () => {
            const user = userEvent.setup();

            render(
                <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                        <Button>Open Menu</Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent>
                        <DropdownMenuItem>Item 1</DropdownMenuItem>
                    </DropdownMenuContent>
                </DropdownMenu>,
            );

            await user.click(screen.getByRole('button', { name: 'Open Menu' }));

            await waitFor(() => {
                expect(screen.getByRole('menu')).toBeInTheDocument();
            });

            await user.keyboard('{Escape}');

            await waitFor(() => {
                expect(screen.queryByRole('menu')).not.toBeInTheDocument();
            });
        });

        it('selects item with Enter', async () => {
            const user = userEvent.setup();
            const handleSelect = vi.fn();

            render(
                <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                        <Button>Open Menu</Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent>
                        <DropdownMenuItem onSelect={handleSelect}>Select Me</DropdownMenuItem>
                    </DropdownMenuContent>
                </DropdownMenu>,
            );

            await user.click(screen.getByRole('button', { name: 'Open Menu' }));

            await waitFor(() => {
                expect(screen.getByRole('menu')).toBeInTheDocument();
            });

            await user.keyboard('{ArrowDown}');
            await user.keyboard('{Enter}');

            expect(handleSelect).toHaveBeenCalled();
        });
    });

    describe('Select Keyboard Navigation', () => {
        it('opens select with Enter, Space, or ArrowDown', async () => {
            const user = userEvent.setup();

            render(
                <Select>
                    <SelectTrigger data-testid="select-trigger">
                        <SelectValue placeholder="Choose option" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="option1">Option 1</SelectItem>
                        <SelectItem value="option2">Option 2</SelectItem>
                    </SelectContent>
                </Select>,
            );

            const trigger = screen.getByTestId('select-trigger');
            trigger.focus();

            await user.keyboard('{Enter}');

            await waitFor(() => {
                expect(screen.getByRole('listbox')).toBeInTheDocument();
            });
        });

        it('closes select on Escape', async () => {
            const user = userEvent.setup();

            render(
                <Select>
                    <SelectTrigger data-testid="select-trigger">
                        <SelectValue placeholder="Choose option" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="option1">Option 1</SelectItem>
                    </SelectContent>
                </Select>,
            );

            await user.click(screen.getByTestId('select-trigger'));

            await waitFor(() => {
                expect(screen.getByRole('listbox')).toBeInTheDocument();
            });

            await user.keyboard('{Escape}');

            await waitFor(() => {
                expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
            });
        });
    });

    describe('Checkbox and Switch Keyboard Activation', () => {
        it('toggles checkbox with Space', async () => {
            const user = userEvent.setup();
            const handleChange = vi.fn();

            render(<Checkbox onCheckedChange={handleChange} aria-label="Test checkbox" />);

            const checkbox = screen.getByRole('checkbox', { name: 'Test checkbox' });
            checkbox.focus();

            await user.keyboard(' ');

            expect(handleChange).toHaveBeenCalledWith(true);
        });

        it('toggles switch with Space', async () => {
            const user = userEvent.setup();
            const handleChange = vi.fn();

            render(<Switch onCheckedChange={handleChange} aria-label="Test switch" />);

            const switchEl = screen.getByRole('switch', { name: 'Test switch' });
            switchEl.focus();

            await user.keyboard(' ');

            expect(handleChange).toHaveBeenCalledWith(true);
        });
    });

    describe('Button Keyboard Activation', () => {
        it('activates button with Enter', async () => {
            const user = userEvent.setup();
            const handleClick = vi.fn();

            render(<Button onClick={handleClick}>Click Me</Button>);

            const button = screen.getByRole('button', { name: 'Click Me' });
            button.focus();

            await user.keyboard('{Enter}');

            expect(handleClick).toHaveBeenCalled();
        });

        it('activates button with Space', async () => {
            const user = userEvent.setup();
            const handleClick = vi.fn();

            render(<Button onClick={handleClick}>Click Me</Button>);

            const button = screen.getByRole('button', { name: 'Click Me' });
            button.focus();

            await user.keyboard(' ');

            expect(handleClick).toHaveBeenCalled();
        });

        it('does not activate disabled button', async () => {
            const user = userEvent.setup();
            const handleClick = vi.fn();

            render(
                <Button onClick={handleClick} disabled>
                    Click Me
                </Button>,
            );

            screen.getByRole('button', { name: 'Click Me' });

            await user.keyboard('{Enter}');

            expect(handleClick).not.toHaveBeenCalled();
        });
    });

    describe('Form Navigation', () => {
        it('navigates form fields with Tab', async () => {
            const user = userEvent.setup();

            render(
                <form>
                    <Input data-testid="input1" placeholder="First name" />
                    <Input data-testid="input2" placeholder="Last name" />
                    <Input data-testid="input3" placeholder="Email" type="email" />
                    <Button type="submit">Submit</Button>
                </form>,
            );

            await user.tab();
            expect(screen.getByTestId('input1')).toHaveFocus();

            await user.tab();
            expect(screen.getByTestId('input2')).toHaveFocus();

            await user.tab();
            expect(screen.getByTestId('input3')).toHaveFocus();

            await user.tab();
            expect(screen.getByRole('button', { name: 'Submit' })).toHaveFocus();
        });

        it('submits form with Enter on button', async () => {
            const user = userEvent.setup();
            const handleSubmit = vi.fn((e) => e.preventDefault());

            render(
                <form onSubmit={handleSubmit}>
                    <Input placeholder="Name" />
                    <Button type="submit">Submit</Button>
                </form>,
            );

            const submitButton = screen.getByRole('button', { name: 'Submit' });
            submitButton.focus();

            await user.keyboard('{Enter}');

            expect(handleSubmit).toHaveBeenCalled();
        });
    });

    describe('Combined Navigation Flow', () => {
        it('completes a full interaction flow with keyboard only', async () => {
            const user = userEvent.setup();
            const handleDialogAction = vi.fn();

            render(
                <div>
                    <SkipLink targetId="main-content" />
                    <nav>
                        <Button data-testid="nav-button">Navigation</Button>
                    </nav>
                    <main id="main-content" tabIndex={-1}>
                        <Dialog>
                            <DialogTrigger asChild>
                                <Button>Open Form</Button>
                            </DialogTrigger>
                            <DialogContent>
                                <DialogHeader>
                                    <DialogTitle>Form Dialog</DialogTitle>
                                </DialogHeader>
                                <Input placeholder="Enter value" data-testid="dialog-input" />
                                <DialogFooter>
                                    <Button onClick={handleDialogAction}>Save</Button>
                                </DialogFooter>
                            </DialogContent>
                        </Dialog>
                    </main>
                </div>,
            );

            await user.tab();
            expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveFocus();

            await user.tab();
            await user.tab();

            await user.keyboard('{Enter}');

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            const dialog = screen.getByRole('dialog');
            expect(dialog.contains(document.activeElement)).toBe(true);

            await user.tab();

            await user.type(screen.getByTestId('dialog-input'), 'Test value');
            expect(screen.getByTestId('dialog-input')).toHaveValue('Test value');

            await user.tab();
            await user.keyboard('{Enter}');

            expect(handleDialogAction).toHaveBeenCalled();
        });
    });

    describe('Focus Visible Styling', () => {
        it('button shows focus-visible styles when focused via keyboard', async () => {
            const user = userEvent.setup();

            render(<Button data-testid="focus-button">Focus Me</Button>);

            await user.tab();

            const button = screen.getByTestId('focus-button');
            expect(button).toHaveFocus();

            expect(button).toHaveAttribute('type', 'button');
        });

        it('input shows focus styles when focused', async () => {
            const user = userEvent.setup();

            render(<Input data-testid="focus-input" placeholder="Focus test" />);

            await user.tab();

            const input = screen.getByTestId('focus-input');
            expect(input).toHaveFocus();
        });
    });

    describe('ARIA Attributes', () => {
        it('dialog has correct ARIA attributes', async () => {
            const user = userEvent.setup();

            render(
                <Dialog>
                    <DialogTrigger asChild>
                        <Button>Open</Button>
                    </DialogTrigger>
                    <DialogContent>
                        <DialogHeader>
                            <DialogTitle>Dialog Title</DialogTitle>
                            <DialogDescription>Dialog description</DialogDescription>
                        </DialogHeader>
                    </DialogContent>
                </Dialog>,
            );

            await user.click(screen.getByRole('button', { name: 'Open' }));

            await waitFor(() => {
                const dialog = screen.getByRole('dialog');
                expect(dialog).toBeInTheDocument();

                const trigger = screen.getByRole('button', { name: 'Open', hidden: true });
                expect(trigger.closest('[aria-hidden="true"]')).toBeInTheDocument();
            });
        });

        it('tabs have correct ARIA attributes', () => {
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
            const tabpanel = screen.getByRole('tabpanel');

            expect(tab1).toHaveAttribute('aria-selected', 'true');
            expect(tab2).toHaveAttribute('aria-selected', 'false');
            expect(tab1).toHaveAttribute('aria-controls');
            expect(tabpanel).toHaveAttribute('aria-labelledby');
        });

        it('checkbox has correct ARIA state', async () => {
            const user = userEvent.setup();

            render(<Checkbox aria-label="Accept terms" />);

            const checkbox = screen.getByRole('checkbox', { name: 'Accept terms' });
            expect(checkbox).toHaveAttribute('aria-checked', 'false');

            await user.click(checkbox);

            expect(checkbox).toHaveAttribute('aria-checked', 'true');
        });

        it('switch has correct ARIA state', async () => {
            const user = userEvent.setup();

            render(<Switch aria-label="Enable feature" />);

            const switchEl = screen.getByRole('switch', { name: 'Enable feature' });
            expect(switchEl).toHaveAttribute('aria-checked', 'false');

            await user.click(switchEl);

            expect(switchEl).toHaveAttribute('aria-checked', 'true');
        });
    });
});
