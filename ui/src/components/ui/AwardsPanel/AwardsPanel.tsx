import { TrophyIcon } from '@phosphor-icons/react';

import type { AwardGroup } from '@/lib/materializedMetadataDisplay';

import './AwardsPanel.css';

export interface AwardsPanelProps {
    groups: AwardGroup[];
    'data-component'?: string;
}

export function AwardsPanel({ groups, 'data-component': dataComponent }: AwardsPanelProps) {
    if (groups.length === 0) {
        return null;
    }

    return (
        <section className="awards-panel" data-component={dataComponent} aria-labelledby="awards-panel-title">
            <h2 className="awards-panel__title" id="awards-panel-title">
                <TrophyIcon weight="bold" aria-hidden="true" />
                Awards
            </h2>
            <dl className="awards-panel__list">
                {groups.map((group) => (
                    <div key={group.key} style={{ display: 'contents' }}>
                        <dt className="awards-panel__body">{group.body}</dt>
                        <dd className="awards-panel__entries">
                            {group.entries.map((entry, index) => (
                                <span key={`${group.key}:${entry.label}:${index}`} className="awards-panel__entry">
                                    {entry.label}
                                    {entry.results.length > 0 && ' '}
                                    {entry.results.length > 0 && (
                                        <span className="awards-panel__results">{entry.results.join(', ')}</span>
                                    )}
                                </span>
                            ))}
                        </dd>
                    </div>
                ))}
            </dl>
        </section>
    );
}

AwardsPanel.displayName = 'AwardsPanel';

export default AwardsPanel;
