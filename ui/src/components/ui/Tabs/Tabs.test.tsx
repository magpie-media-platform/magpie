import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Tabs, TabsList, TabsTrigger, TabsContent } from './Tabs';

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

    it('supports keyboard navigation with arrow keys', async () => {
        const user = userEvent.setup();

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
        tab1.focus();

        await user.keyboard('{ArrowRight}');
        expect(screen.getByRole('tab', { name: 'Tab 2' })).toHaveFocus();

        await user.keyboard('{ArrowRight}');
        expect(screen.getByRole('tab', { name: 'Tab 3' })).toHaveFocus();

        await user.keyboard('{ArrowLeft}');
        expect(screen.getByRole('tab', { name: 'Tab 2' })).toHaveFocus();
    });

    it('wraps focus when navigating past the ends', async () => {
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
        tab1.focus();

        await user.keyboard('{ArrowLeft}');
        expect(screen.getByRole('tab', { name: 'Tab 2' })).toHaveFocus();

        await user.keyboard('{ArrowRight}');
        expect(screen.getByRole('tab', { name: 'Tab 1' })).toHaveFocus();
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

        const tab1 = screen.getByRole('tab', { name: 'Tab 1' });
        tab1.focus();

        await user.keyboard('{ArrowRight}');
        await user.keyboard('{Enter}');

        expect(handleValueChange).toHaveBeenCalledWith('tab2');
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
