import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

export const adminFile = path.resolve(import.meta.dirname, '..', '.mail', 'admin.json');

/** Creates a fresh first Admin with the real create-admin command; the invite email lands in the pickup folder. */
export default function globalSetup(): void {
  const mailDirectory = path.resolve(import.meta.dirname, '..', '.mail');
  mkdirSync(mailDirectory, { recursive: true });
  const email = `e2e-admin-${Date.now()}@example.com`;
  execFileSync(
    'dotnet',
    ['run', '--no-build', '--project', 'src/TaskManagement.Api', '--launch-profile', 'http', '--', 'create-admin', '--email', email, '--name', 'E2E Admin'],
    {
      cwd: path.resolve(import.meta.dirname, '..', '..'),
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Default: process.env['TMA_E2E_DB'],
        Email__PickupDirectory: mailDirectory,
      },
      stdio: 'inherit',
    },
  );
  writeFileSync(adminFile, JSON.stringify({ email }));
}
