---
name: efcore-migrations
description: >
  Handles database schema changes using Entity Framework Core migrations.
  Use this when creating or modifying entities, tables, columns, indexes, or
  relationships. Prevents raw SQL schema changes and enforces EF Core conventions.
---

# EF Core Migrations Skill

## When to Use This Skill

Load this skill when:
- A schema change is required (new entity, column, index, relationship, constraint)
- Asked to "create a migration", "update the schema", "add a column", or "add a table"
- Implementing a plan section that includes data model or DbContext changes
- Any `plan.md` section references database modifications

## Required Tools

Verify the EF Core CLI tools are installed:
```bash
dotnet ef --version
```

If not installed:
```bash
dotnet tool install --global dotnet-ef
```

Or use the local tool manifest if the project has one:
```bash
dotnet tool restore
```

## Step-by-Step Migration Workflow

### Step 1 — Modify the entity class

Locate the relevant entity in the domain or data layer (typically `src/*/Entities/` or `src/*/Models/`).

```csharp
// Example: adding a property to an existing entity
public class Post
{
    public Guid Id { get; init; }
    public string Title { get; set; } = string.Empty;
    public string? CursorToken { get; set; }   // ← new nullable column
    public DateTime CreatedAt { get; init; }
}
```

### Step 2 — Update the DbContext configuration (if needed)

If the property requires explicit configuration (index, max length, precision, computed column):

```csharp
// In your DbContext or a separate IEntityTypeConfiguration<T>
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Post>(entity =>
    {
        entity.HasIndex(p => new { p.CreatedAt, p.Id })
              .HasDatabaseName("IX_Posts_CreatedAt_Id")
              .IsDescending(true, true);

        entity.Property(p => p.Title)
              .HasMaxLength(500)
              .IsRequired();
    });
}
```

Prefer `IEntityTypeConfiguration<T>` over inline `OnModelCreating` for large schemas.

### Step 3 — Generate the migration

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Infrastructure \
  --startup-project src/Api
```

Naming convention: `PascalCase`, descriptive, present-tense verb.

Good names:
- `AddCompositeIndexToPosts`
- `AddCursorTokenColumnToPosts`
- `CreateNotificationsTable`

Bad names:
- `Migration1`
- `Update`
- `Fix`

### Step 4 — Review the generated migration

Always open and read the generated `<Timestamp>_<MigrationName>.cs` file before applying.

Check for:
- Unintended `DropColumn` or `DropTable` calls
- Column type changes that are destructive
- Missing index definitions
- Incorrect nullability (nullable vs non-nullable)

The `Down()` method must be a valid rollback — verify it too.

### Step 5 — Apply the migration locally

```bash
dotnet ef database update \
  --project src/Infrastructure \
  --startup-project src/Api
```

### Step 6 — Verify the schema

```bash
dotnet ef migrations list \
  --project src/Infrastructure \
  --startup-project src/Api
```

The new migration should appear at the bottom with no `(Pending)` state.

## Project Layout Convention

This project separates concerns across projects:

```
src/
  Api/                  ← startup project (contains appsettings, Program.cs)
  Application/          ← use cases, interfaces
  Domain/               ← entities, value objects, domain logic
  Infrastructure/       ← DbContext, repositories, EF configurations, migrations
```

Always pass both `--project` (where DbContext lives) and `--startup-project`
(where the connection string and DI registration live).

## Hard Rules

- **Never write raw SQL migration files by hand.** Always use `dotnet ef migrations add`.
- **Never modify a generated migration file after it has been applied** to any shared environment (staging, production). Create a new migration instead.
- **Never rename a column by removing and re-adding it** — data will be lost. Use `migrationBuilder.RenameColumn()` explicitly.
- **Never apply migrations in unit tests.** Use `UseInMemoryDatabase` or transaction-rollback strategies for test isolation.
- **Never hardcode connection strings** in migration commands — use environment variables or user secrets.

## Destructive Change Checklist

Before applying any migration, verify:
- [ ] No unintended `DropColumn` statements
- [ ] No unintended `DropTable` statements
- [ ] Column type changes are backward-compatible or explicitly handled with data preservation
- [ ] Nullable-to-required column changes have a default value strategy
- [ ] Removed indexes are correctly listed in `Down()`
- [ ] Foreign key constraints are valid after the change

## Column Rename (Safe Pattern)

```csharp
// In the generated migration — edit ONLY this part, never the snapshot
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.RenameColumn(
        name: "OldName",
        table: "Posts",
        newName: "NewName");
}
```

Never achieve a rename via DropColumn + AddColumn — this destroys data.

## When EF Core Cannot Express the Change

If a schema requirement genuinely cannot be expressed through the EF Core fluent API
or data annotations (rare cases: partitioning, custom sequences, complex check constraints):

1. Use `migrationBuilder.Sql()` inside the generated migration
2. Document this explicitly in `plan.md` under `## Deviations`
3. Add a comment in the migration file explaining why EF Core was insufficient

This is an exception, not a pattern.