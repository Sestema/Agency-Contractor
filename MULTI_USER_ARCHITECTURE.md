# Agency Contractor Multi-User Architecture

## Purpose

This document defines the safe path from the current desktop-first application to a professional multi-user system where one customer owns a workspace, invites employees, assigns roles, and all users can work from the desktop app or web panel without overwriting each other.

The first implementation stages must not change the current application behavior. New multi-user logic is added beside the existing code, guarded by feature flags, and enabled only for test tenants until it is stable.

## Non-Breaking Rule

Current stable behavior must remain the default.

- If multi-user flags are disabled, the app must behave exactly as it does today.
- Existing profile, license, PostgreSQL, salary, employee, template, and sync flows must not be renamed or removed during the foundation stage.
- New database tables must be created with `CREATE TABLE IF NOT EXISTS` and must not be required by old flows until enforcement is explicitly enabled.
- Permission checks start in soft mode: they log the decision but return allowed.
- Hard blocking is enabled only after the audit data proves the rules are correct.

## Existing Foundation To Reuse

Do not rebuild these systems from scratch. Extend them.

- `CurrentProfileService` already holds the current `ClientProfileRecord`.
- `ProfileAuthService` and `ProfileSessionService` already provide profile authentication and session behavior.
- `PolicyService` is already the central gate for read-only mode, exports, hidden finance, hidden templates, and feature visibility.
- `ActivityLogService` already records actor names and writes through `IActivityLogStorage`.
- `PostgresActivityLogStorage` already stores activity log data in PostgreSQL mode.
- `WebPanelHostService` already hosts REST endpoints in the desktop process.
- `SharedOperationLockService` already provides operation-level locking.
- `SyncEventService` already coordinates changes between PCs and can remain the fallback channel.
- `LicenseService.GetMachineId()` already identifies the device.
- Existing PostgreSQL storage and migration services must remain the base for database changes.

## Target Product Model

The sold product is not just an `.exe`. It is a licensed company workspace.

- Vendor: the software seller/admin.
- Tenant/workspace: one customer company, for example "Ivan Petrenko Agency".
- Owner: the customer who bought the license and controls that workspace.
- Users: employees invited by the Owner.
- Seats: the maximum number of allowed users/devices for the license.
- Clients: desktop app and web panel users connected to the same workspace.

Example flow:

```text
Vendor sells license with 15 seats
  -> customer activates workspace
  -> first user becomes Owner
  -> Owner invites employees
  -> employees join by invite code
  -> roles and permissions decide what each user can see or edit
```

## Supabase License Control Plane

Supabase remains the vendor-controlled license and policy system. It is the source of truth for who bought the product, which plan is active, how many users/devices are allowed, and whether the customer should be blocked, downgraded, or switched to read-only mode.

Supabase should own:

- vendor customers and `client_id`;
- license keys and plan names;
- subscription/trial status;
- expiration dates;
- allowed seats: `max_users` and `max_devices`;
- enabled modules such as AI, finance, web panel, PostgreSQL, exports;
- remote policy flags such as read-only, force update, hide finance, hide templates, disable exports, disable AI;
- activated devices and last license checks;
- seller/admin controls in `AdminPanel`.

### Product Plans

The first commercial plan set is fixed and simple:

| Plan key | Name | Users | Devices | AI | Multi-user | PostgreSQL multi-PC | Web panel | Notes |
| --- | --- | ---: | ---: | --- | --- | --- | --- | --- |
| `standard_1pc` | Standard 1 PC | 1 | 1 | No | No | No | Limited/off by policy | Single-PC license without AI. |
| `ultimate_1pc` | Ultimate 1 PC | 1 | 1 | Yes | No | No | Limited/off by policy | Single-PC license with AI. |
| `business` | Business | 30 | 30 | Yes | Yes | Yes | Yes | Everything enabled for a company team. |

Business means all modules are available: AI, finance, exports, web panel, PostgreSQL multi-PC, roles, invites, sessions, audit, and future server/realtime capabilities.

Customer SQLite/PostgreSQL databases should own:

