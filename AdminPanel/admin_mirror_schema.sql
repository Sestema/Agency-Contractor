create extension if not exists pgcrypto;

create table if not exists public.client_mirror_state (
    client_id uuid primary key references public.clients(id) on delete cascade,
    schema_version text not null default '1',
    last_full_sync_at timestamptz null,
    last_delta_sync_at timestamptz null,
    last_error_text text null,
    updated_at timestamptz not null default timezone('utc', now())
);

create table if not exists public.admin_agencies (
    client_id uuid not null references public.clients(id) on delete cascade,
    agency_id text not null,
    name text not null default '',
    ico text not null default '',
    full_address text not null default '',
    source_updated_at timestamptz null,
    last_synced_at timestamptz not null default timezone('utc', now()),
    is_deleted boolean not null default false,
    deleted_at timestamptz null,
    updated_at timestamptz not null default timezone('utc', now()),
    primary key (client_id, agency_id)
);

create index if not exists ix_admin_agencies_client_name
    on public.admin_agencies (client_id, name);

create table if not exists public.admin_employers (
    client_id uuid not null references public.clients(id) on delete cascade,
    employer_id uuid not null,
    agency_id text null,
    name text not null default '',
    ico text not null default '',
    legal_address text not null default '',
    weekly_work_hours numeric(10,2) not null default 0,
    daily_work_hours numeric(10,2) not null default 0,
    shift_count integer not null default 0,
    hidden_from_year integer not null default 0,
    hidden_from_month integer not null default 0,
    created_at timestamptz null,
    source_updated_at timestamptz null,
    last_synced_at timestamptz not null default timezone('utc', now()),
    is_deleted boolean not null default false,
    deleted_at timestamptz null,
    updated_at timestamptz not null default timezone('utc', now()),
    primary key (client_id, employer_id),
    constraint fk_admin_employers_agency
        foreign key (client_id, agency_id)
        references public.admin_agencies (client_id, agency_id)
        on delete set null
);

create index if not exists ix_admin_employers_client_name
    on public.admin_employers (client_id, name);

create table if not exists public.admin_employer_addresses (
    client_id uuid not null references public.clients(id) on delete cascade,
    employer_id uuid not null,
    address_id uuid primary key default gen_random_uuid(),
    street text not null default '',
    number text not null default '',
    city text not null default '',
    zip_code text not null default '',
    sort_order integer not null default 0,
    last_synced_at timestamptz not null default timezone('utc', now()),
    updated_at timestamptz not null default timezone('utc', now()),
    constraint fk_admin_employer_addresses_employer
        foreign key (client_id, employer_id)
        references public.admin_employers (client_id, employer_id)
        on delete cascade
);

create table if not exists public.admin_employer_positions (
    client_id uuid not null references public.clients(id) on delete cascade,
    employer_id uuid not null,
    position_id uuid primary key default gen_random_uuid(),
    title text not null default '',
    position_number text not null default '',
    monthly_salary_brutto numeric(12,2) not null default 0,
    hourly_salary numeric(12,2) not null default 0,
    sort_order integer not null default 0,
    last_synced_at timestamptz not null default timezone('utc', now()),
    updated_at timestamptz not null default timezone('utc', now()),
    constraint fk_admin_employer_positions_employer
        foreign key (client_id, employer_id)
        references public.admin_employers (client_id, employer_id)
        on delete cascade
);

