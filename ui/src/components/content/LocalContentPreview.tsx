import { createContext, useCallback, useContext, useRef, useState, type ReactNode, type ComponentProps } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';

import { PreviewDrawer } from './PreviewDrawer';
import { Button } from '@/components/ui/Button';

const PreviewContext = createContext<(id: string, trigger?: HTMLElement, identity?: 'hierarchy' | 'item') => void>(
    () => {},
);

export function LocalContentPreviewProvider({ children }: { children: ReactNode }) {
    const [id, setId] = useState<string>();
    const [identity, setIdentity] = useState<'hierarchy' | 'item'>('hierarchy');
    const [open, setOpen] = useState(false);
    const trigger = useRef<HTMLElement | null>(null);
    const navigate = useNavigate();
    const location = useLocation();
    const openPreview = useCallback(
        (contentId: string, invokingElement?: HTMLElement, contentIdentity: 'hierarchy' | 'item' = 'hierarchy') => {
            trigger.current =
                invokingElement ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
            setId(contentId);
            setIdentity(contentIdentity);
            setOpen(true);
        },
        [],
    );
    return (
        <PreviewContext value={openPreview}>
            {children}
            {id && (
                <PreviewDrawer
                    key={`${identity}:${id}`}
                    open={open}
                    onOpenChange={setOpen}
                    target={null}
                    localContentId={id}
                    localIdentity={identity}
                    onCloseAutoFocus={(event) => {
                        event.preventDefault();
                        if (trigger.current?.isConnected) trigger.current.focus({ preventScroll: true });
                    }}
                    onOpenContent={(resolvedId) => {
                        setOpen(false);
                        void navigate(`/library/${encodeURIComponent(resolvedId)}`, {
                            state: {
                                contentOrigin: {
                                    pathname: location.pathname,
                                    search: location.search,
                                    hash: location.hash,
                                    state: location.state,
                                },
                            },
                        });
                    }}
                />
            )}
        </PreviewContext>
    );
}

export function useOpenLocalContentPreview() {
    return useContext(PreviewContext);
}

export function LocalContentItemReference({ contentItemId, children }: { contentItemId: string; children: ReactNode }) {
    const openPreview = useOpenLocalContentPreview();
    return (
        <Button
            variant="ghost"
            size="sm"
            onClick={(event) => {
                event.stopPropagation();
                openPreview(contentItemId, event.currentTarget, 'item');
            }}
        >
            {children}
        </Button>
    );
}

export function LocalContentLink({
    contentId,
    onClick,
    ...props
}: Omit<ComponentProps<typeof Link>, 'to'> & { contentId: string }) {
    const openPreview = useOpenLocalContentPreview();
    return (
        <Link
            {...props}
            to={`/library/${encodeURIComponent(contentId)}`}
            onClick={(event) => {
                onClick?.(event);
                if (!event.defaultPrevented) {
                    event.preventDefault();
                    event.stopPropagation();
                    openPreview(contentId, event.currentTarget);
                }
            }}
        />
    );
}
