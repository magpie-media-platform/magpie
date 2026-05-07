import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useForm } from 'react-hook-form';
import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';

import { Input } from '../Input';
import { Form, FormField, FormItem, FormLabel, FormControl, FormDescription, FormMessage, useFormField } from './Form';

const testSchema = z.object({
    username: z.string().min(3, 'Username must be at least 3 characters'),
    email: z.string().email('Invalid email address'),
});

type TestFormData = z.infer<typeof testSchema>;

interface TestFormProps {
    onSubmit?: (data: TestFormData) => void;
    defaultValues?: Partial<TestFormData>;
}

function TestForm({ onSubmit = vi.fn(), defaultValues }: TestFormProps) {
    const form = useForm<TestFormData>({
        resolver: zodResolver(testSchema),
        defaultValues: {
            username: '',
            email: '',
            ...defaultValues,
        },
    });

    return (
        <Form {...form}>
            <form onSubmit={form.handleSubmit(onSubmit)} data-testid="test-form">
                <FormField name="username">
                    <FormItem>
                        <FormLabel>Username</FormLabel>
                        <FormControl>
                            <Input {...form.register('username')} />
                        </FormControl>
                        <FormDescription>Enter your username</FormDescription>
                        <FormMessage />
                    </FormItem>
                </FormField>
                <FormField name="email">
                    <FormItem>
                        <FormLabel>Email</FormLabel>
                        <FormControl>
                            <Input type="email" {...form.register('email')} />
                        </FormControl>
                        <FormMessage />
                    </FormItem>
                </FormField>
                <button type="submit">Submit</button>
            </form>
        </Form>
    );
}

describe('Form', () => {
    it('renders form with fields', () => {
        render(<TestForm />);
        expect(screen.getByLabelText('Username')).toBeInTheDocument();
        expect(screen.getByLabelText('Email')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Submit' })).toBeInTheDocument();
    });

    it('displays form description', () => {
        render(<TestForm />);
        expect(screen.getByText('Enter your username')).toBeInTheDocument();
    });

    it('submits valid form data', async () => {
        const user = userEvent.setup();
        const handleSubmit = vi.fn();
        render(<TestForm onSubmit={handleSubmit} />);

        await user.type(screen.getByLabelText('Username'), 'johndoe');
        await user.type(screen.getByLabelText('Email'), 'john@example.com');
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(handleSubmit).toHaveBeenCalledWith(
                { username: 'johndoe', email: 'john@example.com' },
                expect.anything(),
            );
        });
    });

    it('displays validation error for short username', async () => {
        const user = userEvent.setup();
        const handleSubmit = vi.fn();
        render(<TestForm onSubmit={handleSubmit} />);

        await user.type(screen.getByLabelText('Username'), 'ab');
        await user.type(screen.getByLabelText('Email'), 'valid@example.com');
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(screen.getByRole('alert')).toHaveTextContent('Username must be at least 3 characters');
        });
        expect(handleSubmit).not.toHaveBeenCalled();
    });

    it('does not submit form with invalid email', async () => {
        const user = userEvent.setup();
        const handleSubmit = vi.fn();
        render(<TestForm onSubmit={handleSubmit} />);

        await user.type(screen.getByLabelText('Username'), 'validuser');
        await user.type(screen.getByLabelText('Email'), 'not-an-email');
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        expect(handleSubmit).not.toHaveBeenCalled();
    });

    it('does not submit form with multiple validation errors', async () => {
        const user = userEvent.setup();
        const handleSubmit = vi.fn();
        render(<TestForm onSubmit={handleSubmit} />);

        await user.type(screen.getByLabelText('Username'), 'ab');
        await user.type(screen.getByLabelText('Email'), 'invalid');
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        expect(handleSubmit).not.toHaveBeenCalled();
    });

    it('populates form with default values', () => {
        render(<TestForm defaultValues={{ username: 'defaultuser', email: 'default@example.com' }} />);
        expect(screen.getByLabelText('Username')).toHaveValue('defaultuser');
        expect(screen.getByLabelText('Email')).toHaveValue('default@example.com');
    });

    it('clears error when valid value is entered', async () => {
        const user = userEvent.setup();
        render(<TestForm />);

        await user.type(screen.getByLabelText('Username'), 'ab');
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(screen.getByText('Username must be at least 3 characters')).toBeInTheDocument();
        });

        await user.clear(screen.getByLabelText('Username'));
        await user.type(screen.getByLabelText('Username'), 'validname');

        await waitFor(() => {
            expect(screen.queryByText('Username must be at least 3 characters')).not.toBeInTheDocument();
        });
    });
});

describe('FormItem', () => {
    it('applies custom className', () => {
        function TestFormItem() {
            const form = useForm({ defaultValues: { test: '' } });
            return (
                <Form {...form}>
                    <FormField name="test">
                        <FormItem className="custom-class" data-testid="form-item">
                            <FormLabel>Test</FormLabel>
                        </FormItem>
                    </FormField>
                </Form>
            );
        }
        render(<TestFormItem />);
        expect(screen.getByTestId('form-item')).toHaveClass('form-item', 'custom-class');
    });
});

