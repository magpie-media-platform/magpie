import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import type { LibraryImportMonitoringMode } from '@/hooks/useBulkAcceptLibraryImportCandidates';

interface ImportMonitoringSelectProps {
    value: LibraryImportMonitoringMode | '';
    onChange: (value: LibraryImportMonitoringMode) => void;
}

const OPTIONS: { value: LibraryImportMonitoringMode; label: string }[] = [
    { value: 'Inherit', label: 'Inherit' },
    { value: 'Monitored', label: 'Monitored' },
    { value: 'Unmonitored', label: 'Unmonitored' },
    { value: 'Future', label: 'Future only' },
];

export function ImportMonitoringSelect({ value, onChange }: ImportMonitoringSelectProps) {
    const selected = OPTIONS.find((option) => option.value === value);

    return (
        <Select value={value} onValueChange={(next) => onChange(next as LibraryImportMonitoringMode)}>
            <SelectTrigger
                id="import-monitoring-select"
                size="sm"
                placeholder="Select monitoring"
                aria-label="Import monitoring"
            >
                {selected?.label ?? 'Select monitoring'}
            </SelectTrigger>
            <SelectContent>
                {OPTIONS.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                        {option.label}
                    </SelectItem>
                ))}
            </SelectContent>
        </Select>
    );
}

ImportMonitoringSelect.displayName = 'ImportMonitoringSelect';
