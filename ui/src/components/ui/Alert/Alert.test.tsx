import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { InfoIcon, WarningIcon } from '@phosphor-icons/react';

import { Alert } from './Alert';

describe('Alert', () => {
    it('renders with children', () => {
        render(<Alert>Alert message</Alert>);
        expect(screen.getByText('Alert message')).toBeInTheDocument();
    });

    it('announces informational content politely', () => {
        render(<Alert>Accessible alert</Alert>);
        expect(screen.getByRole('status')).toBeInTheDocument();
    });

    it('applies default info variant class', () => {
        render(<Alert>Info alert</Alert>);
        const alert = screen.getByRole('status');
        expect(alert).toHaveClass('alert');
        expect(alert).toHaveAttribute('data-variant', 'info');
    });

    it.each([
        ['info', 'status'],
        ['success', 'status'],
        ['warning', 'alert'],
        ['error', 'alert'],
    ] as const)('announces %s with role %s', (variant, role) => {
        render(<Alert variant={variant}>{variant}</Alert>);
        expect(screen.getByRole(role)).toHaveAttribute('data-variant', variant);
    });

    it('renders title', () => {
        render(<Alert title="Alert Title">Alert description</Alert>);
        expect(screen.getByText('Alert Title')).toBeInTheDocument();
        expect(screen.getByText('Alert Title')).toHaveClass('alert__title');
    });

    it('renders description in alert__description', () => {
        render(<Alert>Description text</Alert>);
        const description = screen.getByText('Description text');
        expect(description).toHaveClass('alert__description');
    });

    it('renders icon when provided', () => {
        render(<Alert icon={<InfoIcon data-testid="alert-icon" />}>With icon</Alert>);
        expect(screen.getByTestId('alert-icon')).toBeInTheDocument();
    });

    it('hides icon from screen readers', () => {
        render(<Alert icon={<WarningIcon data-testid="warning-icon" />}>With warning</Alert>);
        const iconContainer = screen.getByTestId('warning-icon').parentElement;
        expect(iconContainer).toHaveAttribute('aria-hidden', 'true');
    });

    it('does not render dismiss button by default', () => {
        render(<Alert>Not dismissible</Alert>);
        expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument();
    });

    it('renders dismiss button when dismissible', () => {
        render(<Alert dismissible>Dismissible alert</Alert>);
        expect(screen.getByRole('button', { name: /dismiss/i })).toBeInTheDocument();
    });

    it('calls onDismiss when dismiss button is clicked', async () => {
        const user = userEvent.setup();
        const handleDismiss = vi.fn();

        render(
            <Alert dismissible onDismiss={handleDismiss}>
                Click to dismiss
            </Alert>,
        );

        await user.click(screen.getByRole('button', { name: /dismiss/i }));
        expect(handleDismiss).toHaveBeenCalledOnce();
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Alert ref={ref}>Ref alert</Alert>);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('applies custom className', () => {
        render(<Alert className="custom-alert">Custom</Alert>);
        expect(screen.getByRole('status')).toHaveClass('custom-alert');
    });

    it('passes through additional props', () => {
        render(<Alert data-testid="my-alert">Props</Alert>);
        expect(screen.getByTestId('my-alert')).toBeInTheDocument();
    });

    it('renders title without description', () => {
        render(<Alert title="Title only" />);
        expect(screen.getByText('Title only')).toBeInTheDocument();
        expect(screen.getByRole('status').querySelector('.alert__description')).not.toBeInTheDocument();
    });

    it('renders description without title', () => {
        render(<Alert>Description only</Alert>);
        expect(screen.getByText('Description only')).toBeInTheDocument();
        expect(screen.getByRole('status').querySelector('.alert__title')).not.toBeInTheDocument();
    });

    it('renders both title and description', () => {
        render(<Alert title="Alert Title">Alert description</Alert>);
        expect(screen.getByText('Alert Title')).toBeInTheDocument();
        expect(screen.getByText('Alert description')).toBeInTheDocument();
    });
});
