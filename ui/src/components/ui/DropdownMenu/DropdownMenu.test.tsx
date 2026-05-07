import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuCheckboxItem,
    DropdownMenuRadioItem,
    DropdownMenuRadioGroup,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuGroup,
    DropdownMenuSub,
    DropdownMenuSubTrigger,
    DropdownMenuSubContent,
    DropdownMenuShortcut,
    DropdownMenuPortal,
} from './DropdownMenu';

describe('DropdownMenu', () => {
    it('renders trigger element', () => {
        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>Item 1</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        expect(screen.getByRole('button', { name: 'Open Menu' })).toBeInTheDocument();
    });

    it('opens menu when trigger is clicked', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
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
    });

    it('renders menu items', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>Edit</DropdownMenuItem>
                    <DropdownMenuItem>Delete</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByRole('menuitem', { name: 'Edit' })).toBeInTheDocument();
            expect(screen.getByRole('menuitem', { name: 'Delete' })).toBeInTheDocument();
        });
    });

    it('closes menu when item is clicked', async () => {
        const user = userEvent.setup();
        const handleSelect = vi.fn();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem onSelect={handleSelect}>Edit</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByRole('menu')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('menuitem', { name: 'Edit' }));

        expect(handleSelect).toHaveBeenCalled();
        await waitFor(() => {
            expect(screen.queryByRole('menu')).not.toBeInTheDocument();
        });
    });

    it('closes menu when Escape key is pressed', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>Item</DropdownMenuItem>
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

    it('navigates items with arrow keys', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
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

    it('renders disabled items', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem disabled>Disabled Item</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            const item = screen.getByRole('menuitem', { name: 'Disabled Item' });
            expect(item).toHaveAttribute('data-disabled');
        });
    });

    it('renders label', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuLabel>Actions</DropdownMenuLabel>
                    <DropdownMenuItem>Edit</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByText('Actions')).toBeInTheDocument();
        });
    });

    it('renders separator', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>Edit</DropdownMenuItem>
                    <DropdownMenuSeparator data-testid="separator" />
                    <DropdownMenuItem>Delete</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByTestId('separator')).toBeInTheDocument();
            expect(screen.getByTestId('separator')).toHaveClass('dropdown-menu-separator');
        });
    });

    it('renders shortcut text', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>
                        Edit
                        <DropdownMenuShortcut>Ctrl+E</DropdownMenuShortcut>
                    </DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByText('Ctrl+E')).toBeInTheDocument();
            expect(screen.getByText('Ctrl+E')).toHaveClass('dropdown-menu-shortcut');
        });
    });

    it('applies inset class to item', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem inset>Inset Item</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            const item = screen.getByRole('menuitem', { name: 'Inset Item' });
            expect(item).toHaveAttribute('data-inset', 'true');
        });
    });

    it('applies inset class to label', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuLabel inset>Inset Label</DropdownMenuLabel>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByText('Inset Label')).toHaveAttribute('data-inset', 'true');
        });
    });

    it('applies custom className to content', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent className="custom-content">
                    <DropdownMenuItem>Item</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            const content = document.querySelector('.dropdown-menu-content');
            expect(content).toHaveClass('custom-content');
        });
    });

    it('supports controlled open state', async () => {
        const { rerender } = render(
            <DropdownMenu open={false}>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>Item</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        expect(screen.queryByRole('menu')).not.toBeInTheDocument();

        rerender(
            <DropdownMenu open={true}>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>Item</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await waitFor(() => {
            expect(screen.getByRole('menu')).toBeInTheDocument();
        });
    });

    it('calls onOpenChange callback', async () => {
        const user = userEvent.setup();
        const handleOpenChange = vi.fn();

        render(
            <DropdownMenu onOpenChange={handleOpenChange}>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuItem>Item</DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        expect(handleOpenChange).toHaveBeenCalledWith(true);
    });

    it('exports DropdownMenuGroup', () => {
        expect(DropdownMenuGroup).toBeDefined();
    });

    it('exports DropdownMenuPortal', () => {
        expect(DropdownMenuPortal).toBeDefined();
    });
});

describe('DropdownMenuCheckboxItem', () => {
    it('renders checkbox item', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuCheckboxItem checked={true}>Show Status Bar</DropdownMenuCheckboxItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            const item = screen.getByRole('menuitemcheckbox', { name: 'Show Status Bar' });
            expect(item).toBeInTheDocument();
            expect(item).toHaveAttribute('aria-checked', 'true');
        });
    });

    it('toggles checkbox state on click', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuCheckboxItem checked={false} onCheckedChange={handleCheckedChange}>
                        Show Status Bar
                    </DropdownMenuCheckboxItem>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByRole('menuitemcheckbox')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('menuitemcheckbox'));

        expect(handleCheckedChange).toHaveBeenCalledWith(true);
    });
});

