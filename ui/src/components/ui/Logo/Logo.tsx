import './Logo.css';

interface LogoProps {
    size?: number;
    className?: string;
    variant?: 'default' | 'solid';
    'data-testid'?: string;
}

export function Logo({ size = 36, className = '', variant = 'default', 'data-testid': testId }: LogoProps) {
    return (
        <span
            className={`logo logo--${variant} ${className}`}
            aria-hidden="true"
            style={{ width: size, height: size }}
            data-testid={testId}
        />
    );
}
