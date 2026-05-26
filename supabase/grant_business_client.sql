-- Manual Business grant for one client.
-- Replace TARGET_MACHINE_ID before running in Supabase SQL Editor.

update public.clients
set plan = 'business'
where machine_id = 'TARGET_MACHINE_ID';

insert into app.tenants (
    supabase_client_id,
    name,
    plan_key,
    max_users,
    max_devices,
    multi_user_enabled,
    status
)
select
    c.id,
    coalesce(nullif(btrim(c.machine_name), ''), 'Client ' || c.id),
    'business',
    10,
    3,
    true,
    'active'
from public.clients c
where c.machine_id = 'TARGET_MACHINE_ID'
  and not exists (
      select 1
      from app.tenants t
      where t.supabase_client_id = c.id::text
  );

update app.tenants t
set
    plan_key = 'business',
    max_users = 10,
    max_devices = 3,
    multi_user_enabled = true,
    status = 'active',
    license_expires_at = c.expires_at,
    updated_at = timezone('utc', now())
from public.clients c
where c.machine_id = 'TARGET_MACHINE_ID'
  and t.supabase_client_id = c.id::text;

select c.id, c.machine_id, c.machine_name, c.plan, t.plan_key, t.max_users, t.multi_user_enabled, t.status
from public.clients c
left join app.tenants t on t.supabase_client_id = c.id::text
where c.machine_id = 'TARGET_MACHINE_ID';