create table if not exists public.admin_employees (
    client_id uuid not null references public.clients(id) on delete cascade,
    employee_id text not null,
    employer_id uuid null,
    full_name text not null default '',
    first_name text not null default '',
    last_name text not null default '',
    birth_date text not null default '',
    employee_type text not null default '',
    eu_document_type text not null default '',
    visa_doc_type text not null default '',
    gender text not null default '',
    passport_number text not null default '',
    passport_city text not null default '',
    passport_country text not null default '',
    passport_expiry text not null default '',
    visa_number text not null default '',
    visa_type text not null default '',
    visa_expiry text not null default '',
    insurance_company_short text not null default '',
    insurance_number text not null default '',
    insurance_expiry text not null default '',
    work_permit_name text not null default '',
    work_permit_number text not null default '',
    work_permit_type text not null default '',
    work_permit_issue_date text not null default '',
    work_permit_expiry text not null default '',
    work_permit_authority text not null default '',
    address_local_street text not null default '',
    address_local_number text not null default '',
    address_local_city text not null default '',
    address_local_zip text not null default '',
    address_abroad_street text not null default '',
    address_abroad_number text not null default '',
    address_abroad_city text not null default '',
    address_abroad_zip text not null default '',
    work_address_tag text not null default '',
    position_tag text not null default '',
    position_number text not null default '',
    monthly_salary_brutto numeric(12,2) not null default 0,
    hourly_salary numeric(12,2) not null default 0,
    contract_type text not null default '',
    phone text not null default '',
    email text not null default '',
    department text not null default '',
    status text not null default '',
    start_date text not null default '',
    contract_sign_date text not null default '',
    end_date text not null default '',
    is_archived boolean not null default false,
    archived_from_firm text not null default '',
    source_updated_at timestamptz null,
    last_synced_at timestamptz not null default timezone('utc', now()),
    is_deleted boolean not null default false,
    deleted_at timestamptz null,
    updated_at timestamptz not null default timezone('utc', now()),
    primary key (client_id, employee_id),
    constraint fk_admin_employees_employer
        foreign key (client_id, employer_id)
        references public.admin_employers (client_id, employer_id)
        on delete set null
);

create index if not exists ix_admin_employees_client_name
    on public.admin_employees (client_id, full_name);

create index if not exists ix_admin_employees_client_employer
    on public.admin_employees (client_id, employer_id, is_deleted, is_archived);

create table if not exists public.admin_employee_firm_history (
    client_id uuid not null references public.clients(id) on delete cascade,
    employee_id text not null,
    history_id uuid primary key default gen_random_uuid(),
    firm_name text not null default '',
    start_date text not null default '',
    end_date text not null default '',
    sort_order integer not null default 0,
    last_synced_at timestamptz not null default timezone('utc', now()),
    updated_at timestamptz not null default timezone('utc', now()),
    constraint fk_admin_employee_firm_history_employee
        foreign key (client_id, employee_id)
        references public.admin_employees (client_id, employee_id)
        on delete cascade
);

alter table public.admin_employer_addresses
    drop constraint if exists uq_admin_employer_addresses_client_employer_sort;
alter table public.admin_employer_addresses
    add constraint uq_admin_employer_addresses_client_employer_sort
    unique (client_id, employer_id, sort_order);

create index if not exists ix_admin_employer_addresses_employer
    on public.admin_employer_addresses (client_id, employer_id, sort_order);

alter table public.admin_employer_positions
    drop constraint if exists uq_admin_employer_positions_client_employer_sort;
alter table public.admin_employer_positions
    add constraint uq_admin_employer_positions_client_employer_sort
    unique (client_id, employer_id, sort_order);

create index if not exists ix_admin_employer_positions_employer
    on public.admin_employer_positions (client_id, employer_id, sort_order);

alter table public.admin_employee_firm_history
    drop constraint if exists uq_admin_employee_firm_history_client_employee_sort;
alter table public.admin_employee_firm_history
    add constraint uq_admin_employee_firm_history_client_employee_sort
    unique (client_id, employee_id, sort_order);

create index if not exists ix_admin_employee_firm_history_employee
    on public.admin_employee_firm_history (client_id, employee_id, sort_order);

alter table public.client_mirror_state enable row level security;
alter table public.admin_agencies enable row level security;
alter table public.admin_employers enable row level security;
alter table public.admin_employer_addresses enable row level security;
alter table public.admin_employer_positions enable row level security;
alter table public.admin_employees enable row level security;
alter table public.admin_employee_firm_history enable row level security;

drop policy if exists "client_mirror_state_anon_insert" on public.client_mirror_state;
drop policy if exists "client_mirror_state_anon_update" on public.client_mirror_state;
drop policy if exists "client_mirror_state_client_access" on public.client_mirror_state;

drop policy if exists "admin_agencies_anon_insert" on public.admin_agencies;
drop policy if exists "admin_agencies_anon_update" on public.admin_agencies;
drop policy if exists "admin_agencies_client_access" on public.admin_agencies;

