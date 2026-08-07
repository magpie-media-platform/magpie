import { Navigate, Outlet, useLocation, type RouteObject } from 'react-router-dom';

import { RequireAuth, RoleGuard, Role, RoleName, useAuth } from '@/auth';

function Stub({ name, page }: { name: string; page?: string }) {
    return (
        <div data-page={page}>
            <h1>{name}</h1>
        </div>
    );
}

function AdminLayout() {
    return (
        <div data-layout="admin">
            <Outlet />
        </div>
    );
}

function ForbiddenStub() {
    const location = useLocation();
    const { user } = useAuth();
    const state = (location.state as { requiredRole?: string }) || {};

    let description = 'You do not have permission to access this page.';
    if (state.requiredRole) {
        description = `This page requires ${state.requiredRole} role or higher.`;
    }
    if (user) {
        description += ` Signed in as ${user.username} (${RoleName[user.role]}).`;
    }

    return (
        <div data-page="forbidden">
            <h1>Access denied</h1>
            <p>{description}</p>
        </div>
    );
}

export const routes: RouteObject[] = [
    { path: '/login', element: <Stub name="Sign in" /> },
    { path: '/setup', element: <Stub name="Create Admin" /> },
    { path: '/forbidden', element: <ForbiddenStub /> },

    {
        path: '/admin',
        element: (
            <RequireAuth>
                <RoleGuard minRole={Role.Admin}>
                    <AdminLayout />
                </RoleGuard>
            </RequireAuth>
        ),
        children: [
            { index: true, element: <Navigate to="general" replace /> },
            { path: 'general', element: <Stub name="General" page="general" /> },
            { path: 'users', element: <Stub name="Users" page="users" /> },
            { path: 'indexers', element: <Stub name="Indexers" page="indexers" /> },
            { path: 'download-clients', element: <Stub name="Download Clients" page="download-clients" /> },
            { path: 'media-servers', element: <Stub name="Media Servers" page="media-servers" /> },
            { path: 'root-folders', element: <Stub name="Root Folders" page="root-folders" /> },
            { path: 'quality-tiers', element: <Stub name="Quality Tiers" page="quality-tiers" /> },
            { path: 'jobs', element: <Stub name="System Jobs" page="jobs" /> },
            { path: 'extensions', element: <Stub name="Extensions" page="extensions" /> },
            { path: 'database', element: <Stub name="Database" page="backups" /> },
            { path: 'system', element: <Stub name="System Status" page="system" /> },
        ],
    },

    {
        element: (
            <RequireAuth>
                <Outlet />
            </RequireAuth>
        ),
        children: [
            { path: '/', element: <Stub name="Dashboard" /> },
            { path: '/library', element: <Stub name="Library" /> },
            { path: '/library/all', element: <Stub name="Library" /> },
            { path: '/library/content/:contentType', element: <Stub name="Library" /> },
            { path: '/library/content/:contentType/:levelSlug', element: <Stub name="Library" /> },
            { path: '/library/:contentId', element: <Stub name="Content" page="content-detail" /> },
            { path: '/library/:contentId/:childId', element: <Stub name="Content" page="content-detail" /> },
            {
                path: '/library/:contentId/:childId/:grandchildId',
                element: <Stub name="Content" page="content-detail" />,
            },
            { path: '/downloads', element: <Stub name="Downloads" /> },
            { path: '/library/subjects/all', element: <Stub name="Subjects" /> },
            { path: '/library/subjects/:subjectType', element: <Stub name="Subjects" /> },
            { path: '/subjects/:subjectId', element: <Stub name="Subject" /> },
            { path: '/wishlists', element: <Stub name="Wishlists" page="wishlists" /> },
            { path: '/wishlists/:wishlistId', element: <Stub name="Wishlist" /> },
            { path: '/calendar', element: <Stub name="Calendar" /> },
            { path: '/filters', element: <Stub name="Filters" page="filters" /> },
            { path: '/about', element: <Stub name="About" page="about" /> },
            { path: '/my-jobs', element: <Stub name="My Jobs" /> },
        ],
    },

    { path: '*', element: <Stub name="Page not found" /> },
];
