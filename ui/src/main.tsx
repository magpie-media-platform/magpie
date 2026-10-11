import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import { installPreloadErrorReload } from './lib/preloadErrorReload';
import { registerServiceWorker } from './lib/registerServiceWorker';
import { initializeTheme } from './lib/theme';
import './styles/index.css';
import App from './App.tsx';

const disposeTheme = initializeTheme();
installPreloadErrorReload();
registerServiceWorker();

const root = createRoot(document.getElementById('root')!);
import.meta.hot?.dispose(() => {
    disposeTheme();
    root.unmount();
});
root.render(
    <StrictMode>
        <App />
    </StrictMode>,
);
