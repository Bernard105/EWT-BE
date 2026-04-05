Push this folder to the ROOT of your GitHub repo.
Render will read render.yaml from the repo root.

Optional on first Blueprint creation:
- FRONTEND_BASE_URL=https://your-frontend-domain
- CORS_ALLOWED_ORIGINS=https://your-frontend-domain,http://localhost:5173

Optional for Google/GitHub login:
- OAuth__google__ClientId
- OAuth__google__ClientSecret
- OAuth__github__ClientId
- OAuth__github__ClientSecret

Optional for SMTP email:
- Email__Enabled=true
- Email__FromName
- Email__FromAddress
- Email__SmtpHost
- Email__SmtpPort
- Email__Username
- Email__Password
- Email__UseSsl=true
