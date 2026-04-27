# Managed Gemini Rollout Checklist

Project ref: `tssgxhatnjvqthdiyuwo`

## 1. SQL migration

Run in Supabase SQL Editor:

```sql
alter table if exists public.clients
    add column if not exists gemini_api_key text not null default '';
```

Optional: the same SQL is saved in `supabase/managed_gemini_key_migration.sql`.

## 2. Fill client keys

Set `gemini_api_key` only for clients that should receive admin-managed AI access.

Examples:

```sql
update public.clients
set gemini_api_key = 'AIzaSy...'
where machine_id = 'TARGET_MACHINE_ID';
```

```sql
update public.clients
set gemini_api_key = ''
where machine_id = 'TARGET_MACHINE_ID';
```

## 3. Login to Supabase CLI

```powershell
npx supabase@latest login
```

Or set an access token for this shell:

```powershell
$env:SUPABASE_ACCESS_TOKEN = "your-supabase-access-token"
```

## 4. Deploy Edge Functions

Deploy at minimum:

```powershell
npx supabase@latest functions deploy client-gateway --project-ref tssgxhatnjvqthdiyuwo --use-api
```

Recommended to redeploy all functions that import `supabase/functions/_shared/common.ts` so the shared bundle stays in sync:

```powershell
npx supabase@latest functions deploy client-gateway --project-ref tssgxhatnjvqthdiyuwo --use-api
npx supabase@latest functions deploy client-auth --project-ref tssgxhatnjvqthdiyuwo --use-api
npx supabase@latest functions deploy mirror-sync --project-ref tssgxhatnjvqthdiyuwo --use-api
npx supabase@latest functions deploy admin-gateway --project-ref tssgxhatnjvqthdiyuwo --use-api
```

## 5. App-side behavior to verify

Expected rules:

- User key in Settings has priority.
- Server-managed key works only after a live gateway/heartbeat response.
- Server-managed key is not cached to local settings.
- If there is no user key and no live server response, AI stays disabled.
- If policy disables AI, Gemini key is cleared from runtime and AI is unavailable.

## 6. Smoke test scenarios

### Scenario A. No user key, server key present

Expected:

- client starts normally
- AI works
- Settings shows: `AI працює через ключ адміністратора`

### Scenario B. User key present, server key present

Expected:

- user key is used
- AI works even if the server key changes
- removing the user key falls back to the server key on next live sync

### Scenario C. No user key, server key present, then offline

Expected:

- after losing live sync, AI stops working
- app itself can still open under offline grace if access state allows it
- Settings no longer behaves as if admin-managed AI is available

### Scenario D. Standard plan / expired / DisableAI

Expected:

- AI does not work
- Gemini runtime key is cleared
- Settings shows AI is disabled by plan or admin policy

## 7. Quick data check

To confirm the key is stored:

```sql
select id, machine_id, expires_at, is_blocked, gemini_api_key
from public.clients
where machine_id = 'TARGET_MACHINE_ID';
```

## 8. Rollback

If needed:

1. Clear `gemini_api_key` for affected clients:

```sql
update public.clients
set gemini_api_key = '';
```

2. Redeploy previous `client-gateway` function version.
3. Clients will stop receiving admin-managed AI access on the next live sync.
