import { forwardRef, useState, useCallback, type InputHTMLAttributes, type ReactNode } from 'react';
import { FolderOpenIcon } from '@phosphor-icons/react';

import { useAuth, DisabledTooltip, type Permission } from '@/auth';
import { Button } from '@/components/ui/Button';
import { Input, type InputSize } from '@/components/ui/Input';
import { FileBrowser } from '@/components/configuration/FileBrowser';

import './PathInput.css';

export type PathInputMode = 'file' | 'folder' | 'both';

export interface PathInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size' | 'onChange'> {
    size?: InputSize;

    error?: boolean;

    errorMessage?: string;

    mode?: PathInputMode;

    value?: string;

    onChange?: (value: string) => void;

    browseEnabled?: boolean;

    browseLabel?: string;

    browserInitialPath?: string;

    browserEndpoint?: string;

    browsePermission?: Permission;

    leadingIcon?: ReactNode;
}

export const PathInput = forwardRef<HTMLInputElement, PathInputProps>(
    (
        {
            size = 'md',
            error = false,
            errorMessage,
            mode = 'folder',
            value = '',
            onChange,
            browseEnabled = true,
            browseLabel,
            browserInitialPath,
            browserEndpoint,
            browsePermission = 'files.browse',
            leadingIcon,
            disabled,
            id,
            placeholder,
            className,
            ...props
        },
        ref,
    ) => {
        const [browserOpen, setBrowserOpen] = useState(false);
        const { hasPermission } = useAuth();
        const canBrowse = browseEnabled && hasPermission(browsePermission);

        const handleInputChange = useCallback(
            (e: React.ChangeEvent<HTMLInputElement>) => {
                onChange?.(e.target.value);
            },
            [onChange],
        );

        const handleBrowseSelect = useCallback(
            (selectedPath: string) => {
                onChange?.(selectedPath);
                setBrowserOpen(false);
            },
            [onChange],
        );

        const handleBrowseClick = useCallback(() => {
            setBrowserOpen(true);
        }, []);

        const classes = ['path-input', className].filter(Boolean).join(' ');

        const defaultPlaceholder =
            mode === 'file' ? '/path/to/file' : mode === 'folder' ? '/path/to/folder' : '/path/to/file-or-folder';

        return (
            <div className={classes} data-component="path-input">
                <div className="path-input__wrapper">
                    <Input
                        ref={ref}
                        id={id}
                        size={size}
                        value={value}
                        onChange={handleInputChange}
                        error={error}
                        errorMessage={errorMessage}
                        disabled={disabled}
                        placeholder={placeholder ?? defaultPlaceholder}
                        leadingIcon={leadingIcon}
                        className="path-input__input"
                        {...props}
                    />
                    {browseEnabled && (
                        <DisabledTooltip action={browsePermission}>
                            <Button
                                type="button"
                                variant="secondary"
                                size={size}
                                onClick={handleBrowseClick}
                                disabled={disabled || !canBrowse}
                                aria-label={browseLabel ?? 'Browse filesystem'}
                                className="path-input__browse"
                                data-action="browse"
                            >
                                <FolderOpenIcon weight="bold" aria-hidden="true" />
                                {browseLabel ?? 'Browse'}
                            </Button>
                        </DisabledTooltip>
                    )}
                </div>
                <FileBrowser
                    open={browserOpen}
                    onOpenChange={setBrowserOpen}
                    onSelect={handleBrowseSelect}
                    mode={mode}
                    initialPath={browserInitialPath ?? value}
                    endpoint={browserEndpoint}
                />
            </div>
        );
    },
);

PathInput.displayName = 'PathInput';

export default PathInput;
