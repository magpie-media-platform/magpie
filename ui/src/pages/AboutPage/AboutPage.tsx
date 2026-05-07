import {
    Bug,
    Clock,
    CurrencyCircleDollar,
    GithubLogo,
    Globe,
    HandsClapping,
    Info,
    Notebook,
    Scales,
    Tag,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { Skeleton } from '@/components/ui/Skeleton';
import { Logo } from '@/components/ui/Logo';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useSystemInfo } from '@/hooks/useSystemStatus';
import { formatUptime } from '@/lib/formatTime';

import './AboutPage.css';

interface ExternalLink {
    label: string;
    url: string;
    icon: React.ReactNode;
    description: string;
}

const externalLinks: ExternalLink[] = [
    {
        label: 'GitHub',
        url: 'https://github.com/magpie-media/magpie',
        icon: <GithubLogo weight="duotone" />,
        description: 'Source code, issues, and releases',
    },
    {
        label: 'Documentation',
        url: 'https://docs.magpie.media',
        icon: <Notebook weight="duotone" />,
        description: 'Installation guides and API reference',
    },
    {
        label: 'Website',
        url: 'https://magpie.media',
        icon: <Globe weight="duotone" />,
        description: 'Official project website',
    },
    {
        label: 'Report a Bug',
        url: 'https://github.com/magpie-media/magpie/issues/new',
        icon: <Bug weight="duotone" />,
        description: 'Report issues or request features',
    },
    {
        label: 'Donate',
        url: 'https://opencollective.com/magpie',
        icon: <CurrencyCircleDollar weight="duotone" />,
        description: 'Support the project development',
    },
];

interface BuildInfoItemProps {
    label: string;
    value: string;
    mono?: boolean;
    loading?: boolean;
    icon?: React.ReactNode;
}

function BuildInfoItem({ label, value, mono = false, loading, icon }: BuildInfoItemProps) {
    if (loading) {
        return (
            <div className="about-page__info-item">
                <span className="about-page__info-label">{label}</span>
                <Skeleton width={120} height={16} />
            </div>
        );
    }

    return (
        <div className="about-page__info-item">
            <span className="about-page__info-label">
                {icon}
                {label}
            </span>
            <span className={`about-page__info-value ${mono ? 'about-page__info-value--mono' : ''}`}>{value}</span>
        </div>
    );
}

export function AboutPage() {
    useDocumentTitle('About');
    const { data: systemInfo, isLoading } = useSystemInfo();

    const isInitialLoading = isLoading && !systemInfo;

    return (
        <div className="about-page" data-page="about">
            <header className="about-page__header">
                <div className="about-page__logo">
                    <Logo
                        size={48}
                        variant="solid"
                        className="about-page__logo-icon"
                        data-testid="about-page__logo-icon"
                    />
                </div>
                <div className="about-page__title-group">
                    <h1 className="about-page__title">Magpie</h1>
                    {isInitialLoading ? (
                        <Skeleton width={60} height={24} />
                    ) : (
                        <Badge variant="default">
                            <Tag weight="bold" size={12} aria-hidden="true" />v{systemInfo?.version ?? '-'}
                        </Badge>
                    )}
                </div>
                <p className="about-page__tagline">Unified media automation platform</p>
            </header>

            <div className="about-page__content">
                <section className="about-page__section" aria-labelledby="build-info-heading">
                    <h2 id="build-info-heading" className="about-page__section-title">
                        <Info weight="duotone" aria-hidden="true" />
                        System Information
                    </h2>
                    <Card className="about-page__card">
                        <div className="about-page__info-grid">
                            <BuildInfoItem
                                label="Runtime"
                                value={systemInfo?.runtime ?? '-'}
                                loading={isInitialLoading}
                            />
                            <BuildInfoItem
                                label="Operating System"
                                value={systemInfo?.operatingSystem ?? '-'}
                                loading={isInitialLoading}
                            />
                            <BuildInfoItem
                                label="Architecture"
                                value={systemInfo?.architecture ?? '-'}
                                loading={isInitialLoading}
                            />
                            <BuildInfoItem
                                label="Uptime"
                                value={formatUptime(systemInfo?.startedAt)}
                                loading={isInitialLoading}
                                icon={<Clock size={12} weight="regular" aria-hidden="true" />}
                            />
                        </div>
                    </Card>
                </section>

                <section className="about-page__section" aria-labelledby="links-heading">
                    <h2 id="links-heading" className="about-page__section-title">
                        <Globe weight="duotone" aria-hidden="true" />
                        Resources &amp; Links
                    </h2>
                    <div className="about-page__links">
                        {externalLinks.map((link) => (
                            <a
                                key={link.url}
                                href={link.url}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="about-page__link-card"
                                data-testid="about-page__link-card"
                            >
                                <div
                                    className="about-page__link-icon"
                                    data-testid="about-page__link-icon"
                                    aria-hidden="true"
                                >
                                    {link.icon}
                                </div>
                                <div className="about-page__link-content">
                                    <span className="about-page__link-label">{link.label}</span>
                                    <span className="about-page__link-description">{link.description}</span>
                                </div>
                            </a>
                        ))}
                    </div>
                </section>

                <section className="about-page__section" aria-labelledby="license-heading">
                    <h2 id="license-heading" className="about-page__section-title">
                        <Scales weight="duotone" aria-hidden="true" />
                        License
                    </h2>
                    <Card className="about-page__card">
                        <div className="about-page__license">
                            <p className="about-page__license-text">
                                Magpie is open source software licensed under the{' '}
                                <a
                                    href="https://opensource.org/licenses/MIT"
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="about-page__license-link"
                                >
                                    MIT License
                                </a>
                                .
                            </p>
                            <p className="about-page__license-text about-page__license-text--copyright">
                                Copyright 2024-2025 Magpie Contributors
                            </p>
                        </div>
                    </Card>
                </section>

                <section className="about-page__section" aria-labelledby="acknowledgements-heading">
                    <h2 id="acknowledgements-heading" className="about-page__section-title">
                        <HandsClapping weight="duotone" aria-hidden="true" />
                        Acknowledgements
                    </h2>
                    <Card className="about-page__card">
                        <p className="about-page__acknowledgements-text">
                            Thank you to all the contributors who have helped make Magpie possible.
                        </p>
                    </Card>
                </section>
            </div>
        </div>
    );
}

AboutPage.displayName = 'AboutPage';

export default AboutPage;
