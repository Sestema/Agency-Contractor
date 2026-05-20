-- Agency Contractor multi-user foundation schema.
--
-- Safe-by-design notes:
-- - This script is not wired into the desktop app and is not executed automatically.
-- - All new tables use CREATE TABLE IF NOT EXISTS.
-- - Existing activity log columns are extended only with nullable fields.
-- - No existing salary, employee, template, company, or settings behavior depends on this schema yet.
-- - Permission enforcement must stay behind feature flags until tested.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS app;

-- One customer/company workspace.
CREATE TABLE IF NOT EXISTS app.tenants (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    supabase_client_id text NULL,
    name text NOT NULL,
    owner_user_id uuid NULL,
    license_key text NULL,
    plan_key text NOT NULL DEFAULT 'standard_1pc',
    max_users integer NOT NULL DEFAULT 1,
    max_devices integer NOT NULL DEFAULT 1,
    ai_enabled boolean NOT NULL DEFAULT false,
    multi_user_enabled boolean NOT NULL DEFAULT false,
    postgres_enabled boolean NOT NULL DEFAULT false,
    web_panel_enabled boolean NOT NULL DEFAULT false,
    exports_enabled boolean NOT NULL DEFAULT true,
    all_modules_enabled boolean NOT NULL DEFAULT false,
    license_expires_at timestamptz NULL,
    last_policy_sync_at timestamptz NULL,
    status text NOT NULL DEFAULT 'active',
    created_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    updated_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    CONSTRAINT chk_tenants_status CHECK (status IN ('trial', 'active', 'suspended', 'expired', 'blocked')),
    CONSTRAINT chk_tenants_plan_key CHECK (plan_key IN ('standard_1pc', 'ultimate_1pc', 'business')),
    CONSTRAINT chk_tenants_max_users CHECK (max_users > 0),
    CONSTRAINT chk_tenants_max_devices CHECK (max_devices > 0)
);

CREATE INDEX IF NOT EXISTS ix_tenants_status
    ON app.tenants (status);

CREATE INDEX IF NOT EXISTS ix_tenants_license_key
    ON app.tenants (license_key);

CREATE INDEX IF NOT EXISTS ix_tenants_supabase_client_id
    ON app.tenants (supabase_client_id);

CREATE INDEX IF NOT EXISTS ix_tenants_plan_key
    ON app.tenants (plan_key);

-- Workspace user. This extends the current ClientProfileRecord idea without replacing it.
CREATE TABLE IF NOT EXISTS app.tenant_users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES app.tenants(id) ON DELETE CASCADE,
    client_id text NULL,
    first_name text NOT NULL,
    last_name text NOT NULL,
    email text NULL,
    password_hash text NULL,
    password_salt text NULL,
    role_key text NOT NULL DEFAULT 'owner',
    is_active boolean NOT NULL DEFAULT true,
    invited_by_user_id uuid NULL,
    last_seen_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    updated_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    CONSTRAINT chk_tenant_users_role_key CHECK (btrim(role_key) <> '')
);

CREATE INDEX IF NOT EXISTS ix_tenant_users_tenant_id
    ON app.tenant_users (tenant_id);

CREATE INDEX IF NOT EXISTS ix_tenant_users_client_id
    ON app.tenant_users (client_id);

CREATE INDEX IF NOT EXISTS ix_tenant_users_email
    ON app.tenant_users (lower(email))
    WHERE email IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_users_tenant_email
    ON app.tenant_users (tenant_id, lower(email))
    WHERE email IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_users_tenant_client_id
    ON app.tenant_users (tenant_id, client_id)
    WHERE client_id IS NOT NULL;