describe('FormLabel', () => {
    it('associates label with form control', () => {
        render(<TestForm />);
        const label = screen.getByText('Username');
        const input = screen.getByLabelText('Username');
        expect(label).toHaveAttribute('for', input.id);
    });

    it('shows error styling when field has error', async () => {
        const user = userEvent.setup();
        render(<TestForm />);

        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            const labels = document.querySelectorAll('.label--error');
            expect(labels.length).toBeGreaterThan(0);
        });
    });

    it('shows required indicator when specified', () => {
        function RequiredForm() {
            const form = useForm({ defaultValues: { test: '' } });
            return (
                <Form {...form}>
                    <FormField name="test">
                        <FormItem>
                            <FormLabel required>Required Field</FormLabel>
                        </FormItem>
                    </FormField>
                </Form>
            );
        }
        render(<RequiredForm />);
        expect(screen.getByText('*')).toBeInTheDocument();
    });
});

describe('FormDescription', () => {
    it('renders description text', () => {
        render(<TestForm />);
        expect(screen.getByText('Enter your username')).toBeInTheDocument();
    });

    it('applies form-description class', () => {
        render(<TestForm />);
        expect(screen.getByText('Enter your username')).toHaveClass('form-description');
    });

    it('has correct id for aria-describedby', () => {
        function DescriptionForm() {
            const form = useForm({ defaultValues: { test: '' } });
            return (
                <Form {...form}>
                    <FormField name="test">
                        <FormItem>
                            <FormLabel>Test</FormLabel>
                            <FormDescription data-testid="description">Help text</FormDescription>
                        </FormItem>
                    </FormField>
                </Form>
            );
        }
        render(<DescriptionForm />);
        const description = screen.getByTestId('description');
        expect(description).toHaveAttribute('id');
        expect(description.id).toContain('form-item-description');
    });
});

describe('FormMessage', () => {
    it('does not render when no error', () => {
        render(<TestForm />);
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('renders error message from validation', async () => {
        const user = userEvent.setup();
        render(<TestForm />);

        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            const alerts = screen.getAllByRole('alert');
            expect(alerts.length).toBeGreaterThan(0);
        });
    });

    it('renders children when no error exists', () => {
        function MessageWithChildren() {
            const form = useForm({ defaultValues: { test: '' } });
            return (
                <Form {...form}>
                    <FormField name="test">
                        <FormItem>
                            <FormLabel>Test</FormLabel>
                            <FormMessage>Fallback message</FormMessage>
                        </FormItem>
                    </FormField>
                </Form>
            );
        }
        render(<MessageWithChildren />);
        expect(screen.getByRole('alert')).toHaveTextContent('Fallback message');
    });

    it('renders with role="alert" attribute', () => {
        function MessageWithChildren() {
            const form = useForm({ defaultValues: { test: '' } });
            return (
                <Form {...form}>
                    <FormField name="test">
                        <FormItem>
                            <FormLabel>Test</FormLabel>
                            <FormMessage>Error message</FormMessage>
                        </FormItem>
                    </FormField>
                </Form>
            );
        }
        render(<MessageWithChildren />);
        const message = screen.getByRole('alert');
        expect(message).toHaveAttribute('role', 'alert');
    });
});

describe('useFormField', () => {
    it('throws error when used outside FormField', () => {
        function InvalidComponent() {
            useFormField();
            return null;
        }

        expect(() => render(<InvalidComponent />)).toThrow('useFormField must be used within a FormField');
    });

    it('provides field state', async () => {
        const user = userEvent.setup();
        let fieldState: ReturnType<typeof useFormField> | undefined;

        function FieldStateReader() {
            fieldState = useFormField();
            return <span data-testid="field-state">{fieldState.name}</span>;
        }

        function StateForm() {
            const form = useForm({
                resolver: zodResolver(z.object({ test: z.string().min(1) })),
                defaultValues: { test: '' },
            });
            return (
                <Form {...form}>
                    <form onSubmit={form.handleSubmit(() => {})}>
                        <FormField name="test">
                            <FormItem>
                                <FieldStateReader />
                                <input {...form.register('test')} aria-label="Test" />
                            </FormItem>
                        </FormField>
                        <button type="submit">Submit</button>
                    </form>
                </Form>
            );
        }

        render(<StateForm />);
        expect(screen.getByTestId('field-state')).toHaveTextContent('test');

        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(fieldState?.invalid).toBe(true);
        });
    });
});

