import { forwardRef, type ComponentPropsWithoutRef, useCallback, useState, useEffect, useRef } from 'react';
import { MagnifyingGlass, X } from '@phosphor-icons/react';

import { Input } from '../Input';
import { Button } from '../Button';

import './ContentSearch.css';

export interface ContentSearchProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    value: string;

    onChange: (value: string) => void;

    placeholder?: string;

    loading?: boolean;

    debounceMs?: number;
}

export const ContentSearch = forwardRef<HTMLDivElement, ContentSearchProps>(
    (
        { value, onChange, placeholder = 'Search by title...', loading = false, debounceMs = 300, className, ...props },
        ref,
    ) => {
        const [inputValue, setInputValue] = useState(value);
        const debounceTimeoutRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

        useEffect(() => {
            setInputValue(value);
        }, [value]);

        const handleChange = useCallback(
            (e: React.ChangeEvent<HTMLInputElement>) => {
                const newValue = e.target.value;
                setInputValue(newValue);

                if (debounceTimeoutRef.current) {
                    clearTimeout(debounceTimeoutRef.current);
                }

                debounceTimeoutRef.current = setTimeout(() => {
                    onChange(newValue);
                }, debounceMs);
            },
            [onChange, debounceMs],
        );

        const handleClear = useCallback(() => {
            setInputValue('');
            onChange('');
            if (debounceTimeoutRef.current) {
                clearTimeout(debounceTimeoutRef.current);
            }
        }, [onChange]);

        useEffect(() => {
            return () => {
                if (debounceTimeoutRef.current) {
                    clearTimeout(debounceTimeoutRef.current);
                }
            };
        }, []);

        const classes = ['content-search', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-component="content-search" {...props}>
                <Input
                    type="search"
                    value={inputValue}
                    onChange={handleChange}
                    placeholder={placeholder}
                    disabled={loading}
                    size="sm"
                    leadingIcon={<MagnifyingGlass weight="regular" />}
                    trailingIcon={
                        inputValue ? (
                            <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                onClick={handleClear}
                                className="content-search__clear"
                                aria-label="Clear search"
                                data-action="clear-search"
                            >
                                <X weight="bold" />
                            </Button>
                        ) : undefined
                    }
                    aria-label="Search content by title"
                    data-search="input"
                />
            </div>
        );
    },
);

ContentSearch.displayName = 'ContentSearch';

export default ContentSearch;
