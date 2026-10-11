import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';

import { useEventStream } from '@/hooks/useEventStream';
import { useSystemInfo } from '@/hooks/useSystemStatus';
import { useInstanceName } from '@/hooks/useInstanceName';
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

const STARTUP_FAILED_STATUS = 'StartupFailed';
const RECONNECT_TIMEOUT_MS = 120_000;

type HostProbeOutcome = 'restarted' | 'failed' | 'waiting';

async function probeRestartedHost(baselineStartedAt: string | null): Promise<HostProbeOutcome> {
    try {
        const health = await apiGet<ApiHealthResponse>('/system/health', {
            skipAuth: true,
            cache: 'no-store',
        });
        if (health.status === STARTUP_FAILED_STATUS) {
            return 'failed';
        }

        if (health.status !== 'Healthy') {
            return 'waiting';
        }

        if (baselineStartedAt === null) {
            return 'restarted';
        }

        const startedAt = await probeStartedAt();
        return startedAt !== null && startedAt !== baselineStartedAt ? 'restarted' : 'waiting';
    } catch {
        return 'waiting';
    }
}

export function useHostRestart() {
    const instanceName = useInstanceName();
    const navigate = useNavigate();
    const queryClient = useQueryClient();

    const systemInfoQuery = useSystemInfo();

    const [phase, setPhase] = useState<RestartPhase>('idle');
    const [error, setError] = useState<string | null>(null);

    const baselineStartedAt = useRef<string | null>(null);
    const reconnectDeadline = useRef<number>(0);

    const checkRecovered = useCallback(() => {
        void probeRestartedHost(baselineStartedAt.current).then((outcome) => {
            if (outcome === 'restarted') {
                setError(null);
                setPhase('recovered');
                return;
            }

            if (outcome === 'failed') {
                setError(`${instanceName} could not start. Check the host log for the cause.`);
                setPhase('error');
                return;
            }

            if (Date.now() > reconnectDeadline.current) {
                setError(`${instanceName} did not come back online. Read the host log.`);
                setPhase('error');
            }
        });
    }, [instanceName]);

    useEventStream(checkRecovered, {
        eventName: 'stream.reconnected',
        enabled: phase === 'reconnecting',
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

        reconnectDeadline.current = Date.now() + RECONNECT_TIMEOUT_MS;
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
        phase,
        error,
        openConfirm,
        cancelConfirm,
        requestRestart,
        reset,
        recover,
    };
}