describe('DropdownMenuRadioGroup', () => {
    it('renders radio items', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuRadioGroup value="small">
                        <DropdownMenuRadioItem value="small">Small</DropdownMenuRadioItem>
                        <DropdownMenuRadioItem value="medium">Medium</DropdownMenuRadioItem>
                        <DropdownMenuRadioItem value="large">Large</DropdownMenuRadioItem>
                    </DropdownMenuRadioGroup>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByRole('menuitemradio', { name: 'Small' })).toBeInTheDocument();
            expect(screen.getByRole('menuitemradio', { name: 'Medium' })).toBeInTheDocument();
            expect(screen.getByRole('menuitemradio', { name: 'Large' })).toBeInTheDocument();
        });
    });

    it('shows selected radio item', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuRadioGroup value="medium">
                        <DropdownMenuRadioItem value="small">Small</DropdownMenuRadioItem>
                        <DropdownMenuRadioItem value="medium">Medium</DropdownMenuRadioItem>
                    </DropdownMenuRadioGroup>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            const selectedItem = screen.getByRole('menuitemradio', { name: 'Medium' });
            expect(selectedItem).toHaveAttribute('aria-checked', 'true');
        });
    });

    it('changes selection on click', async () => {
        const user = userEvent.setup();
        const handleValueChange = vi.fn();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuRadioGroup value="small" onValueChange={handleValueChange}>
                        <DropdownMenuRadioItem value="small">Small</DropdownMenuRadioItem>
                        <DropdownMenuRadioItem value="large">Large</DropdownMenuRadioItem>
                    </DropdownMenuRadioGroup>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByRole('menuitemradio', { name: 'Large' })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('menuitemradio', { name: 'Large' }));

        expect(handleValueChange).toHaveBeenCalledWith('large');
    });
});

describe('DropdownMenuSub', () => {
    it('renders submenu trigger', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuSub>
                        <DropdownMenuSubTrigger>More Options</DropdownMenuSubTrigger>
                        <DropdownMenuSubContent>
                            <DropdownMenuItem>Sub Item</DropdownMenuItem>
                        </DropdownMenuSubContent>
                    </DropdownMenuSub>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByText('More Options')).toBeInTheDocument();
        });
    });

    it('opens submenu on hover', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuSub>
                        <DropdownMenuSubTrigger>More Options</DropdownMenuSubTrigger>
                        <DropdownMenuSubContent>
                            <DropdownMenuItem>Sub Item</DropdownMenuItem>
                        </DropdownMenuSubContent>
                    </DropdownMenuSub>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByText('More Options')).toBeInTheDocument();
        });

        await user.hover(screen.getByText('More Options'));

        await waitFor(() => {
            expect(screen.getByText('Sub Item')).toBeInTheDocument();
        });
    });

    it('opens submenu with arrow key', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuSub>
                        <DropdownMenuSubTrigger>More Options</DropdownMenuSubTrigger>
                        <DropdownMenuSubContent>
                            <DropdownMenuItem>Sub Item</DropdownMenuItem>
                        </DropdownMenuSubContent>
                    </DropdownMenuSub>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByText('More Options')).toBeInTheDocument();
        });

        await user.keyboard('{ArrowDown}');

        await user.keyboard('{ArrowRight}');

        await waitFor(() => {
            expect(screen.getByText('Sub Item')).toBeInTheDocument();
        });
    });

    it('applies inset class to sub-trigger', async () => {
        const user = userEvent.setup();

        render(
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <button>Open Menu</button>
                </DropdownMenuTrigger>
                <DropdownMenuContent>
                    <DropdownMenuSub>
                        <DropdownMenuSubTrigger inset>More Options</DropdownMenuSubTrigger>
                        <DropdownMenuSubContent>
                            <DropdownMenuItem>Sub Item</DropdownMenuItem>
                        </DropdownMenuSubContent>
                    </DropdownMenuSub>
                </DropdownMenuContent>
            </DropdownMenu>,
        );

        await user.click(screen.getByRole('button', { name: 'Open Menu' }));

        await waitFor(() => {
            expect(screen.getByText('More Options')).toHaveAttribute('data-inset', 'true');
        });
    });
});
