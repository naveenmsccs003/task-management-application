# Task Management — Angular app

Angular 22 (standalone components, signals, zoneless), Angular Material, Vitest, ESLint.

```bash
npm ci
npm start                      # http://localhost:4200, proxies API calls to http://localhost:5080 (proxy.conf.json)
npm run lint
npx ng test --watch=false
npm run build                  # production build in dist/tma-web
```

Structure: `src/app/core` (HTTP setup, error handling, layout), `src/app/features/*` (lazy-loaded pages).
See the repository [README](../README.md) and [docs/architecture.md](../docs/architecture.md) §7.
