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

alter table public.client_profiles enable row level security;

drop policy if exists "client_profiles_anon_select" on public.client_profiles;
create policy "client_profiles_anon_select"
    on public.client_profiles
    for select
    to anon
    using (true);

drop policy if exists "client_profiles_anon_insert" on public.client_profiles;
create policy "client_profiles_anon_insert"
    on public.client_profiles
    for insert
    to anon
    with check (true);

drop policy if exists "client_profiles_anon_update" on public.client_profiles;
create policy "client_profiles_anon_update"
    on public.client_profiles
    for update
    to anon
    using (true)
    with check (true);

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
