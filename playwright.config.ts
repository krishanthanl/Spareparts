import { defineConfig, devices } from '@playwright/test';
const channel = process.env.PLAYWRIGHT_CHANNEL ?? 'chrome';
export default defineConfig({
  testDir: './tests/browser',
  fullyParallel: true,
  workers: 2,
  timeout: 30000,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    channel: channel === 'chromium' ? undefined : channel,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'desktop',
      use: {
        ...devices['Desktop Chrome'],
        channel: channel === 'chromium' ? undefined : channel,
      },
    },
    {
      name: 'mobile',
      use: {
        viewport: { width: 390, height: 844 },
        isMobile: true,
        hasTouch: true,
      },
    },
  ],
  webServer: [
    {
      command:
        'dotnet run --no-build --no-launch-profile --project src/backend/Api --urls http://localhost:5080',
      url: 'http://localhost:5080/health/live',
      reuseExistingServer: false,
      timeout: 60000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        DOTNET_CLI_HOME: process.cwd() + '/.local/dotnet',
        NUGET_PACKAGES: process.cwd() + '/.local/nuget',
        ConnectionStrings__DefaultConnection:
          'Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Timeout=1;Pooling=false',
      },
    },
    {
      command: 'npm run dev:admin',
      url: 'http://localhost:5173',
      reuseExistingServer: false,
    },
    {
      command: 'npm run dev:public',
      url: 'http://localhost:5174',
      reuseExistingServer: false,
    },
  ],
});
