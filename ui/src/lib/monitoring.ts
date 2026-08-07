export interface ResolvedMonitoring {
    monitored: boolean;
    inherited: boolean;
}

export function resolveEffectiveMonitoring(
    monitoring: boolean | null,
    effectiveMonitoring: boolean,
): ResolvedMonitoring {
    return { monitored: effectiveMonitoring, inherited: monitoring === null };
}
