import { useId } from 'react';

import { Label } from '@/components/ui/Label';
import { MonitoringToggle } from '@/components/ui/MonitoringToggle';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { contentFieldRegistry, getScalarField } from '@/editor/field-registry';
import { readContentFilter, setContentFilter } from '@/lib/contentColumnFilters';
import { getFulfillmentBadge, mapFulfillmentStatus, type ApiFulfillmentStatus } from '@/types';

export function LocalContentFilters({
    value,
    onChange,
    disabled,
}: {
    value: string;
    onChange: (value: string) => void;
    disabled: boolean;
}) {
    const id = useId();
    const monitoring = readContentFilter(value, 'monitoring');
    const fulfillment = readContentFilter(value, 'fulfillment');
    const options = getScalarField(contentFieldRegistry, 'fulfillment')?.enumValues ?? [];
    return (
        <>
            <div className="simple-filter-builder__field">
                <Label htmlFor={`${id}-monitoring`}>Monitoring</Label>
                <TooltipProvider>
                    <MonitoringToggle
                        id={`${id}-monitoring`}
                        filter
                        aria-label="Monitoring filter"
                        shotgun
                        value={monitoring === 'shotgun' ? 'Shotgun' : monitoring ? monitoring === 'true' : null}
                        onChange={(next) =>
                            onChange(
                                setContentFilter(
                                    value,
                                    'monitoring',
                                    next === null ? '' : next === 'Shotgun' ? 'shotgun' : String(next),
                                ),
                            )
                        }
                        disabled={disabled}
                    />
                </TooltipProvider>
            </div>
            <div className="simple-filter-builder__field">
                <Label htmlFor={`${id}-status`}>Status</Label>
                <Select
                    value={fulfillment || '__any__'}
                    onValueChange={(next) =>
                        onChange(setContentFilter(value, 'fulfillment', next === '__any__' ? '' : next))
                    }
                    disabled={disabled}
                >
                    <SelectTrigger id={`${id}-status`} size="sm" aria-label="Status filter">
                        {fulfillment
                            ? getFulfillmentBadge(mapFulfillmentStatus(fulfillment as ApiFulfillmentStatus)).label
                            : 'Any status'}
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="__any__">Any status</SelectItem>
                        {options.map((option) => (
                            <SelectItem key={option} value={option}>
                                {getFulfillmentBadge(mapFulfillmentStatus(option as ApiFulfillmentStatus)).label}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>
            </div>
        </>
    );
}
