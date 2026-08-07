import { DetailInfoRow, type DetailInfoRowProps } from '@/components/ui/DetailInfoGrid';

interface ContentPolicyRowProps extends Omit<DetailInfoRowProps, 'value'> {
    effectiveValue: DetailInfoRowProps['value'];
    testId?: string;
}

export function ContentPolicyRow({ effectiveValue, testId, ...props }: ContentPolicyRowProps) {
    return <DetailInfoRow value={effectiveValue} data-testid={testId} {...props} />;
}
