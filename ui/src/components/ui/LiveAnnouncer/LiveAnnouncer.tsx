/* eslint-disable react-refresh/only-export-components */
import { useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';

import { LiveAnnouncerContext, type LiveAnnouncerContextValue } from './LiveAnnouncerContext';

import './LiveAnnouncer.css';

type Politeness = 'polite' | 'assertive';

interface Announcement {
    id: string;
    message: string;
    politeness: Politeness;
}

export interface LiveAnnouncerProps {
    children: ReactNode;
}

export function LiveAnnouncerProvider({ children }: LiveAnnouncerProps) {
    const instanceId = useId();
    const [politeAnnouncements, setPoliteAnnouncements] = useState<Announcement[]>([]);
    const [assertiveAnnouncements, setAssertiveAnnouncements] = useState<Announcement[]>([]);
    const announcementCountRef = useRef(0);
    const timersRef = useRef<Set<ReturnType<typeof setTimeout>>>(new Set());

    useEffect(() => {
        const timers = timersRef.current;
        return () => {
            timers.forEach(clearTimeout);
        };
    }, []);

    const announce = useCallback(
        (message: string, politeness: Politeness = 'polite') => {
            if (!message.trim()) {
                return;
            }

            announcementCountRef.current += 1;
            const id = `${instanceId}-${announcementCountRef.current}`;
            const announcement: Announcement = { id, message, politeness };

            if (politeness === 'assertive') {
                setAssertiveAnnouncements((prev) => [...prev, announcement]);
            } else {
                setPoliteAnnouncements((prev) => [...prev, announcement]);
            }

            const timer = setTimeout(() => {
                timersRef.current.delete(timer);
                if (politeness === 'assertive') {
                    setAssertiveAnnouncements((prev) => prev.filter((a) => a.id !== id));
                } else {
                    setPoliteAnnouncements((prev) => prev.filter((a) => a.id !== id));
                }
            }, 7000);
            timersRef.current.add(timer);
        },
        [instanceId],
    );

    const value = useMemo(() => ({ announce }), [announce]);

    return (
        <LiveAnnouncerContext.Provider value={value}>
            {children}
            <div
                className="live-announcer"
                role="status"
                aria-live="polite"
                aria-atomic="true"
                data-component="live-announcer"
                data-politeness="polite"
            >
                {politeAnnouncements.map((a) => (
                    <span key={a.id}>{a.message}</span>
                ))}
            </div>
            <div
                className="live-announcer"
                role="alert"
                aria-live="assertive"
                aria-atomic="true"
                data-component="live-announcer"
                data-politeness="assertive"
            >
                {assertiveAnnouncements.map((a) => (
                    <span key={a.id}>{a.message}</span>
                ))}
            </div>
        </LiveAnnouncerContext.Provider>
    );
}

export function useLiveAnnouncer(): LiveAnnouncerContextValue {
    const context = useContext(LiveAnnouncerContext);

    if (!context) {
        throw new Error('useLiveAnnouncer must be used within a LiveAnnouncerProvider');
    }

    return context;
}

export default LiveAnnouncerProvider;
