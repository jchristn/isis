import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// Isis dashboard build configuration.
// The dashboard talks to the Isis REST server (default loopback 127.0.0.1:8700).
export default defineConfig({
  plugins: [react()],
  define: {
    __DEFAULT_SERVER_URL__: JSON.stringify(process.env.ISIS_SERVER_URL || 'http://127.0.0.1:8700'),
    __DEFAULT_ADMIN_EMAIL__: JSON.stringify(process.env.ISIS_ADMIN_EMAIL || 'admin@isis.local'),
    __DEFAULT_TENANT_ID__: JSON.stringify(process.env.ISIS_TENANT_ID || 'ten_default')
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@components': path.resolve(__dirname, './src/components'),
      '@views': path.resolve(__dirname, './src/views'),
      '@context': path.resolve(__dirname, './src/context'),
      '@utils': path.resolve(__dirname, './src/utils'),
      '@hooks': path.resolve(__dirname, './src/hooks')
    }
  },
  server: {
    host: true,
    port: 8701
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks: {
          vendor: ['react', 'react-dom', 'react-router-dom']
        }
      }
    }
  }
});
