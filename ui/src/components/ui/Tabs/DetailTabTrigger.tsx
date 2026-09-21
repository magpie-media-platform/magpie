import { type ReactNode } from 'react';

import { Tooltip } from '@/components/ui/Tooltip';

import { TabsTrigger } from './Tabs';

export interface DetailTabTriggerProps {
    value: string;
    label: string;
    icon?: ReactNode;
    count?: number;
    [dataAttribute: `data-${string}`]: string | undefined;
}

export function DetailTabTrigger({ value, label, icon, count, ...dataProps }: DetailTabTriggerProps) {
    return (
        <TabsTrigger value={value} aria-label={count === undefined ? label : `${label} (${count})`} {...dataProps}>
            <Tooltip content={label}>
                <span className="detail-tab-trigger__anchor">
                    {icon}
                    <span className="detail-tab-trigger__label">{label}</span>
                </span>
            </Tooltip>
            {count !== undefined && <span className="tab-count">{count}</span>}
        </TabsTrigger>
    );
}

DetailTabTrigger.displayName = 'DetailTabTrigger';

export default DetailTabTrigger;
