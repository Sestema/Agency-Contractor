-- AdminPanel tenant RPC helpers.
-- These live in public schema so admin-gateway can call them without exposing app via PostgREST.
-- Run once in Supabase SQL Editor after multi_user_foundation_schema.sql.

CREATE OR REPLACE FUNCTION public.admin_get_tenant_for_client(p_client_id text)
RETURNS json
LANGUAGE sql
SECURITY DEFINER
SET search_path = app, public
STABLE
AS $$
  SELECT row_to_json(t)
  FROM app.tenants t
  WHERE t.supabase_client_id = btrim(p_client_id)
  LIMIT 1;
$$;

CREATE OR REPLACE FUNCTION public.admin_sync_client_tenant(
  p_client_id text,
  p_plan text,
  p_tenant_name text DEFAULT NULL,
  p_license_key text DEFAULT NULL,
  p_max_users integer DEFAULT 10,
  p_max_devices integer DEFAULT 3,
  p_multi_user_enabled boolean DEFAULT true,
  p_tenant_status text DEFAULT 'active',
  p_license_expires_at timestamptz DEFAULT NULL
)
RETURNS json
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = app, public
AS $$
DECLARE
  v_existing_id uuid;
  v_result app.tenants%ROWTYPE;
  v_plan_key text;
  v_status text;
BEGIN
  IF btrim(coalesce(p_client_id, '')) = '' THEN
    RAISE EXCEPTION 'client_id_required';
  END IF;

  SELECT id
  INTO v_existing_id
  FROM app.tenants
  WHERE supabase_client_id = btrim(p_client_id)
  LIMIT 1;

  IF lower(btrim(coalesce(p_plan, ''))) = 'business' THEN
    v_status := CASE lower(btrim(coalesce(p_tenant_status, '')))
      WHEN 'suspended' THEN 'suspended'
      WHEN 'blocked' THEN 'blocked'
      WHEN 'expired' THEN 'expired'
      WHEN 'trial' THEN 'trial'
      ELSE 'active'
    END;

    IF v_existing_id IS NULL THEN
      INSERT INTO app.tenants (
        supabase_client_id,
        name,
        license_key,
        plan_key,
        max_users,
        max_devices,
        multi_user_enabled,
        license_expires_at,
        status
      ) VALUES (
        btrim(p_client_id),
        coalesce(nullif(btrim(p_tenant_name), ''), 'Client ' || btrim(p_client_id)),
        p_license_key,
        'business',
        greatest(coalesce(p_max_users, 10), 1),
        greatest(coalesce(p_max_devices, 3), 1),
        coalesce(p_multi_user_enabled, true),
        p_license_expires_at,
        v_status
      )
      RETURNING * INTO v_result;
    ELSE
      UPDATE app.tenants
      SET
        name = coalesce(nullif(btrim(p_tenant_name), ''), name),
        license_key = p_license_key,
        plan_key = 'business',
        max_users = greatest(coalesce(p_max_users, 10), 1),
        max_devices = greatest(coalesce(p_max_devices, 3), 1),
        multi_user_enabled = coalesce(p_multi_user_enabled, true),
        license_expires_at = p_license_expires_at,
        status = v_status,
        updated_at = timezone('utc', now())
      WHERE id = v_existing_id
      RETURNING * INTO v_result;
    END IF;

    RETURN row_to_json(v_result);
  END IF;

  IF v_existing_id IS NULL THEN
    RETURN NULL;
  END IF;

  v_plan_key := CASE WHEN lower(btrim(coalesce(p_plan, ''))) = 'pro' THEN 'ultimate_1pc' ELSE 'standard_1pc' END;

  UPDATE app.tenants
  SET
    plan_key = v_plan_key,
    max_users = 1,
    max_devices = 1,
    multi_user_enabled = false,
    license_key = p_license_key,
    license_expires_at = p_license_expires_at,
    status = 'suspended',
    updated_at = timezone('utc', now())
  WHERE id = v_existing_id
  RETURNING * INTO v_result;

  RETURN row_to_json(v_result);
END;
$$;

REVOKE ALL ON FUNCTION public.admin_get_tenant_for_client(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.admin_sync_client_tenant(text, text, text, text, integer, integer, boolean, text, timestamptz) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION public.admin_get_tenant_for_client(text) TO service_role;
GRANT EXECUTE ON FUNCTION public.admin_sync_client_tenant(text, text, text, text, integer, integer, boolean, text, timestamptz) TO service_role;