-- Default and tenant-specific role permissions.
-- tenant_id NULL means system default permission for that role.
CREATE TABLE IF NOT EXISTS app.tenant_role_permissions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL REFERENCES app.tenants(id) ON DELETE CASCADE,
    role_key text NOT NULL,
    permission_key text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    CONSTRAINT chk_role_permissions_role_key CHECK (btrim(role_key) <> ''),
    CONSTRAINT chk_role_permissions_permission_key CHECK (btrim(permission_key) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_role_permissions_scope_role_permission
    ON app.tenant_role_permissions (COALESCE(tenant_id::text, 'system'), role_key, permission_key);

CREATE INDEX IF NOT EXISTS ix_role_permissions_tenant_role
    ON app.tenant_role_permissions (tenant_id, role_key);

-- Invite code flow: Owner invites another user into the same workspace.
CREATE TABLE IF NOT EXISTS app.tenant_invites (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES app.tenants(id) ON DELETE CASCADE,
    invite_code text NOT NULL UNIQUE,
    email text NULL,
    role_key text NOT NULL,
    created_by_user_id uuid NOT NULL REFERENCES app.tenant_users(id),
    accepted_by_user_id uuid NULL REFERENCES app.tenant_users(id),
    expires_at timestamptz NOT NULL,
    accepted_at timestamptz NULL,
    revoked_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    CONSTRAINT chk_tenant_invites_role_key CHECK (btrim(role_key) <> ''),
    CONSTRAINT chk_tenant_invites_code CHECK (btrim(invite_code) <> '')
);

CREATE INDEX IF NOT EXISTS ix_tenant_invites_tenant_id
    ON app.tenant_invites (tenant_id);

CREATE INDEX IF NOT EXISTS ix_tenant_invites_email
    ON app.tenant_invites (lower(email))
    WHERE email IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_tenant_invites_open
    ON app.tenant_invites (tenant_id, expires_at)
    WHERE accepted_at IS NULL AND revoked_at IS NULL;

-- User/device session tracking for online users and revocation.
CREATE TABLE IF NOT EXISTS app.tenant_user_sessions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES app.tenants(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES app.tenant_users(id) ON DELETE CASCADE,
    machine_id text NOT NULL,
    machine_name text NULL,
    windows_user text NULL,
    app_version text NULL,
    ip_address text NULL,
    started_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    last_seen_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    revoked_at timestamptz NULL,
    ended_at timestamptz NULL,
    CONSTRAINT chk_user_sessions_machine_id CHECK (btrim(machine_id) <> '')
);

CREATE INDEX IF NOT EXISTS ix_user_sessions_tenant_user
    ON app.tenant_user_sessions (tenant_id, user_id);

CREATE INDEX IF NOT EXISTS ix_user_sessions_machine
    ON app.tenant_user_sessions (tenant_id, machine_id);

CREATE INDEX IF NOT EXISTS ix_user_sessions_active
    ON app.tenant_user_sessions (tenant_id, last_seen_at)
    WHERE ended_at IS NULL AND revoked_at IS NULL;

-- Backward-compatible activity/audit metadata.
ALTER TABLE IF EXISTS app.activity_log
    ADD COLUMN IF NOT EXISTS tenant_id uuid NULL,
    ADD COLUMN IF NOT EXISTS actor_user_id uuid NULL,
    ADD COLUMN IF NOT EXISTS session_id uuid NULL,
    ADD COLUMN IF NOT EXISTS machine_id text NULL,
    ADD COLUMN IF NOT EXISTS entity_type text NULL,
    ADD COLUMN IF NOT EXISTS entity_id text NULL,
    ADD COLUMN IF NOT EXISTS old_values_json text NULL,
    ADD COLUMN IF NOT EXISTS new_values_json text NULL;

CREATE INDEX IF NOT EXISTS ix_activity_log_tenant_id
    ON app.activity_log (tenant_id);

CREATE INDEX IF NOT EXISTS ix_activity_log_actor_user_id
    ON app.activity_log (actor_user_id);

CREATE INDEX IF NOT EXISTS ix_activity_log_session_id
    ON app.activity_log (session_id);

CREATE INDEX IF NOT EXISTS ix_activity_log_entity
    ON app.activity_log (entity_type, entity_id);

-- Add foreign keys after both sides exist. DO blocks keep this script re-runnable.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_tenants_owner_user'
          AND conrelid = 'app.tenants'::regclass
    ) THEN
        ALTER TABLE app.tenants
            ADD CONSTRAINT fk_tenants_owner_user
            FOREIGN KEY (owner_user_id)
            REFERENCES app.tenant_users(id)
            ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_tenant_users_invited_by'
          AND conrelid = 'app.tenant_users'::regclass
    ) THEN
        ALTER TABLE app.tenant_users
            ADD CONSTRAINT fk_tenant_users_invited_by
            FOREIGN KEY (invited_by_user_id)
            REFERENCES app.tenant_users(id)
            ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'app'
          AND table_name = 'activity_log'
    ) THEN
        IF NOT EXISTS (
            SELECT 1
            FROM pg_constraint
            WHERE conname = 'fk_activity_log_tenant'
              AND conrelid = 'app.activity_log'::regclass
        ) THEN
            ALTER TABLE app.activity_log
                ADD CONSTRAINT fk_activity_log_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES app.tenants(id)
                ON DELETE SET NULL;
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM pg_constraint
            WHERE conname = 'fk_activity_log_actor_user'
              AND conrelid = 'app.activity_log'::regclass
        ) THEN
            ALTER TABLE app.activity_log
                ADD CONSTRAINT fk_activity_log_actor_user
                FOREIGN KEY (actor_user_id)
                REFERENCES app.tenant_users(id)
                ON DELETE SET NULL;
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM pg_constraint
            WHERE conname = 'fk_activity_log_session'
              AND conrelid = 'app.activity_log'::regclass
        ) THEN
            ALTER TABLE app.activity_log
                ADD CONSTRAINT fk_activity_log_session
                FOREIGN KEY (session_id)
                REFERENCES app.tenant_user_sessions(id)
                ON DELETE SET NULL;
        END IF;
    END IF;
