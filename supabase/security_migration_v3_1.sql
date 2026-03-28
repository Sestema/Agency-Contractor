create extension if not exists pgcrypto;

create table if not exists public.client_profiles (
    id uuid primary key default gen_random_uuid(),
    client_id uuid not null unique references public.clients(id) on delete cascade,
    first_name text not null,
    last_name text not null,
    password_hash text not null,
    password_salt text not null,
    must_reset_password boolean not null default false,
    remember_me_enabled boolean not null default false,
    session_version integer not null default 1,
    created_at timestamptz not null default timezone('utc', now()),
    updated_at timestamptz not null default timezone('utc', now())
);

create index if not exists ix_client_profiles_client_id
    on public.client_profiles (client_id);

create table if not exists public.mirror_sync_log (
    id bigint generated always as identity primary key,
    client_id uuid not null references public.clients(id) on delete cascade,
    machine_id text not null default '',
    operation text not null default '',
    success boolean not null default true,
    error_text text null,
    rows_affected integer not null default 0,
    created_at timestamptz not null default timezone('utc', now())
);

create table if not exists public.client_auth_attempts (
    client_id uuid not null references public.clients(id) on delete cascade,
    machine_id text not null default '',
    ip_address text not null default '',
    failed_attempts integer not null default 0,
    last_failed_at timestamptz null,
    created_at timestamptz not null default timezone('utc', now()),
    updated_at timestamptz not null default timezone('utc', now()),
    primary key (client_id, machine_id, ip_address)
);

alter table public.mirror_sync_log enable row level security;
alter table public.client_auth_attempts enable row level security;
alter table public.client_profiles enable row level security;

drop policy if exists "client_mirror_state_client_access" on public.client_mirror_state;
drop policy if exists "admin_agencies_client_access" on public.admin_agencies;
drop policy if exists "admin_employers_client_access" on public.admin_employers;
drop policy if exists "admin_employer_addresses_client_access" on public.admin_employer_addresses;
drop policy if exists "admin_employer_positions_client_access" on public.admin_employer_positions;
drop policy if exists "admin_employees_client_access" on public.admin_employees;
drop policy if exists "admin_employee_firm_history_client_access" on public.admin_employee_firm_history;

drop policy if exists "client_profiles_anon_select" on public.client_profiles;
drop policy if exists "client_profiles_anon_insert" on public.client_profiles;
drop policy if exists "client_profiles_anon_update" on public.client_profiles;

create or replace function public.touch_client_profiles_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = timezone('utc', now());
    return new;
end;
$$;

drop trigger if exists trg_client_profiles_updated_at on public.client_profiles;
create trigger trg_client_profiles_updated_at
before update on public.client_profiles
for each row
execute function public.touch_client_profiles_updated_at();

alter table public.admin_commands enable row level security;
alter table public.client_policies enable row level security;
alter table public.admin_audit_log enable row level security;
alter table public.client_diagnostics enable row level security;

do $$
begin
  if not (select relrowsecurity from pg_class where relname = 'clients') then
    alter table public.clients enable row level security;
  end if;
  if not (select relrowsecurity from pg_class where relname = 'telemetry') then
    alter table public.telemetry enable row level security;
  end if;
end $$;

select schemaname, tablename, policyname, permissive, roles, cmd, qual
from pg_policies
where schemaname = 'public'
  and tablename in (
    'client_mirror_state', 'admin_agencies', 'admin_employers',
    'admin_employer_addresses', 'admin_employer_positions',
    'admin_employees', 'admin_employee_firm_history',
    'client_profiles', 'admin_commands', 'client_policies',
    'admin_audit_log', 'client_diagnostics', 'clients', 'telemetry',
    'mirror_sync_log', 'client_auth_attempts'
  )
order by tablename, policyname;

select relname, relrowsecurity
from pg_class
where relname in (
    'client_mirror_state', 'admin_agencies', 'admin_employers',
    'admin_employer_addresses', 'admin_employer_positions',
    'admin_employees', 'admin_employee_firm_history',
    'client_profiles', 'admin_commands', 'client_policies',
    'admin_audit_log', 'client_diagnostics', 'clients', 'telemetry',
    'mirror_sync_log', 'client_auth_attempts'
)
order by relname;
