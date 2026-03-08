# TimeClock - Database Schema

## Tables

### Users

| Column       | Type           | Constraints                |
|--------------|----------------|----------------------------|
| Id           | int            | PK, Identity               |
| Username     | nvarchar(100)  | NOT NULL, UNIQUE           |
| PasswordHash | nvarchar(max)  | NOT NULL                   |
| FullName     | nvarchar(200)  | NOT NULL                   |
| Role         | nvarchar(20)   | NOT NULL (Admin/Employee)  |
| CreatedAt    | datetime2      | NOT NULL (UTC)             |
| IsActive     | bit            | NOT NULL, default: true    |
| HourlyRate   | decimal(8,2)   | nullable                   |

### TimeEntries

ClockIn/ClockOut are stored as **Zurich-local** (Europe/Zurich) datetimes without UTC offset.

| Column           | Type          | Constraints                |
|------------------|---------------|----------------------------|
| Id               | int           | PK, Identity               |
| UserId           | int           | FK -> Users.Id, NOT NULL   |
| ClockIn          | datetime2     | NOT NULL (Zurich-local)    |
| ClockOut         | datetime2     | nullable (Zurich-local)    |
| DurationMinutes  | float         | nullable                   |
| Notes            | nvarchar(max) | nullable                   |
| IsManuallyEdited | bit           | NOT NULL, default: false   |
| RowVersion       | rowversion    | concurrency token          |

### TimeEntryAuditLogs

| Column          | Type          | Constraints                     |
|-----------------|---------------|---------------------------------|
| Id              | int           | PK, Identity                    |
| TimeEntryId     | int           | FK -> TimeEntries.Id, NOT NULL  |
| ChangedByUserId | int           | FK -> Users.Id, NOT NULL        |
| ChangedAt       | datetime2     | NOT NULL (UTC)                  |
| FieldName       | nvarchar(50)  | NOT NULL                        |
| OldValue        | nvarchar(500) | nullable                        |
| NewValue        | nvarchar(500) | nullable                        |

### RefreshTokens

| Column          | Type          | Constraints                |
|-----------------|---------------|----------------------------|
| Id              | int           | PK, Identity               |
| UserId          | int           | FK -> Users.Id, NOT NULL   |
| Token           | nvarchar(128) | NOT NULL, UNIQUE           |
| ExpiresAt       | datetime2     | NOT NULL (UTC)             |
| CreatedAt       | datetime2     | NOT NULL (UTC)             |
| RevokedAt       | datetime2     | nullable (UTC)             |
| ReplacedByToken | nvarchar(128) | nullable                   |

## Relationships

```
┌───────────────────────────┐       ┌────────────────────────────────┐
│          Users             │       │          TimeEntries            │
├───────────────────────────┤       ├────────────────────────────────┤
│ PK  Id             int     │──┐    │ PK  Id                int      │
│     Username       str     │  │    │ FK  UserId            int      │──┐
│     PasswordHash   str     │  │    │     ClockIn           datetime  │  │
│     FullName       str     │  │    │     ClockOut          datetime? │  │
│     Role           str     │  │    │     DurationMinutes   float?   │  │
│     CreatedAt      datetime│  │    │     Notes             str?     │  │
│     IsActive       bit     │  │    │     IsManuallyEdited  bit      │  │
│     HourlyRate     decimal?│  │    │     RowVersion        byte[]   │  │
└───────────────────────────┘  │    └────────────────────────────────┘  │
         │                      │              │                         │
         │  1..*                │              │                         │
         ├──────────────────────┘              │  1..*                   │
         │                                     ├─────────────────────────┘
         │  1..*                               │
         │    ┌──────────────────────────────┐ │   ┌──────────────────────────────┐
         │    │       RefreshTokens           │ │   │     TimeEntryAuditLogs        │
         │    ├──────────────────────────────┤ │   ├──────────────────────────────┤
         └───>│ FK  UserId          int       │ └──>│ FK  TimeEntryId    int       │
              │     Token           str       │     │ FK  ChangedByUserId int  ──> Users │
              │     ExpiresAt       datetime  │     │     ChangedAt       datetime  │
              │     CreatedAt       datetime  │     │     FieldName       str       │
              │     RevokedAt       datetime? │     │     OldValue        str?      │
              │     ReplacedByToken str?      │     │     NewValue        str?      │
              └──────────────────────────────┘     └──────────────────────────────┘
```

- **Users 1 ──── * TimeEntries** (Restrict delete)
- **Users 1 ──── * RefreshTokens** (Cascade delete)
- **TimeEntries 1 ──── * TimeEntryAuditLogs** (Cascade delete)
- **Users 1 ──── * TimeEntryAuditLogs** via ChangedByUserId (Restrict delete)

## Indexes

| Table              | Columns                    | Type                                        |
|--------------------|----------------------------|---------------------------------------------|
| Users              | Username                   | Unique                                      |
| TimeEntries        | (UserId, ClockIn)          | Composite, includes ClockOut/Duration/Notes |
| TimeEntries        | UserId WHERE ClockOut IS NULL | Filtered Unique (one open entry per user) |
| RefreshTokens      | Token                      | Unique                                      |
| RefreshTokens      | UserId                     | Non-unique                                  |
| TimeEntryAuditLogs | TimeEntryId                | Non-unique                                  |

## Offline Queue (File-based)

The offline queue is **not stored in SQL Server**. It is persisted as a JSON file (`data/offline-queue.json`) on the server's local filesystem, managed by `OfflineQueueService` with atomic writes.

### OfflineEntry (JSON record)

| Field     | Type     | Notes                                    |
|-----------|----------|------------------------------------------|
| Id        | string   | GUID, generated on creation              |
| Action    | string   | `"ClockIn"` or `"ClockOut"`              |
| UserId    | int      | References Users.Id (logical FK)         |
| Timestamp | datetime | Zurich-local time when action occurred   |
| Notes     | string?  | Optional notes (ClockIn only)            |
| QueuedAt  | datetime | UTC time the entry was queued            |

Entries are appended when the external time API is unreachable and removed atomically after successful sync by `OfflineSyncService`.
| RefreshTokens      | Token                      | Unique                            |
| RefreshTokens      | UserId                     | Non-unique                        |
| TimeEntryAuditLogs | TimeEntryId                | Non-unique                        |

## Delete Behavior

- Deleting a **User** is restricted if they have **TimeEntries** (Restrict)
- Deleting a **User** cascades to delete all their **RefreshTokens** (Cascade)
- Deleting a **TimeEntry** cascades to delete its **TimeEntryAuditLogs** (Cascade)
- Deleting a **User** is restricted if they are referenced in **TimeEntryAuditLogs**.ChangedByUserId (Restrict)

## Seed Data

| Username | Role  | Password |
|----------|-------|----------|
| admin    | Admin | admin123 |

Seed credentials are configurable via `Seed:AdminUsername` and `Seed:AdminPassword` environment variables. A warning is logged when default credentials are used.
