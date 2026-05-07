import { createContext } from 'react';

type Politeness = 'polite' | 'assertive';

export interface LiveAnnouncerContextValue {
    announce: (message: string, politeness?: Politeness) => void;
}

export const LiveAnnouncerContext = createContext<LiveAnnouncerContextValue | null>(null);