- employees, companies, agencies, templates, salary, documents, reports;
- tenant/workspace users invited by the Owner;
- tenant roles and permissions;
- tenant sessions and active devices inside that customer workspace;
- tenant audit/activity log.

The two layers must stay separated:

```text
Supabase
  -> license, plan, max users/devices, vendor policy

Customer SQLite/PostgreSQL
  -> working data, invited users, roles, sessions, audit
```

The local tenant must be linked back to Supabase with a stable `supabase_client_id`/`client_id`. This lets the app enforce seat limits without moving customer working data into Supabase.

Invite flow with license limits:

```text
Owner creates invite
  -> app checks latest cached/online Supabase policy
  -> app compares active tenant users with max_users
  -> invite is allowed only if the license has free seats
```

Offline behavior:

- Existing customers must keep working from cached policy for a limited grace period.
- New invites and seat increases should require a recent Supabase policy check.
- If Supabase returns blocked/read-only, the local app applies `PolicyService` restrictions but must not corrupt local data.

No live Supabase schema or RLS policy should be changed during the foundation stage without a separate tested migration. First update documents and SQL drafts, then test in a separate Supabase project, then apply to production.

## Architecture Roadmap

### Stage 1: In-Process Foundation

Use the current app process and existing services.

```text
Desktop app
WebPanelHostService /api/v2
  -> existing application services
  -> PostgreSQL
```

This is the safest first stage because it avoids a second deployment, SSL setup, monitoring, backup rules, and duplicate business logic.

### Stage 2: Extractable API Boundary

Move new `/api/v2` endpoint logic into services that do not depend on WPF UI classes or ViewModels. `WebPanelHostService` should only map HTTP requests to these services.

The same services should later be usable by:

- in-process `WebPanelHostService`;
- a future Windows Service;
- a future Docker/VPS hosted server;
- a future SaaS backend.

### Stage 3: Dedicated Server

A dedicated server will eventually be required for professional 15-20 user deployments.

```text
Desktop app
Web app
  -> AgencyContractor.Server
  -> PostgreSQL
```

The server becomes the real heart of the customer workspace. It owns authentication, permissions, audit, realtime, concurrency, and license enforcement.

This is mandatory for the long-term ideal, but not the first coding step.

## Data Model Foundation

Use `tenant_id` from the beginning, even if the first versions only have one tenant. Adding it later would force painful data migrations.

### tenants

Represents one customer workspace.

```sql
CREATE TABLE IF NOT EXISTS app.tenants (
    id uuid PRIMARY KEY,
    supabase_client_id text NULL,
    name text NOT NULL,
    owner_user_id uuid NULL,
    license_key text NULL,
    plan_key text NOT NULL DEFAULT 'standard_1pc',
    max_users integer NOT NULL DEFAULT 1,
    max_devices integer NOT NULL DEFAULT 1,
    ai_enabled boolean NOT NULL DEFAULT false,
    multi_user_enabled boolean NOT NULL DEFAULT false,
    postgres_enabled boolean NOT NULL DEFAULT false,
    web_panel_enabled boolean NOT NULL DEFAULT false,
    exports_enabled boolean NOT NULL DEFAULT true,
    all_modules_enabled boolean NOT NULL DEFAULT false,
    license_expires_at timestamptz NULL,
    last_policy_sync_at timestamptz NULL,
    status text NOT NULL DEFAULT 'active',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
```

### tenant_users

Extends the existing profile idea into workspace users. Do not rename `ClientProfileRecord` at the beginning; add compatible fields later.

```sql
CREATE TABLE IF NOT EXISTS app.tenant_users (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES app.tenants(id),
    client_id text NULL,
    first_name text NOT NULL,
    last_name text NOT NULL,
    email text NULL,
    password_hash text NULL,
    role_key text NOT NULL DEFAULT 'owner',
    is_active boolean NOT NULL DEFAULT true,
    invited_by_user_id uuid NULL,
    last_seen_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
```

### tenant_role_permissions

Start with system roles and static permission keys. Avoid a complex role editor until the base is stable.

