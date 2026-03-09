create extension if not exists pgcrypto;

create table if not exists public.admin_commands (
    id uuid primary key default gen_random_uuid(),
    client_id uuid not null references public.clients(id) on delete cascade,
    command_type text not null,
    payload_json jsonb not null default '{}'::jsonb,
    status text not null default 'pending',
    created_at timestamptz not null default timezone('utc', now()),
    created_by text not null default 'admin-panel',
    executed_at timestamptz null,
    result_json jsonb null,
    error_text text null
);

create index if not exists ix_admin_commands_client_status_created
    on public.admin_commands (client_id, status, created_at desc);

create table if not exists public.client_policies (
    client_id uuid primary key references public.clients(id) on delete cascade,
    minimum_supported_version text null,
    recommended_version text null,
    update_channel text not null default 'stable',
    force_update boolean not null default false,
    maintenance_mode boolean not null default false,
    read_only_mode boolean not null default false,
    disable_ai boolean not null default false,
    disable_exports boolean not null default false,
    hide_templates boolean not null default false,
    hide_finance boolean not null default false,
    require_online_check boolean not null default false,
    admin_message text null,
    policy_version text not null default '1',
    updated_at timestamptz not null default timezone('utc', now())
);

create table if not exists public.admin_audit_log (
    id uuid primary key default gen_random_uuid(),
    target_client_id uuid null references public.clients(id) on delete set null,
    action_type text not null,
    old_value_json jsonb null,
    new_value_json jsonb null,
    note text null,
    created_at timestamptz not null default timezone('utc', now()),
    actor text not null default 'admin-panel'
);

create index if not exists ix_admin_audit_log_target_created
    on public.admin_audit_log (target_client_id, created_at desc);

create table if not exists public.client_diagnostics (
    id uuid primary key default gen_random_uuid(),
    client_id uuid not null references public.clients(id) on delete cascade,
    kind text not null,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default timezone('utc', now())
);

create index if not exists ix_client_diagnostics_client_created
    on public.client_diagnostics (client_id, created_at desc);
