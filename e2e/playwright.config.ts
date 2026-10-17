import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';

/**
 * End-to-end tests run the real stack: ASP.NET Core API (Development environment, emails written to a pickup
 * folder) + Angular dev server + PostgreSQL. Requires TMA_E2E_DB: a connection string to an empty or reusable
 * database (tests use unique emails, so a database can be reused between runs).
 */
const database = process.env['TMA_E2E_DB'];
if (!database) {
  throw new Error('Set TMA_E2E_DB to a PostgreSQL connection string for the end-to-end database (see docs/testing.md).');
}

export const mailDirectory = path.resolve(import.meta.dirname, '.mail');
const repositoryRoot = path.resolve(import.meta.dirname, '..');

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  timeout: 60_000,
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  globalSetup: './support/global-setup.ts',
  webServer: [
    {
      command: 'dotnet run --project src/TaskManagement.Api --launch-profile http',
      cwd: repositoryRoot,
      url: 'http://localhost:5080/health/ready',
      timeout: 300_000,
      reuseExistingServer: !process.env['CI'],
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Default: database,
        Email__PickupDirectory: mailDirectory,
      },
    },
    {
      command: 'npm start -- --port 4200',
      cwd: path.join(repositoryRoot, 'frontend'),
      url: 'http://localhost:4200',
      timeout: 300_000,
      reuseExistingServer: !process.env['CI'],
    },
  ],
});
