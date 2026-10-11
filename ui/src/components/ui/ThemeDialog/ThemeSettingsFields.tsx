import { useId } from 'react';

import { Label } from '../Label';
import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';
import type { Theme, ThemeSettings } from '@/lib/theme';
import './ThemeDialog.css';

interface ThemeSettingsFieldsProps {
    themes: Theme[];
    value: ThemeSettings;
    onChange: (key: keyof ThemeSettings, value: string) => void;
    useDefault?: boolean;
    onUseDefault?: () => void;
    disabled?: boolean;
}

export function ThemeSettingsFields({
    themes,
    value,
    onChange,
    useDefault,
    onUseDefault,
    disabled,
}: ThemeSettingsFieldsProps) {
    const id = useId();
    return (
        <div className="theme-settings-fields">
            <div className="theme-settings-fields__field">
                <Label htmlFor={`${id}-mode`}>Mode</Label>
                <Select
                    value={useDefault ? 'Default' : value.mode}
                    disabled={disabled}
                    onValueChange={(mode) => {
                        if (mode === 'Default') onUseDefault?.();
                        else onChange('mode', mode);
                    }}
                >
                    <SelectTrigger id={`${id}-mode`} />
                    <SelectContent>
                        {onUseDefault && <SelectItem value="Default">Default</SelectItem>}
                        <SelectItem value="Dark">Dark</SelectItem>
                        <SelectItem value="Light">Light</SelectItem>
                        <SelectItem value="System">System</SelectItem>
                    </SelectContent>
                </Select>
            </div>
            <div className="theme-settings-fields__themes">
                {(['Dark', 'Light'] as const).map((variant) => {
                    const key = variant === 'Dark' ? 'darkTheme' : 'lightTheme';
                    const choices = themes.filter((theme) => theme.variant === variant);
                    return (
                        <div className="theme-settings-fields__field" key={variant}>
                            <Label htmlFor={`${id}-${variant}`}>{variant} theme</Label>
                            <Select
                                value={choices.some((theme) => theme.id === value[key]) ? value[key] : ''}
                                disabled={disabled || useDefault}
                                onValueChange={(theme) => onChange(key, theme)}
                            >
                                <SelectTrigger id={`${id}-${variant}`} placeholder="Unavailable theme" />
                                <SelectContent>
                                    {choices.map((theme) => (
                                        <SelectItem key={theme.id} value={theme.id}>
                                            {theme.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
