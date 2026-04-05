# EasyWorkTogether API

Backend .NET 8 API prepared for local development and production deployment on Render.

## Production behavior in this revision

- finite session TTL with expiry enforcement and expired-session cleanup
- reset-password tokens stored only as `token_hash`
- OAuth providers enabled only when both client id and client secret are present
- OAuth callback now redirects with a short-lived one-time `code`, never the session token
- WebSocket authentication now uses a short-lived one-time `ws_ticket`
- production CORS requires an explicit allowlist; wildcard origins are rejected at startup
- Render dynamic `PORT` binding and forwarded headers support

## Local run

```bash
dotnet restore
dotnet run --project EasyWorkTogether.Api.csproj
```

## Health and docs

- `GET /health`
- `GET /api/status`
- `GET /swagger`
- `GET /swagger/v1/swagger.json`

## Required environment variables for Render

- `DATABASE_URL`
- `FRONTEND_BASE_URL`
- `CORS_ALLOWED_ORIGINS`
- `ASPNETCORE_ENVIRONMENT=Production`
- `PORT` (Render injects this automatically)

### Optional OAuth variables

Only set the providers you want to expose in the UI:

- `OAuth__google__ClientId`
- `OAuth__google__ClientSecret`
- `OAuth__github__ClientId`
- `OAuth__github__ClientSecret`

Provider buttons are hidden automatically unless both values for that provider are present.

### Optional email variables

- `Email__Enabled=true`
- `Email__FromName`
- `Email__FromAddress`
- `Email__SmtpHost`
- `Email__SmtpPort`
- `Email__Username`
- `Email__Password`
- `Email__UseSsl=true`

If SMTP is not configured, forgot-password still works and the API can return the reset link directly.

## OAuth callback URLs

Configure these exact callback URLs in Google and GitHub:

- `https://YOUR-RENDER-SERVICE.onrender.com/api/oauth/google/callback`
- `https://YOUR-RENDER-SERVICE.onrender.com/api/oauth/github/callback`

## Render deployment notes

- Use the included `render.yaml` blueprint or an equivalent Docker web service.
- `FRONTEND_BASE_URL` must point at the Vercel production domain.
- `CORS_ALLOWED_ORIGINS` must be a comma-separated allowlist such as:
  - `https://your-frontend.vercel.app`
  - `https://your-preview-domain.vercel.app,https://your-frontend.vercel.app`
- Do not use `*` in production.
- Password reset links and OAuth return redirects resolve through `FRONTEND_BASE_URL`.
- WebSocket connections should target the same backend base URL and obtain a `ws_ticket` from `/api/realtime/ws-ticket` before connecting.

## Platform admin bootstrap

- Platform admin access is now separate from workspace admin.
- `SYSTEM_ADMIN_EMAILS` promotes matching existing or newly registered users to **system admin**.
- If `ALLOW_FIRST_USER_SYSTEM_ADMIN_BOOTSTRAP=true`, the first registered user on a fresh instance becomes a system admin automatically. Default: `true` in Development, `false` in Production.
- System admin APIs live under `/api/admin/*` and are guarded by platform-level permission checks.