describe('Zod integration', () => {
    it('validates complex schema', async () => {
        const user = userEvent.setup();
        const complexSchema = z
            .object({
                password: z.string().min(8, 'Password must be at least 8 characters'),
                confirmPassword: z.string(),
            })
            .refine((data) => data.password === data.confirmPassword, {
                message: 'Passwords do not match',
                path: ['confirmPassword'],
            });

        type ComplexFormData = z.infer<typeof complexSchema>;

        function ComplexForm() {
            const form = useForm<ComplexFormData>({
                resolver: zodResolver(complexSchema),
                defaultValues: { password: '', confirmPassword: '' },
            });

            return (
                <Form {...form}>
                    <form onSubmit={form.handleSubmit(() => {})}>
                        <FormField name="password">
                            <FormItem>
                                <FormLabel>Password</FormLabel>
                                <FormControl>
                                    <Input type="password" {...form.register('password')} />
                                </FormControl>
                                <FormMessage />
                            </FormItem>
                        </FormField>
                        <FormField name="confirmPassword">
                            <FormItem>
                                <FormLabel>Confirm Password</FormLabel>
                                <FormControl>
                                    <Input type="password" {...form.register('confirmPassword')} />
                                </FormControl>
                                <FormMessage />
                            </FormItem>
                        </FormField>
                        <button type="submit">Submit</button>
                    </form>
                </Form>
            );
        }

        render(<ComplexForm />);

        await user.type(screen.getByLabelText('Password'), 'password123');
        await user.type(screen.getByLabelText('Confirm Password'), 'different');
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(screen.getByRole('alert')).toHaveTextContent('Passwords do not match');
        });
    });

    it('validates optional fields correctly', async () => {
        const user = userEvent.setup();
        const handleSubmit = vi.fn();
        const optionalSchema = z.object({
            name: z.string().min(1, 'Name is required'),
            nickname: z.string().optional(),
        });

        type OptionalFormData = z.infer<typeof optionalSchema>;

        function OptionalForm() {
            const form = useForm<OptionalFormData>({
                resolver: zodResolver(optionalSchema),
                defaultValues: { name: '', nickname: '' },
            });

            return (
                <Form {...form}>
                    <form onSubmit={form.handleSubmit(handleSubmit)}>
                        <FormField name="name">
                            <FormItem>
                                <FormLabel>Name</FormLabel>
                                <FormControl>
                                    <Input {...form.register('name')} />
                                </FormControl>
                                <FormMessage />
                            </FormItem>
                        </FormField>
                        <FormField name="nickname">
                            <FormItem>
                                <FormLabel>Nickname</FormLabel>
                                <FormControl>
                                    <Input {...form.register('nickname')} />
                                </FormControl>
                                <FormMessage />
                            </FormItem>
                        </FormField>
                        <button type="submit">Submit</button>
                    </form>
                </Form>
            );
        }

        render(<OptionalForm />);

        await user.type(screen.getByLabelText('Name'), 'John');
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(handleSubmit).toHaveBeenCalledWith({ name: 'John', nickname: '' }, expect.anything());
        });
    });
});

describe('FormControl', () => {
    it('passes id to child element', () => {
        function IdTestForm() {
            const form = useForm({ defaultValues: { test: '' } });
            return (
                <Form {...form}>
                    <FormField name="test">
                        <FormItem>
                            <FormLabel>Test</FormLabel>
                            <FormControl>
                                <Input {...form.register('test')} />
                            </FormControl>
                        </FormItem>
                    </FormField>
                </Form>
            );
        }
        render(<IdTestForm />);
        const input = screen.getByLabelText('Test');
        expect(input).toHaveAttribute('id');
        expect(input.id).toContain('form-item');
    });

    it('passes aria-invalid when error exists', async () => {
        const user = userEvent.setup();
        const errorSchema = z.object({ test: z.string().min(1, 'Required') });

        function ErrorTestForm() {
            const form = useForm({
                resolver: zodResolver(errorSchema),
                defaultValues: { test: '' },
            });
            return (
                <Form {...form}>
                    <form onSubmit={form.handleSubmit(() => {})}>
                        <FormField name="test">
                            <FormItem>
                                <FormLabel>Test</FormLabel>
                                <FormControl>
                                    <Input {...form.register('test')} />
                                </FormControl>
                                <FormMessage />
                            </FormItem>
                        </FormField>
                        <button type="submit">Submit</button>
                    </form>
                </Form>
            );
        }

        render(<ErrorTestForm />);
        await user.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(screen.getByLabelText('Test')).toHaveAttribute('aria-invalid', 'true');
        });
    });

    it('passes aria-describedby linking to description', () => {
        function DescribedByTestForm() {
            const form = useForm({ defaultValues: { test: '' } });
            return (
                <Form {...form}>
                    <FormField name="test">
                        <FormItem>
                            <FormLabel>Test</FormLabel>
                            <FormControl>
                                <Input {...form.register('test')} />
                            </FormControl>
                            <FormDescription>Help text</FormDescription>
                        </FormItem>
                    </FormField>
                </Form>
            );
        }
        render(<DescribedByTestForm />);
        const input = screen.getByLabelText('Test');
        const description = screen.getByText('Help text');
        expect(input).toHaveAttribute('aria-describedby', description.id);
    });
});
