# Agency Contractor 0.1.76 beta — PostgreSQL, salary history і workspace

## Зарплати і multi-PC (з 0.1.75)
- **Збереження зарплат записує лише змінених працівників**, а не весь місяць.
- **Row-level upsert** у SQLite і PostgreSQL.
- **Захист від stale cache** при одночасній роботі кількох ПК.
- **Точніші sync-події** `SalaryEntryChanged`; повне перезавантаження місяця — fallback.

## Salary history — автоочищення дублікатів
- **При старті** програма прибирає дублікати історії виплат у SQLite і PostgreSQL.
- Залишається **один запис** на працівника + місяць + фірму (новіший за `PaidAt`).
- Менше попереджень у логах і чистіша історія в картці працівника.

## PostgreSQL — стабільніше підключення
- **PostgresConnectionFactory** — централізоване підключення для всіх PostgreSQL-модулів.
- Однакові параметри timeout, keepalive і pooling.
- Підготовка до хмарного PostgreSQL (DigitalOcean тощо).

## Workspace і Business-ліцензія
- **Workspace session heartbeat** через Supabase gateway.
- **Immutable workspace passport** на OneDrive + детекція conflict-копій.
- Owner синхронізує business users; members — read-only passport.

## Multi-user фундамент
- Архітектура tenant/user/role/audit (feature flags, **вимкнено за замовчуванням**).

## Автооновлення
- Pre-release beta `0.1.76` для каналу `beta`.
- Stable-клієнти (`0.1.73`) цю beta не отримують.

## Що тестувати
- PostgreSQL: 2 ПК, зарплати + sync.
- Перезапуск: salary history без дублікатів.
- Business workspace: passport sync, session heartbeat.
- Beta auto-update (канал `beta`).
