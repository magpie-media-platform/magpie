import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';

import { useEventStream } from '@/hooks/useEventStream';
import { useSystemInfo } from '@/hooks/useSystemStatus';
import { apiGet, apiPost, isApiError } from '@/lib/api';

interface ApiHealthResponse {
    status: string;
}

interface ApiSystemInfoResponse {
    startedAt: string;
}

type RestartPhase = 'idle' | 'confirming' | 'restarting' | 'reconnecting' | 'recovered' | 'error';

async function probeStartedAt(): Promise<string | null> {
    try {
        const systemInfo = await apiGet<ApiSystemInfoResponse>('/system', { cache: 'no-store' });
        return systemInfo.startedAt;
    } catch {
        return null;
    }
}

async function probeRestartedHost(baselineStartedAt: string | null): Promise<boolean> {
    try {
        const health = await apiGet<ApiHealthResponse>('/system/health', {
            skipAuth: true,
            cache: 'no-store',
        });
        if (health.status !== 'Healthy') {
            return false;
        }

        if (baselineStartedAt === null) {
            return true;
        }

        const startedAt = await probeStartedAt();
        return startedAt !== null && startedAt !== baselineStartedAt;
    } catch {
        return false;
    }
}

export function useHostRestart() {
    const navigate = useNavigate();
    const queryClient = useQueryClient();

    const systemInfoQuery = useSystemInfo();

    const [phase, setPhase] = useState<RestartPhase>('idle');
    const [error, setError] = useState<string | null>(null);

    const baselineStartedAt = useRef<string | null>(null);

    const checkRecovered = useCallback(() => {
        void probeRestartedHost(baselineStartedAt.current).then((restarted) => {
            if (restarted) {
                setError(null);
                setPhase('recovered');
            }
        });
    }, []);

    useEventStream(checkRecovered, {
        eventName: 'stream.reconnected',
        enabled: phase === 'reconnecting',
        owner: 'host-restart',
    });

    useEffect(() => {
        if (phase !== 'reconnecting') {
            return;
        }

        checkRecovered();
        const interval = setInterval(checkRecovered, 2000);
        return () => clearInterval(interval);
    }, [phase, checkRecovered]);

    const requestRestart = useCallback(async () => {
        setError(null);
        baselineStartedAt.current = systemInfoQuery.data?.startedAt ?? null;
        setPhase('restarting');

        const currentStartedAt = await probeStartedAt();
        if (currentStartedAt !== null) {
            baselineStartedAt.current = currentStartedAt;
        }

        try {
            await apiPost('/system/restart');
        } catch (err) {
            const message = isApiError(err) ? err.message : 'The restart request failed.';
            setError(message);
            setPhase('error');
            return;
        }

        setPhase('reconnecting');
    }, [systemInfoQuery.data?.startedAt]);

    const openConfirm = useCallback(() => {
        setError(null);
        setPhase('confirming');
    }, []);

    const cancelConfirm = useCallback(() => {
        setPhase('idle');
    }, []);

    const reset = useCallback(() => {
        baselineStartedAt.current = null;
        setError(null);
        setPhase('idle');
    }, []);

    const recover = useCallback(async () => {
        baselineStartedAt.current = null;
        setError(null);
        setPhase('idle');
        void navigate('/');
        await queryClient.invalidateQueries();
    }, [navigate, queryClient]);

    const canRestart = systemInfoQuery.data?.restartSupported ?? false;

    return {
        capabilityLoading: systemInfoQuery.isLoading,
        canRestart,
        unavailableReason: null,
        phase,
        error,
        openConfirm,
        cancelConfirm,
        requestRestart,
        reset,
        recover,
    };
}
