import { lazy, Suspense } from 'react';
import { Route, Routes } from 'react-router-dom';
import CircularProgress from '@mui/material/CircularProgress';
import Box from '@mui/material/Box';

// Route-level code splitting — each page chunk is only loaded when navigated to
const PlaceholderPage = lazy(() => import('@/pages/PlaceholderPage'));
const NotFoundPage = lazy(() => import('@/pages/NotFoundPage'));

function RouteLoadingFallback() {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
      <CircularProgress />
    </Box>
  );
}

function AppRoutes() {
  return (
    <Suspense fallback={<RouteLoadingFallback />}>
      <Routes>
        <Route path="/" element={<PlaceholderPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Suspense>
  );
}

export default AppRoutes;
