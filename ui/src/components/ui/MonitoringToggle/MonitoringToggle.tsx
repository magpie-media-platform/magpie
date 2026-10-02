import { useRef, type ComponentType } from 'react';
import { CrosshairIcon, EyeIcon, EyeSlashIcon, TreeViewIcon, FunnelIcon, type IconProps } from '@phosphor-icons/react';

import { Tooltip } from '@/components/ui/Tooltip';
import type { MonitoringMode } from '@/types';

import './MonitoringToggle.css';

/** A monitoring choice: a boolean override, a monitoring mode, or null. */
export type MonitoringValue = boolean | MonitoringMode | null;

export interface MonitoringToggleProps<T extends MonitoringValue = boolean | null> {
    value: T | undefined;

    onChange: (value: T) => void;

    disabled?: boolean;

    id?: string;

    'aria-label'?: string;

    'aria-labelledby'?: string;

    'data-field'?: string;

    testIdPrefix?: string;
    filter?: boolean;
    /** Offers the Unmonitored, Monitored, and Shotgun choices. */
    shotgun?: boolean;
    /** Offers the Inherit choice. */
    inherit?: boolean;
}

interface MonitoringOption {
    value: MonitoringValue;
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

const SHOTGUN_OPTIONS: MonitoringOption[] = [
    {
        value: null,
        key: 'inherit',
        label: 'Inherit',
        icon: TreeViewIcon,
        tooltip: 'Use the monitoring of the nearest parent with a choice',
    },
    {
        value: 'Unmonitored',
        key: 'false',
        label: 'Unmonitored',
        icon: EyeSlashIcon,
        tooltip: 'Ignore this item. Background jobs will not pick it up',
    },
    {
        value: 'Monitored',
        key: 'true',
        label: 'Monitored',
        icon: EyeIcon,
        tooltip: 'Search for this item and grab the best acceptable release',
    },
    {
        value: 'Shotgun',
        key: 'shotgun',
        label: 'Shotgun',
        icon: CrosshairIcon,
        tooltip: 'Grab every acceptable release and keep the first successful import',
    },
];

/** Renders a monitoring setting or filter as a segmented radio group. */
export function MonitoringToggle<T extends MonitoringValue = boolean | null>({
    value,
    onChange,
    disabled = false,
    id,
    'aria-label': ariaLabel = 'Monitoring override',
    'aria-labelledby': ariaLabelledBy,
    'data-field': dataField,
    testIdPrefix,
    filter = false,
    shotgun = false,
    inherit = true,
}: MonitoringToggleProps<T>) {
    const optionRefs = useRef<(HTMLButtonElement | null)[]>([]);
    const options: MonitoringOption[] = filter
        ? [
              { value: true, key: 'true', label: 'Monitored', icon: EyeIcon, tooltip: 'Show monitored content' },
              {
                  value: false,
                  key: 'false',
                  label: 'Unmonitored',
                  icon: EyeSlashIcon,
                  tooltip: 'Show unmonitored content',
              },
              ...(shotgun
                  ? [
                        {
                            value: 'Shotgun' as const,
                            key: 'shotgun',
                            label: 'Shotgun',
                            icon: CrosshairIcon,
                            tooltip: 'Show content that uses Shotgun mode',
                        },
                    ]
                  : []),
              { value: null, key: 'any', label: 'Any', icon: FunnelIcon, tooltip: 'Do not restrict monitoring' },
          ]
        : (shotgun ? SHOTGUN_OPTIONS : OPTIONS).filter((option) => inherit || option.value !== null);

    return (
        <div
            id={id}
            className="monitoring-toggle__segments"
            role="radiogroup"
            aria-label={ariaLabelledBy ? undefined : ariaLabel}
            aria-labelledby={ariaLabelledBy}
            data-monitoring={options.find((option) => option.value === value)?.key}
            data-component="monitoring-toggle"
            data-field={dataField}
            data-testid={testIdPrefix ? `${testIdPrefix}__toggle` : undefined}
        >
            {options.map(({ value: optionValue, key, label, icon: Icon, tooltip }, index) => {
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
                                const next = (index + direction + options.length) % options.length;
                                onChange(options[next].value as T);
                                optionRefs.current[next]?.focus();
                            }}
                            aria-label={`${label}: ${tooltip}`}
                            className="monitoring-toggle__option"
                            data-state={isActive ? 'active' : 'inactive'}
                            data-value={key}
                            data-testid={testIdPrefix ? `${testIdPrefix}__option-${key}` : undefined}
                            onClick={() => onChange(optionValue as T)}
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
