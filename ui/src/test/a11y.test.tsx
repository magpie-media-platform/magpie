import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { checkA11y } from './axe-helper';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Card, CardHeader, CardBody } from '@/components/ui/Card';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Skeleton } from '@/components/ui/Skeleton';
import { EmptyState } from '@/components/ui/EmptyState';
import { Avatar } from '@/components/ui/Avatar';

describe('Accessibility: Button', () => {
    it('has no a11y violations in default state', async () => {
        const { container } = render(<Button>Click me</Button>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations when disabled', async () => {
        const { container } = render(<Button disabled>Disabled</Button>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations when loading', async () => {
        const { container } = render(<Button loading>Loading</Button>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it.each(['primary', 'secondary', 'ghost', 'destructive'] as const)(
        'has no a11y violations for the %s variant',
        async (variant) => {
            const { container } = render(<Button variant={variant}>{variant}</Button>);
            const results = await checkA11y(container);
            expect(results).toHaveNoViolations();
        },
    );
});

describe('Accessibility: Input', () => {
    it('has no a11y violations when labeled', async () => {
        const { container } = render(
            <div>
                <Label htmlFor="email">Email</Label>
                <Input id="email" type="email" placeholder="Enter email" />
            </div>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations with aria-label', async () => {
        const { container } = render(<Input aria-label="Search" placeholder="Search..." />);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations when disabled', async () => {
        const { container } = render(
            <div>
                <Label htmlFor="disabled-input">Disabled field</Label>
                <Input id="disabled-input" disabled />
            </div>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations with error state', async () => {
        const { container } = render(
            <div>
                <Label htmlFor="error-input">Email</Label>
                <Input id="error-input" aria-invalid="true" aria-describedby="error-message" />
                <span id="error-message">Please enter a valid email</span>
            </div>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});

describe('Accessibility: Card', () => {
    it('has no a11y violations with content', async () => {
        const { container } = render(
            <Card>
                <CardHeader>
                    <h3>Card Title</h3>
                    <p>Card description text</p>
                </CardHeader>
                <CardBody>Card body content</CardBody>
            </Card>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations with minimal content', async () => {
        const { container } = render(
            <Card>
                <CardBody>Simple card content</CardBody>
            </Card>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});

describe('Accessibility: Alert', () => {
    it('has no a11y violations for info variant', async () => {
        const { container } = render(<Alert variant="info">Information message</Alert>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations for success variant', async () => {
        const { container } = render(<Alert variant="success">Success message</Alert>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations for warning variant', async () => {
        const { container } = render(<Alert variant="warning">Warning message</Alert>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations for error variant', async () => {
        const { container } = render(<Alert variant="error">Error message</Alert>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations with title', async () => {
        const { container } = render(
            <Alert variant="info" title="Alert Title">
                Alert description content
            </Alert>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});

describe('Accessibility: Badge', () => {
    it('has no a11y violations for default variant', async () => {
        const { container } = render(<Badge>Default</Badge>);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it.each(['healthy', 'degraded', 'unhealthy', 'unknown', 'stabilizing', 'missing'] as const)(
        'has no a11y violations for the %s semantic variant',
        async (variant) => {
            const { container } = render(<Badge variant={variant}>{variant}</Badge>);
            const results = await checkA11y(container);
            expect(results).toHaveNoViolations();
        },
    );
});

describe('Accessibility: Avatar', () => {
    it('has no a11y violations with image', async () => {
        const { container } = render(<Avatar src="/avatar.jpg" alt="User avatar" />);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations with fallback', async () => {
        const { container } = render(<Avatar fallback="JD" alt="John Doe" />);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});

describe('Accessibility: Skeleton', () => {
    it('has no a11y violations', async () => {
        const { container } = render(<Skeleton className="h-4 w-32" />);
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});

describe('Accessibility: EmptyState', () => {
    it('has no a11y violations', async () => {
        const { container } = render(
            <EmptyState title="No items found" description="Try adjusting your filters or adding new items." />,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations with action', async () => {
        const { container } = render(
            <EmptyState
                title="No items"
                description="Get started by creating your first item."
                action={<Button>Create item</Button>}
            />,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});

describe('Accessibility: Form combinations', () => {
    it('has no a11y violations for a form with multiple fields', async () => {
        const { container } = render(
            <form aria-label="Contact form">
                <div>
                    <Label htmlFor="name">Name</Label>
                    <Input id="name" required />
                </div>
                <div>
                    <Label htmlFor="email">Email</Label>
                    <Input id="email" type="email" required />
                </div>
                <div>
                    <Label htmlFor="message">Message</Label>
                    <Input id="message" />
                </div>
                <Button type="submit">Submit</Button>
            </form>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations for a form with validation errors', async () => {
        const { container } = render(
            <form aria-label="Login form">
                <div>
                    <Label htmlFor="username">Username</Label>
                    <Input id="username" aria-invalid="true" aria-describedby="username-error" />
                    <span id="username-error" role="alert">
                        Username is required
                    </span>
                </div>
                <div>
                    <Label htmlFor="password">Password</Label>
                    <Input id="password" type="password" aria-invalid="true" aria-describedby="password-error" />
                    <span id="password-error" role="alert">
                        Password is required
                    </span>
                </div>
                <Button type="submit" disabled>
                    Login
                </Button>
            </form>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});

describe('Accessibility: Interactive component states', () => {
    it('has no a11y violations for button group', async () => {
        const { container } = render(
            <div role="group" aria-label="Actions">
                <Button>Save</Button>
                <Button variant="ghost">Cancel</Button>
                <Button variant="destructive">Delete</Button>
            </div>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });

    it('has no a11y violations for status badges', async () => {
        const { container } = render(
            <div>
                <Badge variant="healthy">Online</Badge>
                <Badge variant="unhealthy">Offline</Badge>
                <Badge variant="degraded">Degraded</Badge>
            </div>,
        );
        const results = await checkA11y(container);
        expect(results).toHaveNoViolations();
    });
});
