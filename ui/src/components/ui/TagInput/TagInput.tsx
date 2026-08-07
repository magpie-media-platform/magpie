import {
    useCallback,
    useId,
    useLayoutEffect,
    useMemo,
    useRef,
    useState,
    type KeyboardEvent,
    type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';
import { XIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';

import './TagInput.css';

interface AnchorRect {
    top: number;
    left: number;
    width: number;
}

export interface TagInputProps {
    value: string[];

    onChange: (next: string[]) => void;

    suggestions?: string[];

    allowCreate?: boolean;

    placeholder?: string;

    label?: string;

    'aria-label'?: string;

    'aria-describedby'?: string;

    'aria-invalid'?: boolean;

    id?: string;

    disabled?: boolean;

    autoFocus?: boolean;
}

const EMPTY_SUGGESTIONS: string[] = [];

export function TagInput({
    value,
    onChange,
    suggestions = EMPTY_SUGGESTIONS,
    allowCreate = true,
    placeholder,
    label,
    'aria-label': ariaLabel,
    'aria-describedby': ariaDescribedBy,
    'aria-invalid': ariaInvalid,
    id,
    disabled = false,
    autoFocus = false,
}: TagInputProps): ReactNode {
    const generatedId = useId();
    const inputId = id ?? generatedId;
    const listboxId = `${inputId}-listbox`;

    const inputRef = useRef<HTMLInputElement>(null);
    const boxRef = useRef<HTMLDivElement>(null);
    const [text, setText] = useState('');
    const [open, setOpen] = useState(false);
    const [activeIndex, setActiveIndex] = useState(-1);
    const [anchorRect, setAnchorRect] = useState<AnchorRect | null>(null);

    const normalizedValue = useMemo(() => new Set(value.map((v) => v.toLowerCase())), [value]);

    const filteredSuggestions = useMemo(() => {
        const query = text.trim().toLowerCase();
        return suggestions.filter(
            (s) => !normalizedValue.has(s.toLowerCase()) && (query === '' || s.toLowerCase().includes(query)),
        );
    }, [suggestions, normalizedValue, text]);

    const trimmedText = text.trim();
    const canCreate =
        allowCreate &&
        trimmedText.length > 0 &&
        !normalizedValue.has(trimmedText.toLowerCase()) &&
        !filteredSuggestions.some((s) => s.toLowerCase() === trimmedText.toLowerCase());

    const options = useMemo<Array<{ kind: 'suggestion' | 'create'; value: string }>>(() => {
        const rows: Array<{ kind: 'suggestion' | 'create'; value: string }> = filteredSuggestions.map((label) => ({
            kind: 'suggestion',
            value: label,
        }));
        if (canCreate) {
            rows.push({ kind: 'create', value: trimmedText });
        }
        return rows;
    }, [filteredSuggestions, canCreate, trimmedText]);

    const commit = useCallback(
        (raw: string) => {
            const next = raw.trim();
            if (!next || normalizedValue.has(next.toLowerCase())) {
                return;
            }
            onChange([...value, next]);
            setText('');
            setActiveIndex(-1);
        },
        [normalizedValue, onChange, value],
    );

    const remove = useCallback(
        (target: string) => {
            onChange(value.filter((v) => v !== target));
        },
        [onChange, value],
    );

    const handleTextChange = useCallback((next: string) => {
        setText(next);
        setOpen(true);
        setActiveIndex(-1);
    }, []);

    const handleKeyDown = useCallback(
        (e: KeyboardEvent<HTMLInputElement>) => {
            if (e.key === 'Enter' || e.key === ',') {
                e.preventDefault();
                if (open && activeIndex >= 0 && activeIndex < options.length) {
                    commit(options[activeIndex].value);
                } else if (trimmedText) {
                    if (allowCreate || suggestions.some((s) => s.toLowerCase() === trimmedText.toLowerCase())) {
                        commit(trimmedText);
                    }
                }
                return;
            }

            if (e.key === 'Backspace' && text === '' && value.length > 0) {
                e.preventDefault();
                remove(value[value.length - 1]);
                return;
            }

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (options.length === 0) {
                    return;
                }
                setOpen(true);
                setActiveIndex((prev) => (prev + 1) % options.length);
                return;
            }

            if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (options.length === 0) {
                    return;
                }
                setOpen(true);
                setActiveIndex((prev) => (prev <= 0 ? options.length - 1 : prev - 1));
                return;
            }

            if (e.key === 'Escape') {
                if (open) {
                    e.preventDefault();
                    setOpen(false);
                    setActiveIndex(-1);
                }
            }
        },
        [open, activeIndex, options, commit, trimmedText, allowCreate, suggestions, text, value, remove],
    );

    const showListbox = open && !disabled && options.length > 0;

    useLayoutEffect(() => {
        if (!showListbox) {
            return;
        }
        const measure = () => {
            const box = boxRef.current;
            if (!box) {
                return;
            }
            const rect = box.getBoundingClientRect();
            setAnchorRect({ top: rect.bottom, left: rect.left, width: rect.width });
        };
        measure();
        window.addEventListener('scroll', measure, true);
        window.addEventListener('resize', measure);
        return () => {
            window.removeEventListener('scroll', measure, true);
            window.removeEventListener('resize', measure);
        };
    }, [showListbox]);

    return (
        <div className="tag-input" data-component="tag-input">
            <div className="tag-input__control">
                <div
                    ref={boxRef}
                    className={`tag-input__box${disabled ? ' tag-input__box--disabled' : ''}`}
                    data-testid="tag-input__box"
                    onMouseDown={(e) => {
                        if (disabled || e.target === inputRef.current) {
                            return;
                        }
                        e.preventDefault();
                        inputRef.current?.focus();
                    }}
                >
                    {value.map((tag) => (
                        <Badge key={tag} variant="default" size="sm" className="tag-input__chip">
                            {tag}
                            {!disabled && (
                                <button
                                    type="button"
                                    className="tag-input__chip-remove"
                                    onClick={() => remove(tag)}
                                    aria-label={`Remove ${tag}`}
                                >
                                    <XIcon weight="bold" aria-hidden="true" />
                                </button>
                            )}
                        </Badge>
                    ))}
                    <input
                        ref={inputRef}
                        id={inputId}
                        className="tag-input__field"
                        type="text"
                        role="combobox"
                        aria-expanded={showListbox}
                        aria-controls={listboxId}
                        aria-autocomplete="list"
                        aria-activedescendant={
                            showListbox && activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined
                        }
                        aria-label={label ?? ariaLabel}
                        aria-describedby={ariaDescribedBy}
                        aria-invalid={ariaInvalid}
                        value={text}
                        placeholder={placeholder}
                        disabled={disabled}
                        autoFocus={autoFocus}
                        autoComplete="off"
                        onChange={(e) => handleTextChange(e.target.value)}
                        onKeyDown={handleKeyDown}
                        onFocus={() => setOpen(true)}
                        onBlur={() => setOpen(false)}
                    />
                </div>

                {showListbox &&
                    anchorRect &&
                    createPortal(
                        <ul
                            className="tag-input__listbox"
                            id={listboxId}
                            role="listbox"
                            onPointerDown={(e) => e.stopPropagation()}
                            style={{
                                position: 'fixed',
                                top: anchorRect.top,
                                left: anchorRect.left,
                                width: anchorRect.width,
                            }}
                        >
                            {options.map((option, index) => (
                                <li
                                    key={`${option.kind}:${option.value}`}
                                    id={`${listboxId}-option-${index}`}
                                    role="option"
                                    aria-selected={index === activeIndex}
                                    className={`tag-input__option${index === activeIndex ? ' tag-input__option--active' : ''}`}
                                    onMouseDown={(e) => {
                                        e.preventDefault();
                                        commit(option.value);
                                        inputRef.current?.focus();
                                    }}
                                    onMouseEnter={() => setActiveIndex(index)}
                                >
                                    {option.kind === 'create' ? (
                                        <span className="tag-input__option-create">
                                            Add &ldquo;{option.value}&rdquo;
                                        </span>
                                    ) : (
                                        option.value
                                    )}
                                </li>
                            ))}
                        </ul>,
                        document.body,
                    )}
            </div>
        </div>
    );
}

TagInput.displayName = 'TagInput';

export default TagInput;
