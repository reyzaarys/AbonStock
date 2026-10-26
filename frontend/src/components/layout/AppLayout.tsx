import React from 'react';
import { Outlet } from 'react-router-dom';
import { Navbar } from './Navbar';

export const AppLayout: React.FC = () => {
  return (
    <div className="app-container">
      <Navbar />
      <main className="main-content">
        <Outlet />
      </main>
      <footer className="footer">
        <p>AbonStock &copy; 2026 &mdash; High-Concurrency Inventory &amp; Order Fulfillment Portfolio</p>
      </footer>
    </div>
  );
};
