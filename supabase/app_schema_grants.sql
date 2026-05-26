-- Run once in Supabase SQL Editor if app.tenants already exists.
-- Prefer supabase/admin_tenant_rpc.sql (works without exposing app schema in PostgREST).
-- Exposing app in Dashboard -> Project Settings -> API is optional when RPC helpers are installed.

GRANT USAGE ON SCHEMA app TO service_role;
GRANT ALL ON ALL TABLES IN SCHEMA app TO service_role;
GRANT ALL ON ALL SEQUENCES IN SCHEMA app TO service_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA app GRANT ALL ON TABLES TO service_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA app GRANT ALL ON SEQUENCES TO service_role;
