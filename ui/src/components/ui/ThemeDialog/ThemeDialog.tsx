import { useSyncExternalStore } from 'react';

import { getThemeState, setThemePreference, subscribeTheme, type ThemeSettings } from '@/lib/theme';
import { Alert } from '../Alert';
import { Button } from '../Button';
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogBody,
    DialogFooter,
    DialogClose,
} from '../Dialog';
import { ThemeSettingsFields } from './ThemeSettingsFields';

export function ThemeDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
    const { catalog, preference, error } = useSyncExternalStore(subscribeTheme, getThemeState);
    const settings = preference ?? catalog.defaults;
    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent>
                <DialogHeader>
                    <DialogTitle>Theme</DialogTitle>
                    <DialogDescription>Applies to this browser.</DialogDescription>
                </DialogHeader>
                <DialogBody>
                    <ThemeSettingsFields
                        themes={catalog.themes}
                        value={settings}
                        onChange={(key, value) => setThemePreference({ ...settings, [key]: value } as ThemeSettings)}
                        useDefault={preference === null}
                        onUseDefault={() => setThemePreference(null)}
                    />
                    {error && (
                        <Alert variant="warning" className="theme-dialog__error">
                            {error}
                        </Alert>
                    )}
                </DialogBody>
                <DialogFooter>
                    <DialogClose asChild>
                        <Button>Done</Button>
                    </DialogClose>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}
