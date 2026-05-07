import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode, type RefObject } from 'react';
import { FloppyDisk, X } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Role, RoleName } from '@/auth';

import './UserForm.css';

export interface UserFormValues {
    id?: string;
    username: string;
    role: Role;
    enabled: boolean;
    password?: string;
}

interface UserFormProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    initialValues: UserFormValues | null;
    onSubmit: (values: UserFormValues) => void;
    isSubmitting?: boolean;
    returnFocusRef?: RefObject<HTMLElement | null>;
}

const defaultValues: UserFormValues = {
    username: '',
    role: Role.Viewer,
    enabled: true,
};

export function UserForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting = false,
    returnFocusRef,
}: UserFormProps): ReactNode {
    const isEditing = Boolean(initialValues?.id);
    const title = isEditing ? 'Edit User' : 'Add User';

    const [values, setValues] = useState<UserFormValues>(initialValues ?? defaultValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<UserFormValues>(initialValues ?? defaultValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    useEffect(() => {
        if (open) {
            const clean = initialValues ?? defaultValues;
            setValues(clean);
            cleanValuesRef.current = clean;
            setErrors({});
            setShowUnsavedDialog(false);
        }
    }, [open, initialValues]);

    const isDirty = useMemo(
        () =>
            values.username !== cleanValuesRef.current.username ||
            values.role !== cleanValuesRef.current.role ||
            values.enabled !== cleanValuesRef.current.enabled ||
            values.password !== cleanValuesRef.current.password,
        [values],
    );

    useUnsavedChangesGuard(open && isDirty);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, onOpenChange],
    );

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleChange = useCallback(<K extends keyof UserFormValues>(key: K, value: UserFormValues[K]) => {
        setValues((prev) => ({ ...prev, [key]: value }));
        setErrors((prev) => {
            const next = { ...prev, [key]: '' };
            if (key === 'username' && !(value as string).trim()) {
                next.username = 'Username is required';
            } else if (key === 'password') {
                const pw = value as string | undefined;
                if (pw && pw.length < 8) {
                    next.password = 'Password must be at least 8 characters';
                }
            }
            return next;
        });
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.username.trim()) {
            newErrors.username = 'Username is required';
        }

        if (!isEditing) {
            if (!values.password || values.password.length < 8) {
                newErrors.password = 'Password must be at least 8 characters';
            }
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [values, isEditing]);

    const handleSubmit = useCallback(
        (e: React.FormEvent) => {
            e.preventDefault();
            if (validate()) {
                onSubmit(values);
            }
        },
        [values, validate, onSubmit],
    );

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent
                side="right"
                className="user-form__sheet"
                data-component="user-form"
                onCloseAutoFocus={(e) => {
                    if (returnFocusRef?.current) {
                        e.preventDefault();
                        returnFocusRef.current.focus();
                    }
                }}
            >
                <SheetHeader>
                    <SheetTitle>{title}</SheetTitle>
                    <SheetDescription>
                        {isEditing ? 'Update user account settings.' : 'Create a new user account.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="user-form">
                    <div className="user-form__body">
                        <div className="user-form__field">
                            <Label htmlFor="user-username" error={!!errors.username} required>
                                Username
                            </Label>
                            <Input
                                id="user-username"
                                type="text"
                                value={values.username}
                                onChange={(e) => handleChange('username', e.target.value)}
                                placeholder="Username"
                                error={!!errors.username}
                                errorMessage={errors.username || undefined}
                                disabled={isSubmitting || isEditing}
                                autoComplete="username"
                                data-field="username"
                            />
                            {isEditing && (
                                <p className="user-form__help">Usernames cannot be changed after creation.</p>
                            )}
                        </div>

                        <div className="user-form__field">
                            <Label htmlFor="user-role">Role</Label>
                            <Select
                                value={String(values.role)}
                                onValueChange={(v) => handleChange('role', Number(v) as Role)}
                                disabled={isSubmitting}
                            >
                                <SelectTrigger id="user-role" data-field="role">
                                    {RoleName[values.role]}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={String(Role.Viewer)}>{RoleName[Role.Viewer]}</SelectItem>
                                    <SelectItem value={String(Role.Requester)}>{RoleName[Role.Requester]}</SelectItem>
                                    <SelectItem value={String(Role.Manager)}>{RoleName[Role.Manager]}</SelectItem>
                                    <SelectItem value={String(Role.Admin)}>{RoleName[Role.Admin]}</SelectItem>
                                </SelectContent>
                            </Select>
                        </div>

                        <div className="user-form__field user-form__field--inline">
                            <Label htmlFor="user-enabled">Enabled</Label>
                            <Switch
                                id="user-enabled"
                                checked={values.enabled}
                                onCheckedChange={(checked) => handleChange('enabled', checked)}
                                aria-label="User enabled"
                                disabled={isSubmitting}
                            />
                        </div>

                        {!isEditing && (
                            <div className="user-form__field">
                                <Label htmlFor="user-password" error={!!errors.password}>
                                    Password
                                </Label>
                                <Input
                                    id="user-password"
                                    type="password"
                                    value={values.password ?? ''}
                                    onChange={(e) => handleChange('password', e.target.value || undefined)}
                                    placeholder="Password"
                                    error={!!errors.password}
                                    errorMessage={errors.password || undefined}
                                    disabled={isSubmitting}
                                    autoComplete="new-password"
                                    data-field="password"
                                />
                                <p className="user-form__help">Minimum 8 characters</p>
                            </div>
                        )}
                    </div>

                    <SheetFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            disabled={isSubmitting}
                            onClick={handleCancel}
                            data-action="cancel"
                        >
                            <X weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button
                            type="submit"
                            variant="primary"
                            loading={isSubmitting}
                            data-action={isEditing ? 'save' : 'create'}
                        >
                            <FloppyDisk weight="bold" aria-hidden="true" />
                            {isEditing ? 'Save' : 'Create'}
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
        </Sheet>
    );
}

UserForm.displayName = 'UserForm';