```sql
CREATE TABLE IF NOT EXISTS app.tenant_role_permissions (
    tenant_id uuid NULL,
    role_key text NOT NULL,
    permission_key text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, role_key, permission_key)
);
```

`tenant_id = NULL` means default system permissions shared by all tenants. Tenant-specific overrides can be added later.

### tenant_invites

Owner can invite a worker into the same workspace.

```sql
CREATE TABLE IF NOT EXISTS app.tenant_invites (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES app.tenants(id),
    invite_code text NOT NULL UNIQUE,
    email text NULL,
    role_key text NOT NULL,
    created_by_user_id uuid NOT NULL,
    accepted_by_user_id uuid NULL,
    expires_at timestamptz NOT NULL,
    accepted_at timestamptz NULL,
    revoked_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
```

### tenant_user_sessions

Tracks who is online, which machine is used, and whether Owner revoked access.

```sql
CREATE TABLE IF NOT EXISTS app.tenant_user_sessions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES app.tenants(id),
    user_id uuid NOT NULL REFERENCES app.tenant_users(id),
    machine_id text NOT NULL,
    machine_name text NULL,
    windows_user text NULL,
    app_version text NULL,
    ip_address text NULL,
    started_at timestamptz NOT NULL DEFAULT now(),
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL,
    ended_at timestamptz NULL
);
```

### audit metadata extension

The current activity log should be extended in a backward-compatible way.

New nullable fields:

- `tenant_id`
- `actor_user_id`
- `session_id`
- `machine_id`
- `entity_type`
- `entity_id`
- `old_values_json`
- `new_values_json`

Existing records may keep these fields null.

## Roles

Start with a small fixed role set.

- `owner`: full workspace control, users, roles, license, audit, all data.
- `admin`: most operational rights, no vendor/license administration.
- `hr`: employees, documents, templates, generation.
- `accountant`: salary, advances, expenses, finance reports, exports.
- `manager`: employee/company overview, limited editing, no salary by default.
- `viewer`: read-only access to allowed areas.

Custom roles can come later. Fixed roles are safer for the foundation stage.

## Permission Keys

Use stable string keys. These are the contract between UI, services, audit, and future API.

### Employees

- `employees.view`
- `employees.create`
- `employees.edit`
- `employees.archive`
- `employees.restore`
- `employees.delete`
- `employees.history.view`
- `employees.history.edit`

### Documents

- `documents.view`
- `documents.add`
- `documents.replace`
- `documents.delete`
- `documents.generate`
- `documents.bulk_generate`
- `documents.ai_scan`

### Templates

- `templates.view`
- `templates.create`
- `templates.edit`
- `templates.delete`
- `templates.copy`
- `templates.ai_tags`

### Salary And Finance

- `salary.view`
- `salary.edit`
- `salary.mark_paid`
- `salary.export`
- `salary.advances.edit`
- `salary.expenses.edit`
- `finance.custom_fields.edit`

### Companies And Agencies

- `companies.view`
- `companies.create`
- `companies.edit`
- `companies.delete`
- `agencies.view`
- `agencies.edit`

### Reports

- `reports.view`
- `reports.export`
- `reports.configure`

### Settings And Administration

- `settings.view`
- `settings.edit`
- `settings.postgres_admin`
- `settings.web_panel_admin`
- `users.view`
- `users.invite`
- `users.edit`
- `users.disable`
- `sessions.view`
- `sessions.revoke`
- `audit.view`
- `license.view`
- `license.manage`

## PolicyService Plan

Extend `PolicyService`; do not create a competing permission system.

Target API:

```csharp
PolicyService.RequirePermission("salary.edit", "зберегти зарплатний звіт");
PolicyService.HasPermission("salary.view");
```

Foundation behavior:

- If `ExperimentalMultiUser` is false, return true and preserve current behavior.
- If `PermissionSoftMode` is true, calculate the decision, write audit/log data, but still return true.
- If hard enforcement is enabled for the tenant, block missing permissions with a clear toast.
- Existing `EnsureWriteAllowed` and `EnsureExportsAllowed` remain valid and can call the new permission logic internally later.

## Feature Flags

Add these before wiring behavior:

