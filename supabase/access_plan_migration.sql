alter table if exists public.clients
    add column if not exists plan text not null default 'trial';

update public.clients
set plan = 'trial'
where plan is null
   or btrim(plan) = '';
