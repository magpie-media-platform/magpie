import { lazy } from 'react';
import { Navigate, Outlet, type RouteObject } from 'react-router-dom';

import { RequireAuth, RoleGuard, Role } from '@/auth';

const ExtensionPage = lazy(() => import('@/pages/ExtensionPage/ExtensionPage'));

const NotFoundPage = lazy(() => import('@/pages/NotFoundPage/NotFoundPage'));
const ForbiddenPage = lazy(() => import('@/pages/ForbiddenPage/ForbiddenPage'));
const LoginPage = lazy(() => import('@/pages/LoginPage/LoginPage'));
const SetupPage = lazy(() => import('@/pages/SetupPage/SetupPage'));

const DashboardPage = lazy(() => import('@/pages/DashboardPage/DashboardPage'));
const LibraryLandingPage = lazy(() => import('@/pages/LibraryLandingPage/LibraryLandingPage'));
const LibraryPage = lazy(() => import('@/pages/LibraryPage/LibraryPage'));
const ContentDetailPage = lazy(() => import('@/pages/ContentDetailPage/ContentDetailPage'));
const DownloadsPage = lazy(() => import('@/pages/DownloadsPage/DownloadsPage'));
const SubjectsPage = lazy(() => import('@/pages/SubjectsPage/SubjectsPage'));
const SubjectDetailPage = lazy(() => import('@/pages/SubjectDetailPage/SubjectDetailPage'));
const WishlistsPage = lazy(() => import('@/pages/WishlistsPage/WishlistsPage'));
const WishlistDetailPage = lazy(() => import('@/pages/WishlistsPage/WishlistDetailPage'));
const CalendarPage = lazy(() => import('@/pages/CalendarPage/CalendarPage'));
const FiltersPage = lazy(() => import('@/pages/FiltersPage/FiltersPage'));
const AboutPage = lazy(() => import('@/pages/AboutPage/AboutPage'));
const MyJobsPage = lazy(() => import('@/pages/MyJobsPage/MyJobsPage'));

const GeneralPage = lazy(() => import('@/pages/GeneralPage/GeneralPage'));
const UsersPage = lazy(() => import('@/pages/UsersPage/UsersPage'));
const IndexersPage = lazy(() => import('@/pages/IndexersPage/IndexersPage'));
const DownloadClientsPage = lazy(() => import('@/pages/DownloadClientsPage/DownloadClientsPage'));
const MediaServersPage = lazy(() => import('@/pages/MediaServersPage/MediaServersPage'));
const RootFoldersPage = lazy(() => import('@/pages/RootFoldersPage/RootFoldersPage'));
const QualityTiersPage = lazy(() => import('@/pages/QualityTiersPage/QualityTiersPage'));
const TierListDetailPage = lazy(() => import('@/pages/QualityTiersPage/TierListDetailPage'));
const JobsPage = lazy(() => import('@/pages/JobsPage/JobsPage'));
const ExtensionsPage = lazy(() => import('@/pages/ExtensionsPage/ExtensionsPage'));
const ExtensionDetailPage = lazy(() => import('@/pages/ExtensionsPage/ExtensionDetailPage'));
const BackupsPage = lazy(() => import('@/pages/BackupsPage/BackupsPage'));
const SystemStatusPage = lazy(() => import('@/pages/SystemStatusPage/SystemStatusPage'));

export const routes: RouteObject[] = [
    {
        path: '/login',
        element: <LoginPage />,
    },

    {
        path: '/setup',
        element: <SetupPage />,
    },

    {
        path: '/forbidden',
        element: <ForbiddenPage />,
    },

    {
        element: (
            <RequireAuth>
                <Outlet />
            </RequireAuth>
        ),
        children: [
            {
                path: '/admin',
                element: (
                    <RoleGuard minRole={Role.Admin}>
                        <Outlet />
                    </RoleGuard>
                ),
                children: [
                    {
                        index: true,
                        element: <Navigate to="/admin/general" replace />,
                    },

                    {
                        path: 'general',
                        element: <GeneralPage />,
                    },

                    {
                        path: 'users',
                        element: <UsersPage />,
                    },

                    {
                        path: 'indexers',
                        element: <IndexersPage />,
                    },
                    {
                        path: 'download-clients',
                        element: <DownloadClientsPage />,
                    },
                    {
                        path: 'media-servers',
                        element: <MediaServersPage />,
                    },
                    {
                        path: 'root-folders',
                        element: <RootFoldersPage />,
                    },

                    {
                        path: 'quality-tiers',
                        element: <QualityTiersPage />,
                    },
                    {
                        path: 'quality-tiers/:tierListId',
                        element: <TierListDetailPage />,
                    },
                    {
                        path: 'jobs',
                        element: <JobsPage />,
                    },
                    {
                        path: 'extensions',
                        element: <ExtensionsPage />,
                    },
                    {
                        path: 'extensions/:extensionId',
                        element: <ExtensionDetailPage />,
                    },
                    {
                        path: 'backups',
                        element: <BackupsPage />,
                    },
                    {
                        path: 'system',
                        element: <SystemStatusPage />,
                    },

                    {
                        path: 'ext/:slug/*',
                        element: <ExtensionPage admin />,
                    },
                ],
            },

            {
                path: '/',
                element: <DashboardPage />,
            },
            {
                path: '/library',
                element: <LibraryLandingPage />,
            },
            {
                path: '/library/all',
                element: <LibraryPage />,
            },
            {
                path: '/library/content/:contentType',
                element: <LibraryPage />,
            },
            {
                path: '/library/:contentId',
                element: <ContentDetailPage />,
            },
            {
                path: '/library/:contentId/:childId',
                element: <ContentDetailPage />,
            },
            {
                path: '/library/:contentId/:childId/:grandchildId',
                element: <ContentDetailPage />,
            },
            {
                path: '/downloads',
                element: <DownloadsPage />,
            },
            {
                path: '/library/subjects/all',
                element: <SubjectsPage />,
            },
            {
                path: '/library/subjects/:subjectType',
                element: <SubjectsPage />,
            },
            {
                path: '/subjects/:subjectId',
                element: <SubjectDetailPage />,
            },
            {
                path: '/wishlists',
                element: <WishlistsPage />,
            },
            {
                path: '/wishlists/:wishlistId',
                element: <WishlistDetailPage />,
            },
            {
                path: '/calendar',
                element: <CalendarPage />,
            },
            {
                path: '/filters',
                element: <FiltersPage />,
            },
            {
                path: '/about',
                element: <AboutPage />,
            },
            {
                path: '/my-jobs',
                element: <MyJobsPage />,
            },

            {
                path: '/ext/:slug/*',
                element: <ExtensionPage />,
            },

            {
                path: '*',
                element: <NotFoundPage />,
            },
        ],
    },
];