- `ExperimentalMultiUser`: master flag, default false.
- `PermissionSoftMode`: default true while testing.
- `UseApiV2ForWebPanel`: default false until endpoints are ready.
- `UsePostgresNotify`: default false until LISTEN/NOTIFY is proven stable.
- `MultiUserHardEnforcement`: default false; can be enabled per test tenant later.

## Safe Implementation Sequence

1. Create this architecture document.
2. Add SQL migration definitions for new tables, but do not require them in old flows.
3. Add feature flags, all disabled by default.
4. Extend `ClientProfileRecord` compatibility with `TenantId`, `RoleKey`, `IsActive`, and permissions metadata. Empty values mean current Owner-only behavior.
5. Extend `ActivityLogEntry` and PostgreSQL activity storage with nullable tenant/user/session/machine fields.
6. Add `PolicyService.RequirePermission` in soft mode only.
7. Add a test-only Users screen behind `ExperimentalMultiUser`.
8. Add invite-code creation and acceptance behind the feature flag.
9. Add `/api/v2` endpoints inside `WebPanelHostService`, but keep logic in extractable services.
10. Add PostgreSQL LISTEN/NOTIFY beside `SyncEventService`, not instead of it.
11. Add optimistic concurrency to the highest-risk tables, starting with salary rows.
12. Enable hard permission enforcement only for new test tenants.

## Concurrency Plan

Use optimistic concurrency as the default.

For rows such as `salary_entries`, add:

```sql
row_version bigint NOT NULL DEFAULT 1
```

Update pattern:

```sql
UPDATE salary.salary_entries
SET hours = @hours,
    row_version = row_version + 1
WHERE id = @id
  AND row_version = @expected_row_version;
```

If zero rows are updated, another user saved first. UI should show a conflict message and offer reload/compare, not silently overwrite.

Use `SharedOperationLockService` for short critical operations and mass updates only. Do not use long locks for normal editing.

## Realtime Plan

Keep current sync behavior as fallback.

- OneDrive/file mode: `SyncEventService` remains the channel.
- PostgreSQL mode: add LISTEN/NOTIFY for faster events.
- Future server mode: use SignalR from `AgencyContractor.Server`.

PostgreSQL example:

```sql
NOTIFY salary_changed, '{"tenant_id":"...","year":2026,"month":5,"actor_user_id":"..."}';
```

The listener must run in a background task with cancellation, reconnect backoff, and no UI-thread blocking.

## Performance And Stability Rules

- All database operations must be async where practical.
- Use `CancellationToken` and command timeouts.
- Never block WPF UI thread with database or network calls.
- Use dispatcher only to apply completed UI updates.
- Use connection pooling, retry helpers, and reconnect backoff.
- Long operations should show progress and support cancellation.
- Bulk document generation should become a background job later.
- All locks and sessions must expire or be revocable.

## Future Server Extraction Rules

Everything added under `/api/v2` must be written so it can move to a dedicated server later.

- Do not put business rules inside XAML code-behind.
- Do not make API logic depend on WPF controls, windows, or ViewModels.
- Put tenant/user/permission/audit logic in services.
- Keep database access behind storage/service abstractions.
- Use request/response DTOs that are suitable for HTTP clients.
- Design endpoints as if desktop and web are both clients.

Future server deployment options:

- Windows Service on the customer's office server.
- Docker container on a VPS.
- Cloud-hosted SaaS controlled by the vendor.

## What Not To Do First

- Do not rename `ClientProfileRecord` to `User` at the start.
- Do not force login for every existing customer immediately.
- Do not enable hard permission blocking globally.
- Do not replace current sync with LISTEN/NOTIFY in one step.
- Do not move all desktop data access through API in the first phase.
- Do not start with a separate server project before the foundation is stable.
- Do not migrate all historical data to tenant-aware tables before the model is proven.

## First Deliverable Definition

The first safe deliverable is complete when:

- this document exists;
- permission keys are agreed;
- server extraction is explicitly part of the roadmap;
- no runtime behavior has changed;
- the next step can be a non-breaking SQL migration for new tenant/user/session/invite tables.
