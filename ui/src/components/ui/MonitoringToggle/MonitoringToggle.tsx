import { useRef, type ComponentType } from 'react';
import { EyeIcon, EyeSlashIcon, TreeViewIcon, type IconProps } from '@phosphor-icons/react';

import { Tooltip } from '@/components/ui/Tooltip';

import './MonitoringToggle.css';

export interface MonitoringToggleProps {
    value: boolean | null;

    onChange: (value: boolean | null) => void;

    disabled?: boolean;

    id?: string;

    'aria-label'?: string;

    'data-field'?: string;

    testIdPrefix?: string;
}

interface MonitoringOption {
    value: boolean | null;
    key: string;
    label: string;
    icon: ComponentType<IconProps>;
    tooltip: string;
}

const OPTIONS: MonitoringOption[] = [
    {
        value: true,
        key: 'true',
        label: 'Monitor',
        icon: EyeIcon,
        tooltip: 'Monitor this item. The system searches and grabs for it',
    },
    {
        value: false,
        key: 'false',
        label: 'Do not monitor',
        icon: EyeSlashIcon,
        tooltip: 'Ignore this item. Background jobs will not pick it up',
    },
    {
        value: null,
        key: 'inherit',
        label: 'Inherit',
        icon: TreeViewIcon,
        tooltip: 'Inherit monitoring from a parent (no explicit decision)',
    },
];

/** Renders the Monitor / Do not monitor / Inherit radiogroup used everywhere monitoring is set. */
export function MonitoringToggle({
    value,
    onChange,
    disabled = false,
    id,
    'aria-label': ariaLabel = 'Monitoring override',
    'data-field': dataField,
    testIdPrefix,
}: MonitoringToggleProps) {
    const optionRefs = useRef<(HTMLButtonElement | null)[]>([]);

    return (
        <div
            id={id}
            className="monitoring-toggle__segments"
            role="radiogroup"
            aria-label={ariaLabel}
            data-monitoring={value === null ? 'inherit' : value}
            data-component="monitoring-toggle"
            data-field={dataField}
            data-testid={testIdPrefix ? `${testIdPrefix}__toggle` : undefined}
        >
            {OPTIONS.map(({ value: optionValue, key, label, icon: Icon, tooltip }, index) => {
                const isActive = value === optionValue;
                return (
                    <Tooltip key={key} content={tooltip}>
                        <button
                            ref={(node) => {
                                optionRefs.current[index] = node;
                            }}
                            type="button"
                            role="radio"
                            aria-checked={isActive}
                            tabIndex={isActive ? 0 : -1}
                            onKeyDown={(event) => {
                                const direction =
                                    event.key === 'ArrowRight' || event.key === 'ArrowDown'
                                        ? 1
                                        : event.key === 'ArrowLeft' || event.key === 'ArrowUp'
                                          ? -1
                                          : 0;
                                if (!direction) return;
                                event.preventDefault();
                                const next = (index + direction + OPTIONS.length) % OPTIONS.length;
                                onChange(OPTIONS[next].value);
                                optionRefs.current[next]?.focus();
                            }}
                            aria-label={`${label}: ${tooltip}`}
                            className="monitoring-toggle__option"
                            data-state={isActive ? 'active' : 'inactive'}
                            data-value={key}
                            data-testid={testIdPrefix ? `${testIdPrefix}__option-${key}` : undefined}
                            onClick={() => onChange(optionValue)}
                            disabled={disabled}
                        >
                            <Icon weight="bold" aria-hidden="true" />
                            <span>{label}</span>
                        </button>
                    </Tooltip>
                );
            })}
        </div>
    );
}

MonitoringToggle.displayName = 'MonitoringToggle';
