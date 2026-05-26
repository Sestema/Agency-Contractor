-- Client workspace session RPC helpers for Business multi-user / OneDrive shared folders.
-- Run once in Supabase SQL Editor after multi_user_foundation_schema.sql and admin_tenant_rpc.sql.

CREATE TABLE IF NOT EXISTS app.workspace_device_sessions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL REFERENCES app.tenants(id) ON DELETE CASCADE,
    workspace_id text NOT NULL,
    owner_client_id text NOT NULL,
    machine_id text NOT NULL,
    machine_name text NULL,
    windows_user text NOT NULL DEFAULT '',
    actor_kind text NOT NULL DEFAULT 'owner',
    local_user_id text NULL,
    actor_display_name text NULL,
    app_version text NULL,
    ip_address text NULL,
    started_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    last_seen_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
    ended_at timestamptz NULL,
    CONSTRAINT chk_workspace_device_sessions_actor_kind
        CHECK (actor_kind IN ('owner', 'member')),
    CONSTRAINT chk_workspace_device_sessions_workspace_id
        CHECK (btrim(workspace_id) <> ''),
    CONSTRAINT chk_workspace_device_sessions_owner_client_id
        CHECK (btrim(owner_client_id) <> ''),
    CONSTRAINT chk_workspace_device_sessions_machine_id
        CHECK (btrim(machine_id) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_workspace_device_sessions_identity
    ON app.workspace_device_sessions (workspace_id, machine_id, windows_user);

CREATE INDEX IF NOT EXISTS ix_workspace_device_sessions_workspace
    ON app.workspace_device_sessions (workspace_id, last_seen_at DESC);

CREATE INDEX IF NOT EXISTS ix_workspace_device_sessions_owner
    ON app.workspace_device_sessions (owner_client_id, last_seen_at DESC);

CREATE INDEX IF NOT EXISTS ix_workspace_device_sessions_tenant
    ON app.workspace_device_sessions (tenant_id, last_seen_at DESC)
    WHERE tenant_id IS NOT NULL;

CREATE OR REPLACE FUNCTION public.client_upsert_workspace_device_session(
    p_owner_client_id text,
    p_workspace_id text,
    p_machine_id text,
    p_machine_name text DEFAULT NULL,
    p_windows_user text DEFAULT NULL,
    p_actor_kind text DEFAULT 'owner',
    p_local_user_id text DEFAULT NULL,
    p_actor_display_name text DEFAULT NULL,
    p_app_version text DEFAULT NULL,
    p_ip_address text DEFAULT NULL,
    p_tenant_id text DEFAULT NULL
)
RETURNS json
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = app, public
AS $$
DECLARE
    v_owner_client_id text := btrim(coalesce(p_owner_client_id, ''));
    v_workspace_id text := btrim(coalesce(p_workspace_id, ''));
    v_machine_id text := btrim(coalesce(p_machine_id, ''));
    v_actor_kind text := lower(btrim(coalesce(p_actor_kind, 'owner')));
    v_client_plan text;
    v_tenant app.tenants%ROWTYPE;
    v_tenant_id uuid;
    v_active_devices integer;
    v_session app.workspace_device_sessions%ROWTYPE;
    v_now timestamptz := timezone('utc', now());
BEGIN
    IF v_owner_client_id = '' OR v_workspace_id = '' OR v_machine_id = '' THEN
        RAISE EXCEPTION 'workspace_session_identity_required';
    END IF;

    IF v_actor_kind NOT IN ('owner', 'member') THEN
        RAISE EXCEPTION 'invalid_actor_kind';
    END IF;

    SELECT lower(coalesce(plan, 'trial'))
    INTO v_client_plan
    FROM public.clients
    WHERE id = v_owner_client_id::uuid
    LIMIT 1;

    IF coalesce(v_client_plan, 'trial') <> 'business' THEN
        RETURN json_build_object(
            'ok', false,
            'error', 'business_plan_required'
        );
    END IF;

    SELECT *
    INTO v_tenant
    FROM app.tenants
    WHERE supabase_client_id = v_owner_client_id
    LIMIT 1;

    IF v_tenant.id IS NULL THEN
        RETURN json_build_object(
            'ok', false,
            'error', 'tenant_not_found'
        );
    END IF;

    v_tenant_id := v_tenant.id;

    IF coalesce(v_tenant.multi_user_enabled, false) = false THEN
        RETURN json_build_object(
            'ok', false,
            'error', 'multi_user_disabled'
        );
    END IF;

    SELECT COUNT(DISTINCT s.machine_id)
    INTO v_active_devices
    FROM app.workspace_device_sessions s
    WHERE s.workspace_id = v_workspace_id
      AND s.owner_client_id = v_owner_client_id
      AND s.ended_at IS NULL
      AND s.last_seen_at >= v_now - interval '3 minutes'
      AND NOT (
          s.machine_id = v_machine_id
          AND s.windows_user = coalesce(btrim(p_windows_user), '')
      );

    IF v_active_devices >= v_tenant.max_devices THEN
        RETURN json_build_object(
            'ok', false,
            'error', 'device_limit_reached',
            'max_devices', v_tenant.max_devices,
            'active_devices', v_active_devices
        );
    END IF;

    INSERT INTO app.workspace_device_sessions (
        tenant_id,
        workspace_id,
        owner_client_id,
        machine_id,
        machine_name,
        windows_user,
        actor_kind,
        local_user_id,
        actor_display_name,
        app_version,
        ip_address,
        started_at,
        last_seen_at,
        ended_at
    ) VALUES (
        v_tenant_id,
        v_workspace_id,
        v_owner_client_id,
        v_machine_id,
        nullif(btrim(p_machine_name), ''),
        coalesce(nullif(btrim(p_windows_user), ''), ''),
        v_actor_kind,
        nullif(btrim(p_local_user_id), ''),
        nullif(btrim(p_actor_display_name), ''),
        nullif(btrim(p_app_version), ''),
        nullif(btrim(p_ip_address), ''),
        v_now,
        v_now,
        NULL
    )
    ON CONFLICT (workspace_id, machine_id, windows_user)
    DO UPDATE SET
        tenant_id = EXCLUDED.tenant_id,
        owner_client_id = EXCLUDED.owner_client_id,
        machine_name = EXCLUDED.machine_name,
        actor_kind = EXCLUDED.actor_kind,
        local_user_id = EXCLUDED.local_user_id,
        actor_display_name = EXCLUDED.actor_display_name,
        app_version = EXCLUDED.app_version,
        ip_address = EXCLUDED.ip_address,
        last_seen_at = EXCLUDED.last_seen_at,
        ended_at = NULL
    RETURNING * INTO v_session;

    RETURN json_build_object(
        'ok', true,
        'session_id', v_session.id,
        'tenant_id', v_session.tenant_id,
        'workspace_id', v_session.workspace_id,
        'max_devices', v_tenant.max_devices,
        'active_devices', v_active_devices + 1
    );
END;
$$;

CREATE OR REPLACE FUNCTION public.client_get_workspace_device_status(
    p_owner_client_id text,
    p_workspace_id text DEFAULT NULL
)
RETURNS json
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = app, public
AS $$
DECLARE
    v_owner_client_id text := btrim(coalesce(p_owner_client_id, ''));
    v_workspace_id text := nullif(btrim(coalesce(p_workspace_id, '')), '');
    v_tenant app.tenants%ROWTYPE;
    v_now timestamptz := timezone('utc', now());
    v_users json;
    v_sessions json;
    v_active_devices integer;
BEGIN
    IF v_owner_client_id = '' THEN
        RAISE EXCEPTION 'owner_client_id_required';
    END IF;

    SELECT *
    INTO v_tenant
    FROM app.tenants
    WHERE supabase_client_id = v_owner_client_id
    LIMIT 1;

    IF v_tenant.id IS NULL THEN
        RETURN json_build_object(
            'ok', false,
            'error', 'tenant_not_found'
        );
    END IF;

    SELECT COUNT(DISTINCT s.machine_id)
    INTO v_active_devices
    FROM app.workspace_device_sessions s
    WHERE s.owner_client_id = v_owner_client_id
      AND (v_workspace_id IS NULL OR s.workspace_id = v_workspace_id)
      AND s.ended_at IS NULL
      AND s.last_seen_at >= v_now - interval '3 minutes';

    SELECT coalesce(json_agg(row_to_json(q)), '[]'::json)
    INTO v_sessions
    FROM (
        SELECT
            s.workspace_id,
            s.machine_id,
            s.machine_name,
            s.windows_user,
            s.actor_kind,
            s.local_user_id,
            s.actor_display_name,
            s.app_version,
            s.ip_address,
            s.started_at,
            s.last_seen_at,
            (s.ended_at IS NULL AND s.last_seen_at >= v_now - interval '3 minutes') AS is_online
        FROM app.workspace_device_sessions s
        WHERE s.owner_client_id = v_owner_client_id
          AND (v_workspace_id IS NULL OR s.workspace_id = v_workspace_id)
        ORDER BY s.last_seen_at DESC
        LIMIT 100
    ) q;

    SELECT coalesce(json_agg(row_to_json(q)), '[]'::json)
    INTO v_users
    FROM (
        SELECT
            s.local_user_id,
            s.actor_kind,
            max(s.actor_display_name) AS display_name,
            max(s.last_seen_at) AS last_seen_at,
            COUNT(DISTINCT s.machine_id) FILTER (
                WHERE s.ended_at IS NULL AND s.last_seen_at >= v_now - interval '3 minutes'
            ) AS devices_online,
            COUNT(DISTINCT s.machine_id) AS devices_total
        FROM app.workspace_device_sessions s
        WHERE s.owner_client_id = v_owner_client_id
          AND (v_workspace_id IS NULL OR s.workspace_id = v_workspace_id)
        GROUP BY s.local_user_id, s.actor_kind
        ORDER BY max(s.last_seen_at) DESC NULLS LAST
    ) q;

    RETURN json_build_object(
        'ok', true,
        'tenant', json_build_object(
            'id', v_tenant.id,
            'max_users', v_tenant.max_users,
            'max_devices', v_tenant.max_devices,
            'status', v_tenant.status
        ),
        'summary', json_build_object(
            'devices_online', v_active_devices,
            'max_devices', v_tenant.max_devices
        ),
        'users', v_users,
        'sessions', v_sessions
    );
END;
$$;

CREATE OR REPLACE FUNCTION public.client_end_workspace_device_session(
    p_owner_client_id text,
    p_workspace_id text,
    p_machine_id text,
    p_windows_user text DEFAULT NULL
)
RETURNS json
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = app, public
AS $$
DECLARE
    v_now timestamptz := timezone('utc', now());
BEGIN
    UPDATE app.workspace_device_sessions
    SET ended_at = v_now,
        last_seen_at = v_now
    WHERE owner_client_id = btrim(coalesce(p_owner_client_id, ''))
      AND workspace_id = btrim(coalesce(p_workspace_id, ''))
      AND machine_id = btrim(coalesce(p_machine_id, ''))
      AND windows_user = coalesce(btrim(p_windows_user), '');

    RETURN json_build_object('ok', true);
END;
$$;

REVOKE ALL ON FUNCTION public.client_upsert_workspace_device_session(text, text, text, text, text, text, text, text, text, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.client_get_workspace_device_status(text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.client_end_workspace_device_session(text, text, text, text) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION public.client_upsert_workspace_device_session(text, text, text, text, text, text, text, text, text, text, text) TO service_role;
GRANT EXECUTE ON FUNCTION public.client_get_workspace_device_status(text, text) TO service_role;
GRANT EXECUTE ON FUNCTION public.client_end_workspace_device_session(text, text, text, text) TO service_role;
