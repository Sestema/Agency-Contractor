alter table if exists public.clients
    add column if not exists gemini_api_key text not null default '';
