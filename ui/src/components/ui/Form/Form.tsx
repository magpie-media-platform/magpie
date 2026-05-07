/* eslint-disable react-refresh/only-export-components */
import {
    Children,
    cloneElement,
    createContext,
    forwardRef,
    isValidElement,
    useContext,
    useId,
    useMemo,
    type ComponentPropsWithoutRef,
    type ReactElement,
    type ReactNode,
} from 'react';
import { FormProvider, useFormContext, useFormState } from 'react-hook-form';

import { Label, type LabelProps } from '../Label';

import './Form.css';

interface FormFieldContextValue {
    id: string;
    name: string;
    formItemId: string;
    formDescriptionId: string;
    formMessageId: string;
}

const FormFieldContext = createContext<FormFieldContextValue | null>(null);

function useFormField(): FormFieldContextValue & {
    invalid: boolean;
    isDirty: boolean;
    isTouched: boolean;
    isValidating: boolean;
    error?: { message?: string };
} {
    const fieldContext = useContext(FormFieldContext);

    if (!fieldContext) {
        throw new Error('useFormField must be used within a FormField');
    }

    const { control } = useFormContext();
    const { errors, dirtyFields, touchedFields, isValidating } = useFormState({
        control,
        name: fieldContext.name,
    });

    const error = errors[fieldContext.name] as { message?: string } | undefined;
    const isDirty = !!dirtyFields[fieldContext.name];
    const isTouched = !!touchedFields[fieldContext.name];

    return {
        ...fieldContext,
        invalid: !!error,
        isDirty,
        isTouched,
        isValidating,
        error,
    };
}

const Form = FormProvider;

interface FormFieldProps {
    name: string;
    children: ReactNode;
}

function FormField({ name, children }: FormFieldProps): ReactNode {
    const id = useId();

    const contextValue = useMemo(
        (): FormFieldContextValue => ({
            id,
            name,
            formItemId: `${id}-form-item`,
            formDescriptionId: `${id}-form-item-description`,
            formMessageId: `${id}-form-item-message`,
        }),
        [id, name],
    );

    return <FormFieldContext.Provider value={contextValue}>{children}</FormFieldContext.Provider>;
}

interface FormItemProps extends ComponentPropsWithoutRef<'div'> {
    children: ReactNode;
}

const FormItem = forwardRef<HTMLDivElement, FormItemProps>(({ className, children, ...props }, ref) => {
    const classes = ['form-item', className].filter(Boolean).join(' ');

    return (
        <div ref={ref} className={classes} data-component="form-item" {...props}>
            {children}
        </div>
    );
});

FormItem.displayName = 'FormItem';

interface FormLabelProps extends Omit<LabelProps, 'error'> {
    children: ReactNode;
}

const FormLabel = forwardRef<HTMLLabelElement, FormLabelProps>(({ className, ...props }, ref) => {
    const { formItemId, error } = useFormField();
    const classes = ['form-label', className].filter(Boolean).join(' ');

    return <Label ref={ref} className={classes} htmlFor={formItemId} error={!!error} {...props} />;
});

FormLabel.displayName = 'FormLabel';

interface FormControlProps {
    children: ReactNode;
}

function FormControl({ children }: FormControlProps): ReactNode {
    const { formItemId, formDescriptionId, formMessageId, error } = useFormField();

    const describedBy = [formDescriptionId, error ? formMessageId : undefined].filter(Boolean).join(' ') || undefined;

    const child = Children.only(children);

    if (!isValidElement(child)) {
        return children;
    }

    return cloneElement(child as ReactElement<Record<string, unknown>>, {
        id: formItemId,
        'aria-describedby': describedBy,
        'aria-invalid': !!error,
        error: !!error,
    });
}

interface FormDescriptionProps extends ComponentPropsWithoutRef<'p'> {
    children: ReactNode;
}

const FormDescription = forwardRef<HTMLParagraphElement, FormDescriptionProps>(
    ({ className, children, ...props }, ref) => {
        const { formDescriptionId } = useFormField();
        const classes = ['form-description', className].filter(Boolean).join(' ');

        return (
            <p ref={ref} id={formDescriptionId} className={classes} {...props}>
                {children}
            </p>
        );
    },
);

FormDescription.displayName = 'FormDescription';

interface FormMessageProps extends ComponentPropsWithoutRef<'p'> {
    children?: ReactNode;
}

const FormMessage = forwardRef<HTMLParagraphElement, FormMessageProps>(({ className, children, ...props }, ref) => {
    const { formMessageId, error } = useFormField();
    const body = error?.message ?? children;

    if (!body) {
        return null;
    }

    const classes = ['form-message', className].filter(Boolean).join(' ');

    return (
        <p ref={ref} id={formMessageId} className={classes} role="alert" {...props}>
            {body}
        </p>
    );
});

FormMessage.displayName = 'FormMessage';

export { Form, FormField, FormItem, FormLabel, FormControl, FormDescription, FormMessage, useFormField };

export type { FormFieldProps, FormItemProps, FormLabelProps, FormDescriptionProps, FormMessageProps };
