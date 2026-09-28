import { useEffect, useRef, useState, useSyncExternalStore } from 'react';

import { Button } from '../Button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from '../Dialog';
import { Popover, PopoverContent, PopoverTrigger } from '../Popover';
import { useMediaQuery } from '@/hooks/ui/useMediaQuery';
import { getLiveUpdateStatus, retryLiveUpdates, subscribeLiveUpdateStatus } from '@/lib/liveUpdates';

import './LiveUpdateStatus.css';

const labels = {
    connecting: 'Connecting…',
    connected: 'Live updates connected.',
    reconnecting: 'Connection lost. Live updates paused. Reconnecting…',
    unsupported: 'Live updates are not supported in this browser. Reload the page to see changes.',
    failed: 'Live updates failed',
};

export function LiveUpdateStatus() {
    const status = useSyncExternalStore(subscribeLiveUpdateStatus, getLiveUpdateStatus);
    const mobile = useMediaQuery('(max-width: 767px)');
    const [open, setOpen] = useState(false);
    const closeTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
    const restoringFocus = useRef(false);
    useEffect(() => () => clearTimeout(closeTimer.current), []);
    const show = () => {
        clearTimeout(closeTimer.current);
        setOpen(true);
    };
    const hide = () => {
        closeTimer.current = setTimeout(() => setOpen(false), 150);
    };
    const label = labels[status.state];
    const trigger = (
        <button type="button" className="live-update-status" aria-label={label} data-state={status.state}>
            <span className="live-update-status__dot" aria-hidden="true" />
        </button>
    );
    const details = (
        <>
            {status.error && <p className="live-update-status__error">{status.error}</p>}
            {status.state === 'failed' && <Button onClick={retryLiveUpdates}>Retry live updates</Button>}
        </>
    );

    if (mobile) {
        return (
            <Dialog open={open} onOpenChange={setOpen}>
                <DialogTrigger asChild>{trigger}</DialogTrigger>
                <DialogContent size="alert">
                    <DialogHeader>
                        <DialogTitle>Live updates</DialogTitle>
                        <DialogDescription>{label}</DialogDescription>
                    </DialogHeader>
                    {details}
                </DialogContent>
            </Dialog>
        );
    }

    return (
        <Popover open={open} onOpenChange={setOpen}>
            <PopoverTrigger
                asChild
                onMouseEnter={show}
                onMouseLeave={hide}
                onFocus={() => {
                    if (!restoringFocus.current) show();
                }}
                onClick={(event) => {
                    event.preventDefault();
                    show();
                }}
            >
                {trigger}
            </PopoverTrigger>
            <PopoverContent
                align="start"
                aria-label="Live updates"
                onMouseEnter={() => clearTimeout(closeTimer.current)}
                onMouseLeave={hide}
                onOpenAutoFocus={(event) => event.preventDefault()}
                onCloseAutoFocus={() => {
                    restoringFocus.current = true;
                    queueMicrotask(() => {
                        restoringFocus.current = false;
                    });
                }}
            >
                <p>{label}</p>
                {details}
            </PopoverContent>
        </Popover>
    );
}
