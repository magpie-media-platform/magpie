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

export function buildSettingsPatch(
    newSettings: Record<string, string>,
    existingSettings: Record<string, string>,
): { add?: Record<string, string>; update?: Record<string, string> } {
    const existingKeys = new Set(Object.keys(existingSettings));
    const add: Record<string, string> = {};
    const update: Record<string, string> = {};

    for (const [key, value] of Object.entries(newSettings)) {
        if (existingKeys.has(key)) {
            update[key] = value;
        } else {
            add[key] = value;
        }
    }

    const patch: { add?: Record<string, string>; update?: Record<string, string> } = {};
    if (Object.keys(add).length > 0) {
        patch.add = add;
    }
    if (Object.keys(update).length > 0) {
        patch.update = update;
    }
    return patch;
}
