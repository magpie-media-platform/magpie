import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { FolderIcon, MagnifyingGlassIcon } from '@phosphor-icons/react';

import { EmptyState } from './EmptyState';

describe('EmptyState', () => {
    it('renders with title', () => {
        render(<EmptyState title="No items found" />);
        expect(screen.getByText('No items found')).toBeInTheDocument();
    });

    it('renders title as heading', () => {
        render(<EmptyState title="Empty list" />);
        expect(screen.getByRole('heading', { name: 'Empty list', level: 3 })).toBeInTheDocument();
    });

    it('supports a page-level title', () => {
        render(<EmptyState title="Missing page" titleLevel={1} />);
        expect(screen.getByRole('heading', { name: 'Missing page', level: 1 })).toBeInTheDocument();
    });

    it('applies empty-state class', () => {
        render(<EmptyState title="Test" data-testid="empty" />);
        expect(screen.getByTestId('empty')).toHaveClass('empty-state');
    });

    it('has data-component attribute', () => {
        render(<EmptyState title="Test" data-testid="empty" />);
        expect(screen.getByTestId('empty')).toHaveAttribute('data-component', 'empty-state');
    });

    it('applies default md size class', () => {
        render(<EmptyState title="Test" data-testid="empty" />);
        expect(screen.getByTestId('empty')).toHaveAttribute('data-size', 'md');
    });

    it('applies size classes', () => {
        const sizes = ['sm', 'md', 'lg'] as const;

        sizes.forEach((size) => {
            const { unmount } = render(<EmptyState title={`Size ${size}`} size={size} data-testid="empty" />);
            expect(screen.getByTestId('empty')).toHaveAttribute('data-size', size);
            unmount();
        });
    });

    it('renders description when provided', () => {
        render(<EmptyState title="No results" description="Try adjusting your search criteria" />);
        expect(screen.getByText('Try adjusting your search criteria')).toBeInTheDocument();
    });

    it('does not render description when not provided', () => {
        render(<EmptyState title="No results" data-testid="empty" />);
        expect(screen.queryByText('Try adjusting')).not.toBeInTheDocument();
        expect(screen.getByTestId('empty').querySelector('.empty-state__description')).not.toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        render(<EmptyState title="Empty" icon={<FolderIcon data-testid="icon" />} />);
        expect(screen.getByTestId('icon')).toBeInTheDocument();
    });

    it('hides icon from screen readers', () => {
        render(<EmptyState title="Empty" icon={<MagnifyingGlassIcon data-testid="icon" />} />);
        const iconContainer = screen.getByTestId('icon').parentElement;
        expect(iconContainer).toHaveAttribute('aria-hidden', 'true');
    });

    it('does not render icon container when icon not provided', () => {
        render(<EmptyState title="No icon" data-testid="empty" />);
        expect(screen.getByTestId('empty').querySelector('.empty-state__icon')).not.toBeInTheDocument();
    });

    it('renders action when provided', () => {
        render(<EmptyState title="No items" action={<button type="button">Add item</button>} />);
        expect(screen.getByRole('button', { name: 'Add item' })).toBeInTheDocument();
    });

    it('EmptyState_CallsOnRetry_WhenRetryIsClicked', async () => {
        // Arrange
        const user = userEvent.setup();
        const onRetry = vi.fn();
        render(<EmptyState title="Failed to load items" onRetry={onRetry} />);

        // Act
        await user.click(screen.getByRole('button', { name: 'Retry' }));

        // Assert
        expect(onRetry).toHaveBeenCalledWith();
    });

    it('does not render action container when action not provided', () => {
        render(<EmptyState title="No action" data-testid="empty" />);
        expect(screen.getByTestId('empty').querySelector('.empty-state__action')).not.toBeInTheDocument();
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<EmptyState ref={ref} title="Ref test" />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('applies custom className', () => {
        render(<EmptyState title="Custom" className="my-empty" data-testid="empty" />);
        expect(screen.getByTestId('empty')).toHaveClass('my-empty');
    });

    it('passes through additional props', () => {
        render(<EmptyState title="Props" data-testid="my-empty" />);
        expect(screen.getByTestId('my-empty')).toBeInTheDocument();
    });

    it('renders all parts together', () => {
        render(
            <EmptyState
                icon={<FolderIcon data-testid="folder-icon" />}
                title="No files"
                description="Upload a file to get started"
                action={<button type="button">Upload</button>}
                data-testid="empty"
            />,
        );

        expect(screen.getByTestId('folder-icon')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'No files' })).toBeInTheDocument();
        expect(screen.getByText('Upload a file to get started')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Upload' })).toBeInTheDocument();
    });

    it('description has empty-state__description class', () => {
        render(<EmptyState title="Test" description="Description text" />);
        expect(screen.getByText('Description text')).toHaveClass('empty-state__description');
    });

    it('title has empty-state__title class', () => {
        render(<EmptyState title="Title text" />);
        expect(screen.getByText('Title text')).toHaveClass('empty-state__title');
    });
});
