---
name: new-endpoint
description: Scaffold a Khadra use case (CQRS command/query + handler + validator + tests) and its REST endpoint.
argument-hint: <Context> <UseCase>
---

Scaffold use case `$ARGUMENTS` (e.g. `Dealers SubmitDealerRegistration`).

1. Read `.claude/rules/backend/architecture.md` and the closest existing use case under `Khadra.Application/<Context>/` (start with `Khadra.Application/IdentityAccess/Login/`).
2. Create `Khadra.Application/<Context>/<UseCase>/` with `<UseCase>Command.cs` (or `Query.cs`) as a sealed record implementing `ICommand<Result<TDto>>` / `IQuery<Result<TDto>>`, a `<UseCase>Handler.cs` implementing `IRequestHandler<...>`, and a `<UseCase>Validator.cs` (FluentValidation).
3. Return `Result` failures via `AppError.*` for expected outcomes; never throw for business rules.
4. Authorize inside the handler using `ICurrentActor` (role, user id); the controller only adds `[Authorize(Policy = ...)]`.
5. Add the controller action in `Khadra.WebAPI/Controllers/<Context>Controller.cs` following the REST rules (status codes, `Location`, ProblemDetails) and a rate-limit policy.
6. Add handler tests under `Khadra.Tests/Application/<Context>/` with NSubstitute doubles, covering success and each failure branch.
7. Run `dotnet build Khadra.slnx` and `dotnet test Khadra.slnx` before reporting completion.
