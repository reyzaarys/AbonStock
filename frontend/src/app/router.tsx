import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppLayout } from '../components/layout/AppLayout';
import { ProtectedRoute } from '../components/auth/ProtectedRoute';
import { LoginPage } from '../features/auth/LoginPage';
import { InventoryPage } from '../features/inventory/InventoryPage';
import { ProductsPage } from '../features/products/ProductsPage';
import { OrdersPage } from '../features/orders/OrdersPage';

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <LoginPage />,
  },
  {
    path: '/',
    element: <AppLayout />,
    children: [
      {
        element: <ProtectedRoute />,
        children: [
          {
            index: true,
            element: <Navigate to="/inventory" replace />,
          },
          {
            path: 'inventory',
            element: <InventoryPage />,
          },
          {
            path: 'products',
            element: <ProductsPage />,
          },
          {
            path: 'orders',
            element: <OrdersPage />,
          },
        ],
      },
      {
        path: '*',
        element: <Navigate to="/inventory" replace />,
      },
    ],
  },
]);
