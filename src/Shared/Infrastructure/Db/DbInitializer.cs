namespace EasyWorkTogether.Api.Shared.Infrastructure.Db;

public sealed class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await db.OpenConnectionAsync();

        const string sql = """
            CREATE TABLE IF NOT EXISTS users (
                id SERIAL PRIMARY KEY,
                email VARCHAR(255) UNIQUE NOT NULL,
                password_hash TEXT NOT NULL,
                name VARCHAR(100) NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                email_verified_at TIMESTAMPTZ
            );
            ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verified_at TIMESTAMPTZ;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS avatar_url TEXT;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS public_user_id VARCHAR(32);
            ALTER TABLE users ADD COLUMN IF NOT EXISTS is_system_admin BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS system_admin_granted_at TIMESTAMPTZ;

            CREATE TABLE IF NOT EXISTS sessions (
                id SERIAL PRIMARY KEY,
                token UUID UNIQUE NOT NULL,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at TIMESTAMPTZ NOT NULL
            );
            ALTER TABLE sessions ADD COLUMN IF NOT EXISTS token UUID;
            ALTER TABLE sessions ADD COLUMN IF NOT EXISTS user_id INT;
            ALTER TABLE sessions ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            ALTER TABLE sessions ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ;
            UPDATE sessions
            SET token = (
                substr(md5(id::text || clock_timestamp()::text || random()::text), 1, 8) || '-' ||
                substr(md5(id::text || clock_timestamp()::text || random()::text), 9, 4) || '-' ||
                substr(md5(id::text || clock_timestamp()::text || random()::text), 13, 4) || '-' ||
                substr(md5(id::text || clock_timestamp()::text || random()::text), 17, 4) || '-' ||
                substr(md5(id::text || clock_timestamp()::text || random()::text), 21, 12)
            )::uuid
            WHERE token IS NULL;
            UPDATE sessions SET expires_at = NOW() + INTERVAL '30 days' WHERE expires_at IS NULL;
            DELETE FROM sessions WHERE expires_at <= NOW();

            CREATE TABLE IF NOT EXISTS password_reset_tokens (
                id SERIAL PRIMARY KEY,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token_hash VARCHAR(128) UNIQUE NOT NULL,
                expires_at TIMESTAMPTZ NOT NULL,
                used_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE password_reset_tokens ADD COLUMN IF NOT EXISTS user_id INT;
            ALTER TABLE password_reset_tokens ADD COLUMN IF NOT EXISTS token_hash VARCHAR(128);
            ALTER TABLE password_reset_tokens ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ;
            ALTER TABLE password_reset_tokens ADD COLUMN IF NOT EXISTS used_at TIMESTAMPTZ;
            ALTER TABLE password_reset_tokens ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

            CREATE TABLE IF NOT EXISTS email_verification_tokens (
                id SERIAL PRIMARY KEY,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token_hash VARCHAR(128) UNIQUE NOT NULL,
                expires_at TIMESTAMPTZ NOT NULL,
                used_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE email_verification_tokens ADD COLUMN IF NOT EXISTS user_id INT;
            ALTER TABLE email_verification_tokens ADD COLUMN IF NOT EXISTS token_hash VARCHAR(128);
            ALTER TABLE email_verification_tokens ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ;
            ALTER TABLE email_verification_tokens ADD COLUMN IF NOT EXISTS used_at TIMESTAMPTZ;
            ALTER TABLE email_verification_tokens ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

            CREATE TABLE IF NOT EXISTS external_identities (
                id SERIAL PRIMARY KEY,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                provider VARCHAR(20) NOT NULL,
                provider_user_id VARCHAR(255) NOT NULL,
                provider_email VARCHAR(255),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_login_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (provider, provider_user_id),
                UNIQUE (user_id, provider)
            );
            ALTER TABLE external_identities ADD COLUMN IF NOT EXISTS user_id INT;
            ALTER TABLE external_identities ADD COLUMN IF NOT EXISTS provider VARCHAR(20);
            ALTER TABLE external_identities ADD COLUMN IF NOT EXISTS provider_user_id VARCHAR(255);
            ALTER TABLE external_identities ADD COLUMN IF NOT EXISTS provider_email VARCHAR(255);
            ALTER TABLE external_identities ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            ALTER TABLE external_identities ADD COLUMN IF NOT EXISTS last_login_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

            CREATE TABLE IF NOT EXISTS workspaces (
                id SERIAL PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                owner_id INT NOT NULL REFERENCES users(id),
                domain_namespace VARCHAR(80),
                industry_vertical VARCHAR(80),
                workspace_logo_data TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE workspaces ADD COLUMN IF NOT EXISTS domain_namespace VARCHAR(80);
            ALTER TABLE workspaces ADD COLUMN IF NOT EXISTS industry_vertical VARCHAR(80);
            ALTER TABLE workspaces ADD COLUMN IF NOT EXISTS workspace_logo_data TEXT;
            ALTER TABLE workspaces ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

            CREATE TABLE IF NOT EXISTS workspace_members (
                workspace_id INT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                role VARCHAR(10) NOT NULL CHECK (role IN ('owner', 'admin', 'member')),
                joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (workspace_id, user_id)
            );
            ALTER TABLE workspace_members ADD COLUMN IF NOT EXISTS joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            ALTER TABLE workspace_members DROP CONSTRAINT IF EXISTS workspace_members_role_check;
            ALTER TABLE workspace_members ADD CONSTRAINT workspace_members_role_check CHECK (role IN ('owner', 'admin', 'member'));

            CREATE TABLE IF NOT EXISTS workspace_invitations (
                id SERIAL PRIMARY KEY,
                workspace_id INT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                inviter_id INT NOT NULL REFERENCES users(id),
                invitee_email VARCHAR(255) NOT NULL,
                code VARCHAR(64) UNIQUE NOT NULL,
                role VARCHAR(80),
                expires_at TIMESTAMPTZ NOT NULL,
                responded_at TIMESTAMPTZ,
                status VARCHAR(20) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'accepted', 'declined', 'revoked')),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE workspace_invitations ADD COLUMN IF NOT EXISTS role VARCHAR(80);
            ALTER TABLE workspace_invitations ADD COLUMN IF NOT EXISTS responded_at TIMESTAMPTZ;
            ALTER TABLE workspace_invitations ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'pending';
            ALTER TABLE workspace_invitations ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            UPDATE workspace_invitations
            SET role = 'Team Member'
            WHERE role IS NULL OR BTRIM(role) = '';
            ALTER TABLE workspace_invitations DROP CONSTRAINT IF EXISTS workspace_invitations_status_check;
            ALTER TABLE workspace_invitations ADD CONSTRAINT workspace_invitations_status_check CHECK (status IN ('pending', 'accepted', 'declined', 'revoked'));

            CREATE TABLE IF NOT EXISTS tasks (
                id SERIAL PRIMARY KEY,
                workspace_id INT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                sku VARCHAR(120),
                title VARCHAR(255) NOT NULL,
                description TEXT,
                due_date DATE,
                due_at TIMESTAMPTZ,
                story_points INT,
                priority VARCHAR(20) NOT NULL DEFAULT 'medium' CHECK (priority IN ('low', 'medium', 'high', 'urgent')),
                status VARCHAR(20) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'in_progress', 'completed')),
                created_by INT NOT NULL REFERENCES users(id),
                assignee_id INT REFERENCES users(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS sku VARCHAR(120);
            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS story_points INT;
            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS priority VARCHAR(20) NOT NULL DEFAULT 'medium';
            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS due_at TIMESTAMPTZ;
            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            UPDATE tasks SET priority = 'medium' WHERE priority IS NULL;
            UPDATE tasks
            SET due_at = COALESCE(
                due_at,
                CASE
                    WHEN due_date IS NULL THEN NULL
                    ELSE ((due_date::timestamp + INTERVAL '23 hours 59 minutes') AT TIME ZONE 'UTC')
                END
            )
            WHERE due_at IS NULL AND due_date IS NOT NULL;
            UPDATE tasks SET sku = 'TASK-' || id WHERE sku IS NULL OR BTRIM(sku) = '';
            ALTER TABLE tasks DROP CONSTRAINT IF EXISTS tasks_priority_check;
            ALTER TABLE tasks ADD CONSTRAINT tasks_priority_check CHECK (priority IN ('low', 'medium', 'high', 'urgent'));
            ALTER TABLE tasks DROP CONSTRAINT IF EXISTS tasks_story_points_check;
            ALTER TABLE tasks ADD CONSTRAINT tasks_story_points_check CHECK (story_points IS NULL OR story_points >= 0);

            CREATE TABLE IF NOT EXISTS task_story_point_votes (
                task_id INT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                points INT NOT NULL CHECK (points >= 0),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (task_id, user_id)
            );
            ALTER TABLE task_story_point_votes ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            ALTER TABLE task_story_point_votes DROP CONSTRAINT IF EXISTS task_story_point_votes_points_check;
            ALTER TABLE task_story_point_votes ADD CONSTRAINT task_story_point_votes_points_check CHECK (points >= 0);

            CREATE UNIQUE INDEX IF NOT EXISTS uq_external_identities_provider_user_id ON external_identities(provider, provider_user_id);
            CREATE UNIQUE INDEX IF NOT EXISTS uq_external_identities_user_provider ON external_identities(user_id, provider);
            CREATE UNIQUE INDEX IF NOT EXISTS uq_users_public_user_id ON users(LOWER(public_user_id));
            CREATE INDEX IF NOT EXISTS idx_sessions_token ON sessions(token);
            CREATE INDEX IF NOT EXISTS idx_sessions_user_expiry ON sessions(user_id, expires_at DESC);
            CREATE INDEX IF NOT EXISTS idx_sessions_expires_at ON sessions(expires_at);
            CREATE UNIQUE INDEX IF NOT EXISTS uq_password_reset_tokens_token_hash ON password_reset_tokens(token_hash);
            CREATE UNIQUE INDEX IF NOT EXISTS uq_email_verification_tokens_token_hash ON email_verification_tokens(token_hash);
            CREATE INDEX IF NOT EXISTS idx_email_verification_tokens_token_hash ON email_verification_tokens(token_hash);
            CREATE INDEX IF NOT EXISTS idx_email_verification_tokens_user_id ON email_verification_tokens(user_id);
            CREATE INDEX IF NOT EXISTS idx_password_reset_tokens_token_hash ON password_reset_tokens(token_hash);
            CREATE INDEX IF NOT EXISTS idx_password_reset_tokens_user_id ON password_reset_tokens(user_id);
            CREATE INDEX IF NOT EXISTS idx_external_identities_provider_user_id ON external_identities(provider, provider_user_id);
            CREATE INDEX IF NOT EXISTS idx_external_identities_user_id ON external_identities(user_id);
            CREATE INDEX IF NOT EXISTS idx_workspace_members_user_id ON workspace_members(user_id);
            CREATE INDEX IF NOT EXISTS idx_workspace_members_workspace_user ON workspace_members(workspace_id, user_id);
            CREATE INDEX IF NOT EXISTS idx_workspaces_domain_namespace ON workspaces(domain_namespace);
            CREATE INDEX IF NOT EXISTS idx_workspace_invitations_workspace_id ON workspace_invitations(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_workspace_invitations_invitee_email_status ON workspace_invitations(invitee_email, status, expires_at);
            CREATE INDEX IF NOT EXISTS idx_tasks_workspace_id ON tasks(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_tasks_workspace_id_desc ON tasks(workspace_id, id DESC);
            CREATE INDEX IF NOT EXISTS idx_tasks_assignee_id ON tasks(assignee_id);
            CREATE INDEX IF NOT EXISTS idx_tasks_priority_due_date ON tasks(priority, due_date);
            CREATE UNIQUE INDEX IF NOT EXISTS uq_tasks_workspace_sku ON tasks(workspace_id, sku);
            CREATE INDEX IF NOT EXISTS idx_task_story_point_votes_task_id ON task_story_point_votes(task_id);
            CREATE INDEX IF NOT EXISTS idx_task_story_point_votes_user_id ON task_story_point_votes(user_id);

            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS is_emergency BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS support_requested_from INT REFERENCES users(id);
            ALTER TABLE tasks ADD COLUMN IF NOT EXISTS completed_at TIMESTAMPTZ;
            UPDATE tasks
            SET completed_at = COALESCE(completed_at, updated_at, created_at)
            WHERE status = 'completed' AND completed_at IS NULL;


            CREATE TABLE IF NOT EXISTS oauth_handoff_codes (
                code VARCHAR(128) PRIMARY KEY,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                session_token UUID NOT NULL REFERENCES sessions(token) ON DELETE CASCADE,
                expires_at TIMESTAMPTZ NOT NULL,
                used_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS ws_tickets (
                ticket VARCHAR(128) PRIMARY KEY,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                expires_at TIMESTAMPTZ NOT NULL,
                used_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS file_assets (
                id SERIAL PRIMARY KEY,
                public_id UUID NOT NULL UNIQUE,
                original_file_name VARCHAR(255) NOT NULL,
                content_type VARCHAR(255) NOT NULL,
                size_bytes BIGINT NOT NULL,
                data BYTEA NOT NULL,
                created_by INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS task_attachments (
                id SERIAL PRIMARY KEY,
                task_id INT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                file_asset_id INT NOT NULL REFERENCES file_assets(id) ON DELETE CASCADE,
                uploaded_by INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                kind VARCHAR(40) NOT NULL DEFAULT 'completion_proof',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS task_activities (
                id SERIAL PRIMARY KEY,
                task_id INT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                user_id INT REFERENCES users(id) ON DELETE SET NULL,
                activity_type VARCHAR(50) NOT NULL,
                message TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS notifications (
                id SERIAL PRIMARY KEY,
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                actor_user_id INT REFERENCES users(id) ON DELETE SET NULL,
                type VARCHAR(50) NOT NULL,
                title VARCHAR(160) NOT NULL,
                message TEXT NOT NULL,
                entity_type VARCHAR(50),
                entity_id INT,
                data_json TEXT,
                dedupe_key VARCHAR(255),
                is_read BOOLEAN NOT NULL DEFAULT FALSE,
                read_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS friend_requests (
                id SERIAL PRIMARY KEY,
                requester_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                receiver_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                status VARCHAR(20) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'accepted', 'declined')),
                responded_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CHECK (requester_id <> receiver_id)
            );

            CREATE TABLE IF NOT EXISTS friendships (
                user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                friend_user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (user_id, friend_user_id),
                CHECK (user_id <> friend_user_id)
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id SERIAL PRIMARY KEY,
                sender_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                receiver_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                content TEXT,
                type VARCHAR(20) NOT NULL DEFAULT 'text' CHECK (type IN ('text', 'image', 'file')),
                file_asset_id INT REFERENCES file_assets(id) ON DELETE SET NULL,
                read_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CHECK (sender_id <> receiver_id)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS uq_notifications_dedupe_key ON notifications(dedupe_key) WHERE dedupe_key IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_friend_requests_pending_pair
                ON friend_requests (LEAST(requester_id, receiver_id), GREATEST(requester_id, receiver_id), status)
                WHERE status = 'pending';
            CREATE INDEX IF NOT EXISTS idx_notifications_user_created_at ON notifications(user_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_notifications_user_unread ON notifications(user_id, is_read, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_friend_requests_receiver_status ON friend_requests(receiver_id, status, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_friend_requests_requester_status ON friend_requests(requester_id, status, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_friendships_friend_user_id ON friendships(friend_user_id);
            CREATE INDEX IF NOT EXISTS idx_users_created_at ON users(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_users_system_admin ON users(is_system_admin) WHERE is_system_admin = TRUE;
            CREATE INDEX IF NOT EXISTS idx_workspaces_created_at ON workspaces(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_workspaces_updated_at ON workspaces(updated_at DESC);
            CREATE INDEX IF NOT EXISTS idx_tasks_created_at ON tasks(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_tasks_status_due_at ON tasks(status, due_at DESC);
            CREATE INDEX IF NOT EXISTS idx_tasks_priority_created_at ON tasks(priority, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_created_at ON chat_messages(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_users_public_user_id ON users(LOWER(public_user_id));
            CREATE INDEX IF NOT EXISTS idx_oauth_handoff_codes_session ON oauth_handoff_codes(session_token, expires_at);
            CREATE INDEX IF NOT EXISTS idx_ws_tickets_user_expires ON ws_tickets(user_id, expires_at);
            CREATE INDEX IF NOT EXISTS idx_file_assets_public_id ON file_assets(public_id);
            CREATE INDEX IF NOT EXISTS idx_task_attachments_task_id ON task_attachments(task_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_task_activities_task_id ON task_activities(task_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_pair_created_at ON chat_messages(sender_id, receiver_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_receiver_unread ON chat_messages(receiver_id, sender_id, read_at, created_at DESC);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();

        var legacyTokenColumnExists = false;
        await using (var columnCmd = new NpgsqlCommand(@"
            SELECT 1
            FROM information_schema.columns
            WHERE table_name = 'password_reset_tokens'
              AND column_name = 'token'
            LIMIT 1;
        ", conn))
        {
            legacyTokenColumnExists = await columnCmd.ExecuteScalarAsync() is not null;
        }

        if (legacyTokenColumnExists)
        {
            await using var legacyTokenReader = new NpgsqlCommand("SELECT id, token FROM password_reset_tokens WHERE token IS NOT NULL AND (token_hash IS NULL OR BTRIM(token_hash) = '')", conn);
            await using (var reader = await legacyTokenReader.ExecuteReaderAsync())
            {
                var rows = new List<(int Id, string Token)>();
                while (await reader.ReadAsync())
                {
                    rows.Add((reader.GetInt32(0), reader.GetString(1)));
                }

                foreach (var row in rows)
                {
                    await using var updateCmd = new NpgsqlCommand("UPDATE password_reset_tokens SET token_hash = @token_hash WHERE id = @id;", conn);
                    updateCmd.Parameters.AddWithValue("token_hash", ComputeSha256(row.Token));
                    updateCmd.Parameters.AddWithValue("id", row.Id);
                    await updateCmd.ExecuteNonQueryAsync();
                }
            }
        }

        var seededUsers = new List<(int Id, string Seed)>();
        await using (var reader = await new NpgsqlCommand("SELECT id, COALESCE(name, email, 'ewt') FROM users WHERE public_user_id IS NULL OR BTRIM(public_user_id) = ''", conn).ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                seededUsers.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        foreach (var user in seededUsers)
        {
            var publicId = await GenerateUniquePublicUserIdAsync(conn, null, user.Seed);
            await using var updateUser = new NpgsqlCommand("UPDATE users SET public_user_id = @public_user_id WHERE id = @id;", conn);
            updateUser.Parameters.AddWithValue("public_user_id", publicId);
            updateUser.Parameters.AddWithValue("id", user.Id);
            await updateUser.ExecuteNonQueryAsync();
        }

        await using (var dropCmd = new NpgsqlCommand(@"
            DROP INDEX IF EXISTS idx_password_reset_tokens_token;
            ALTER TABLE password_reset_tokens DROP COLUMN IF EXISTS token;
        ", conn))
        {
            await dropCmd.ExecuteNonQueryAsync();
        }

        await using (var cleanupCmd = new NpgsqlCommand(@"
            DELETE FROM oauth_handoff_codes WHERE used_at IS NOT NULL OR expires_at <= NOW();
            DELETE FROM ws_tickets WHERE used_at IS NOT NULL OR expires_at <= NOW();
            DELETE FROM sessions WHERE expires_at <= NOW();
        ", conn))
        {
            await cleanupCmd.ExecuteNonQueryAsync();
        }
    }
}

