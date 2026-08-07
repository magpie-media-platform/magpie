export function toStringSettings(
    settings: Record<string, string | number | boolean | undefined>,
): Record<string, string> {
    const result: Record<string, string> = {};
    for (const [key, value] of Object.entries(settings)) {
        if (value !== undefined && value !== null) {
            result[key] = String(value);
        }
    }
    return result;
}

interface SettingsPatch {
    add?: Record<string, string>;
    remove?: string[];
    update?: Record<string, string>;
}

export function buildSettingsPatch(
    newSettings: Record<string, string>,
    existingSettings: Record<string, string>,
): SettingsPatch {
    const existingKeys = new Set(Object.keys(existingSettings));
    const add: Record<string, string> = {};
    const remove: string[] = [];
    const update: Record<string, string> = {};

    for (const [key, value] of Object.entries(newSettings)) {
        if (existingKeys.has(key)) {
            update[key] = value;
        } else {
            add[key] = value;
        }
    }

    for (const key of Object.keys(existingSettings)) {
        if (newSettings[key] === undefined) {
            remove.push(key);
        }
    }

    const patch: SettingsPatch = {};
    if (Object.keys(add).length > 0) {
        patch.add = add;
    }
    if (remove.length > 0) {
        patch.remove = remove;
    }
    if (Object.keys(update).length > 0) {
        patch.update = update;
    }
    return patch;
}
