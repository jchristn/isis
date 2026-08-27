# Isis Dashboard

React 19 / Vite 6 management dashboard for the **Isis** agent-memory platform.

## Stack

- React 19, React Router 7, Vite 6
- Browser `fetch` behind a single `ApiClient` class (`src/utils/api.js`) — no axios
- Hand-rolled SVG charts (`ActivityChart`, `HealthHistogram`) — no chart library
- i18next foundation (`en` + `en-XA` pseudo-locale) with locale-aware formatters

## Auth

The dashboard signs in with a **3-step email/password login**:

1. Enter the server URL and your email; the app calls `POST /v1.0/api/tenants-for-email`
   to list the tenants that email belongs to.
2. Select your tenant by name.
3. Enter your password; the app calls `POST /v1.0/api/token`, which issues a session
   token.

The token is stored in `localStorage` and sent on every request as
`Authorization: Bearer <token>`. Logging out calls `DELETE /v1.0/api/token` to revoke the
session.

Local dev defaults: email `admin@isis.local`, password `isisadmin`, tenant `ten_default`,
server `http://127.0.0.1:8700`.

## Scripts

```bash
npm install
npm run dev      # start Vite dev server (port 8701)
npm run build    # production build to dist/
npm run preview  # preview the production build
npm run lint     # eslint
```

## Project structure

```
src/
  components/   shell + reusable UI (DataTable, Pagination, Modal, Toast, …)
  views/        route targets (Home, Scopes, Memories, Search, Chat, …)
  context/      AuthContext, ThemeContext, AppContext
  hooks/        useApiExplorer, useDebounce, useLocalStorage
  utils/        api.js (ApiClient), openApi.js, constants.js
  i18n/         index.js, localeRegistry.js, resources.js, formatters.js
```

## Routes

Grouped by workflow: Memory (Home, Scopes → categories/memories) · Recall
(Search, Chat with Memory) · Inference (Embedding/Inference endpoints) ·
Collections (RecallDB pass-through) · Observability (Request History, API
Explorer) · System (Settings) · Administration (Tenants).