END $$;

-- System default permissions. Tenant-specific overrides can be added later.
INSERT INTO app.tenant_role_permissions (tenant_id, role_key, permission_key)
VALUES
    (NULL, 'owner', 'employees.view'),
    (NULL, 'owner', 'employees.create'),
    (NULL, 'owner', 'employees.edit'),
    (NULL, 'owner', 'employees.archive'),
    (NULL, 'owner', 'employees.restore'),
    (NULL, 'owner', 'employees.delete'),
    (NULL, 'owner', 'employees.history.view'),
    (NULL, 'owner', 'employees.history.edit'),
    (NULL, 'owner', 'documents.view'),
    (NULL, 'owner', 'documents.add'),
    (NULL, 'owner', 'documents.replace'),
    (NULL, 'owner', 'documents.delete'),
    (NULL, 'owner', 'documents.generate'),
    (NULL, 'owner', 'documents.bulk_generate'),
    (NULL, 'owner', 'documents.ai_scan'),
    (NULL, 'owner', 'templates.view'),
    (NULL, 'owner', 'templates.create'),
    (NULL, 'owner', 'templates.edit'),
    (NULL, 'owner', 'templates.delete'),
    (NULL, 'owner', 'templates.copy'),
    (NULL, 'owner', 'templates.ai_tags'),
    (NULL, 'owner', 'salary.view'),
    (NULL, 'owner', 'salary.edit'),
    (NULL, 'owner', 'salary.mark_paid'),
    (NULL, 'owner', 'salary.export'),
    (NULL, 'owner', 'salary.advances.edit'),
    (NULL, 'owner', 'salary.expenses.edit'),
    (NULL, 'owner', 'finance.custom_fields.edit'),
    (NULL, 'owner', 'companies.view'),
    (NULL, 'owner', 'companies.create'),
    (NULL, 'owner', 'companies.edit'),
    (NULL, 'owner', 'companies.delete'),
    (NULL, 'owner', 'agencies.view'),
    (NULL, 'owner', 'agencies.edit'),
    (NULL, 'owner', 'reports.view'),
    (NULL, 'owner', 'reports.export'),
    (NULL, 'owner', 'reports.configure'),
    (NULL, 'owner', 'settings.view'),
    (NULL, 'owner', 'settings.edit'),
    (NULL, 'owner', 'settings.postgres_admin'),
    (NULL, 'owner', 'settings.web_panel_admin'),
    (NULL, 'owner', 'users.view'),
    (NULL, 'owner', 'users.invite'),
    (NULL, 'owner', 'users.edit'),
    (NULL, 'owner', 'users.disable'),
    (NULL, 'owner', 'sessions.view'),
    (NULL, 'owner', 'sessions.revoke'),
    (NULL, 'owner', 'audit.view'),
    (NULL, 'owner', 'license.view'),
    (NULL, 'owner', 'license.manage'),

    (NULL, 'admin', 'employees.view'),
    (NULL, 'admin', 'employees.create'),
    (NULL, 'admin', 'employees.edit'),
    (NULL, 'admin', 'employees.archive'),
    (NULL, 'admin', 'employees.restore'),
    (NULL, 'admin', 'employees.history.view'),
    (NULL, 'admin', 'documents.view'),
    (NULL, 'admin', 'documents.add'),
    (NULL, 'admin', 'documents.replace'),
    (NULL, 'admin', 'documents.generate'),
    (NULL, 'admin', 'documents.bulk_generate'),
    (NULL, 'admin', 'documents.ai_scan'),
    (NULL, 'admin', 'templates.view'),
    (NULL, 'admin', 'templates.create'),
    (NULL, 'admin', 'templates.edit'),
    (NULL, 'admin', 'templates.copy'),
    (NULL, 'admin', 'templates.ai_tags'),
    (NULL, 'admin', 'salary.view'),
    (NULL, 'admin', 'salary.edit'),
    (NULL, 'admin', 'salary.mark_paid'),
    (NULL, 'admin', 'salary.export'),
    (NULL, 'admin', 'salary.advances.edit'),
    (NULL, 'admin', 'salary.expenses.edit'),
    (NULL, 'admin', 'finance.custom_fields.edit'),
    (NULL, 'admin', 'companies.view'),
    (NULL, 'admin', 'companies.create'),
    (NULL, 'admin', 'companies.edit'),
    (NULL, 'admin', 'agencies.view'),
    (NULL, 'admin', 'agencies.edit'),
    (NULL, 'admin', 'reports.view'),
    (NULL, 'admin', 'reports.export'),
    (NULL, 'admin', 'reports.configure'),
    (NULL, 'admin', 'settings.view'),
    (NULL, 'admin', 'users.view'),
    (NULL, 'admin', 'users.invite'),
    (NULL, 'admin', 'sessions.view'),
    (NULL, 'admin', 'audit.view'),
    (NULL, 'admin', 'license.view'),

    (NULL, 'hr', 'employees.view'),
    (NULL, 'hr', 'employees.create'),
    (NULL, 'hr', 'employees.edit'),
    (NULL, 'hr', 'employees.archive'),
    (NULL, 'hr', 'employees.restore'),
    (NULL, 'hr', 'employees.history.view'),
    (NULL, 'hr', 'employees.history.edit'),
    (NULL, 'hr', 'documents.view'),
    (NULL, 'hr', 'documents.add'),
    (NULL, 'hr', 'documents.replace'),
    (NULL, 'hr', 'documents.generate'),
    (NULL, 'hr', 'documents.bulk_generate'),
    (NULL, 'hr', 'documents.ai_scan'),
    (NULL, 'hr', 'templates.view'),
    (NULL, 'hr', 'templates.create'),
    (NULL, 'hr', 'templates.edit'),
    (NULL, 'hr', 'templates.copy'),
    (NULL, 'hr', 'templates.ai_tags'),
    (NULL, 'hr', 'companies.view'),
    (NULL, 'hr', 'agencies.view'),
    (NULL, 'hr', 'reports.view'),
    (NULL, 'hr', 'reports.export'),

    (NULL, 'accountant', 'employees.view'),
    (NULL, 'accountant', 'salary.view'),
    (NULL, 'accountant', 'salary.edit'),
    (NULL, 'accountant', 'salary.mark_paid'),
    (NULL, 'accountant', 'salary.export'),
    (NULL, 'accountant', 'salary.advances.edit'),
    (NULL, 'accountant', 'salary.expenses.edit'),
    (NULL, 'accountant', 'finance.custom_fields.edit'),
    (NULL, 'accountant', 'companies.view'),
    (NULL, 'accountant', 'agencies.view'),
    (NULL, 'accountant', 'reports.view'),
    (NULL, 'accountant', 'reports.export'),

    (NULL, 'manager', 'employees.view'),
    (NULL, 'manager', 'documents.view'),
    (NULL, 'manager', 'companies.view'),
    (NULL, 'manager', 'agencies.view'),
    (NULL, 'manager', 'reports.view'),

    (NULL, 'viewer', 'employees.view'),
    (NULL, 'viewer', 'documents.view'),
    (NULL, 'viewer', 'companies.view'),
    (NULL, 'viewer', 'agencies.view'),
    (NULL, 'viewer', 'reports.view')
ON CONFLICT DO NOTHING;

CREATE OR REPLACE FUNCTION app.touch_updated_at_utc()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = timezone('utc', now());
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_tenants_updated_at ON app.tenants;
CREATE TRIGGER trg_tenants_updated_at
BEFORE UPDATE ON app.tenants
FOR EACH ROW
EXECUTE FUNCTION app.touch_updated_at_utc();

DROP TRIGGER IF EXISTS trg_tenant_users_updated_at ON app.tenant_users;
CREATE TRIGGER trg_tenant_users_updated_at
BEFORE UPDATE ON app.tenant_users
FOR EACH ROW
EXECUTE FUNCTION app.touch_updated_at_utc();
