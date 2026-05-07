import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Card, CardBody, CardFooter, CardHeader } from './Card';

describe('Card', () => {
    it('renders with children', () => {
        render(<Card>Card content</Card>);
        expect(screen.getByText('Card content')).toBeInTheDocument();
    });

    it('applies card class', () => {
        render(<Card data-testid="card">Content</Card>);
        expect(screen.getByTestId('card')).toHaveClass('card');
    });

    it('applies interactive class when interactive prop is true', () => {
        render(
            <Card interactive data-testid="card">
                Interactive
            </Card>,
        );
        expect(screen.getByTestId('card')).toHaveAttribute('data-interactive', 'true');
    });

    it('does not apply interactive class by default', () => {
        render(<Card data-testid="card">Static</Card>);
        expect(screen.getByTestId('card')).not.toHaveAttribute('data-interactive');
    });

    it('applies custom className', () => {
        render(
            <Card className="custom-class" data-testid="card">
                Custom
            </Card>,
        );
        expect(screen.getByTestId('card')).toHaveClass('custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Card ref={ref}>Ref</Card>);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        render(<Card data-testid="my-card">Props</Card>);
        expect(screen.getByTestId('my-card')).toBeInTheDocument();
    });

    it('handles click events on interactive card', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();
        render(
            <Card interactive onClick={handleClick} data-testid="card">
                Clickable
            </Card>,
        );

        await user.click(screen.getByTestId('card'));
        expect(handleClick).toHaveBeenCalledTimes(1);
    });
});

describe('CardHeader', () => {
    it('renders with children', () => {
        render(<CardHeader>Header content</CardHeader>);
        expect(screen.getByText('Header content')).toBeInTheDocument();
    });

    it('applies card-header class', () => {
        render(<CardHeader data-testid="header">Header</CardHeader>);
        expect(screen.getByTestId('header')).toHaveClass('card-header');
    });

    it('applies custom className', () => {
        render(
            <CardHeader className="custom-header" data-testid="header">
                Custom
            </CardHeader>,
        );
        expect(screen.getByTestId('header')).toHaveClass('custom-header');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<CardHeader ref={ref}>Ref</CardHeader>);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        render(<CardHeader data-testid="my-header">Props</CardHeader>);
        expect(screen.getByTestId('my-header')).toBeInTheDocument();
    });
});

describe('CardBody', () => {
    it('renders with children', () => {
        render(<CardBody>Body content</CardBody>);
        expect(screen.getByText('Body content')).toBeInTheDocument();
    });

    it('applies card-body class', () => {
        render(<CardBody data-testid="body">Body</CardBody>);
        expect(screen.getByTestId('body')).toHaveClass('card-body');
    });

    it('applies custom className', () => {
        render(
            <CardBody className="custom-body" data-testid="body">
                Custom
            </CardBody>,
        );
        expect(screen.getByTestId('body')).toHaveClass('custom-body');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<CardBody ref={ref}>Ref</CardBody>);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        render(<CardBody data-testid="my-body">Props</CardBody>);
        expect(screen.getByTestId('my-body')).toBeInTheDocument();
    });
});

describe('CardFooter', () => {
    it('renders with children', () => {
        render(<CardFooter>Footer content</CardFooter>);
        expect(screen.getByText('Footer content')).toBeInTheDocument();
    });

    it('applies card-footer class', () => {
        render(<CardFooter data-testid="footer">Footer</CardFooter>);
        expect(screen.getByTestId('footer')).toHaveClass('card-footer');
    });

    it('applies custom className', () => {
        render(
            <CardFooter className="custom-footer" data-testid="footer">
                Custom
            </CardFooter>,
        );
        expect(screen.getByTestId('footer')).toHaveClass('custom-footer');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<CardFooter ref={ref}>Ref</CardFooter>);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        render(<CardFooter data-testid="my-footer">Props</CardFooter>);
        expect(screen.getByTestId('my-footer')).toBeInTheDocument();
    });
});

describe('Card composition', () => {
    it('renders card with header, body, and footer', () => {
        render(
            <Card>
                <CardHeader>Header</CardHeader>
                <CardBody>Body</CardBody>
                <CardFooter>Footer</CardFooter>
            </Card>,
        );

        expect(screen.getByText('Header')).toBeInTheDocument();
        expect(screen.getByText('Body')).toBeInTheDocument();
        expect(screen.getByText('Footer')).toBeInTheDocument();
    });

    it('renders card with only body', () => {
        render(
            <Card>
                <CardBody>Only body</CardBody>
            </Card>,
        );

        expect(screen.getByText('Only body')).toBeInTheDocument();
    });

    it('renders card with header and body', () => {
        render(
            <Card>
                <CardHeader>Header</CardHeader>
                <CardBody>Body</CardBody>
            </Card>,
        );

        expect(screen.getByText('Header')).toBeInTheDocument();
        expect(screen.getByText('Body')).toBeInTheDocument();
    });
});
