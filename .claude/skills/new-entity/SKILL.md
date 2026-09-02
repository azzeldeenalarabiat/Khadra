---
name: new-entity
description: Scaffold a new Khadra.Domain aggregate/entity following the DDD rules (smart enums, value objects, soft delete, EF configuration, migration, tests).
argument-hint: <Context> <EntityName>
---

Scaffold aggregate `$ARGUMENTS` (first token = bounded context folder, e.g. `Dealers Dealer`).

1. Read `.claude/rules/backend/architecture.md` and the closest existing aggregate in `Khadra.Domain/<Context>/` (start with `Khadra.Domain/IdentityAccess/User.cs`).
2. Create the aggregate under `Khadra.Domain/<Context>/`: sealed class inheriting `AggregateRoot`, private setters, static factory that validates inputs, behavior methods returning `Result`, domain events under `<Context>/Events/`.
3. Model status/type fields as smart enums (`Enumeration`). Model strings with meaning as value objects.
4. Implement `ISoftDeletable` unless the aggregate is genuinely immutable.
5. Add `IEntityTypeConfiguration<T>` under `Khadra.Infrastructure/Persistence/Configurations/<Context>/` with snake_case table name, indexes, `DeleteBehavior.Restrict`, smart-enum and value-object conversions; register the `DbSet` in `KhadraDbContext`.
6. Add a migration: `dotnet ef migrations add <Name> --project Khadra.Infrastructure --startup-project Khadra.WebAPI`.
7. Add domain tests under `Khadra.Tests/Domain/<Context>/` covering every invariant.
8. Run `dotnet build Khadra.slnx` and `dotnet test Khadra.slnx` before reporting completion.