drop policy if exists "admin_employers_anon_insert" on public.admin_employers;
drop policy if exists "admin_employers_anon_update" on public.admin_employers;
drop policy if exists "admin_employers_client_access" on public.admin_employers;

drop policy if exists "admin_employer_addresses_anon_insert" on public.admin_employer_addresses;
drop policy if exists "admin_employer_addresses_anon_update" on public.admin_employer_addresses;
drop policy if exists "admin_employer_addresses_anon_delete" on public.admin_employer_addresses;
drop policy if exists "admin_employer_addresses_client_access" on public.admin_employer_addresses;

drop policy if exists "admin_employer_positions_anon_insert" on public.admin_employer_positions;
drop policy if exists "admin_employer_positions_anon_update" on public.admin_employer_positions;
drop policy if exists "admin_employer_positions_anon_delete" on public.admin_employer_positions;
drop policy if exists "admin_employer_positions_client_access" on public.admin_employer_positions;

drop policy if exists "admin_employees_anon_insert" on public.admin_employees;
drop policy if exists "admin_employees_anon_update" on public.admin_employees;
drop policy if exists "admin_employees_client_access" on public.admin_employees;

drop policy if exists "admin_employee_firm_history_anon_insert" on public.admin_employee_firm_history;
drop policy if exists "admin_employee_firm_history_anon_update" on public.admin_employee_firm_history;
drop policy if exists "admin_employee_firm_history_anon_delete" on public.admin_employee_firm_history;
drop policy if exists "admin_employee_firm_history_client_access" on public.admin_employee_firm_history;

create policy "client_mirror_state_client_access"
    on public.client_mirror_state
    for all
    to anon, authenticated
    using (true)
    with check (true);

create policy "admin_agencies_client_access"
    on public.admin_agencies
    for all
    to anon, authenticated
    using (true)
    with check (true);

create policy "admin_employers_client_access"
    on public.admin_employers
    for all
    to anon, authenticated
    using (true)
    with check (true);

create policy "admin_employer_addresses_client_access"
    on public.admin_employer_addresses
    for all
    to anon, authenticated
    using (true)
    with check (true);

create policy "admin_employer_positions_client_access"
    on public.admin_employer_positions
    for all
    to anon, authenticated
    using (true)
    with check (true);

create policy "admin_employees_client_access"
    on public.admin_employees
    for all
    to anon, authenticated
    using (true)
    with check (true);

create policy "admin_employee_firm_history_client_access"
    on public.admin_employee_firm_history
    for all
    to anon, authenticated
    using (true)
    with check (true);

create or replace function public.touch_admin_mirror_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = timezone('utc', now());
    return new;
end;
$$;

drop trigger if exists trg_client_mirror_state_updated_at on public.client_mirror_state;
create trigger trg_client_mirror_state_updated_at
before update on public.client_mirror_state
for each row
execute function public.touch_admin_mirror_updated_at();

drop trigger if exists trg_admin_agencies_updated_at on public.admin_agencies;
create trigger trg_admin_agencies_updated_at
before update on public.admin_agencies
for each row
execute function public.touch_admin_mirror_updated_at();

drop trigger if exists trg_admin_employers_updated_at on public.admin_employers;
create trigger trg_admin_employers_updated_at
before update on public.admin_employers
for each row
execute function public.touch_admin_mirror_updated_at();

drop trigger if exists trg_admin_employer_addresses_updated_at on public.admin_employer_addresses;
create trigger trg_admin_employer_addresses_updated_at
before update on public.admin_employer_addresses
for each row
execute function public.touch_admin_mirror_updated_at();

drop trigger if exists trg_admin_employer_positions_updated_at on public.admin_employer_positions;
create trigger trg_admin_employer_positions_updated_at
before update on public.admin_employer_positions
for each row
execute function public.touch_admin_mirror_updated_at();

drop trigger if exists trg_admin_employees_updated_at on public.admin_employees;
create trigger trg_admin_employees_updated_at
before update on public.admin_employees
for each row
execute function public.touch_admin_mirror_updated_at();

drop trigger if exists trg_admin_employee_firm_history_updated_at on public.admin_employee_firm_history;
create trigger trg_admin_employee_firm_history_updated_at
before update on public.admin_employee_firm_history
for each row
execute function public.touch_admin_mirror_updated_at();
