---
applyTo: "**/*.cs"
---

# .NET Coding Standards

These rules apply to every C# file, every suggestion, every interaction. No exceptions.

## Core Principles

- **Correctness over cleverness.** Readable, verifiable code beats concise code that requires a comment to explain.
- **Fit the codebase.** Before introducing a new pattern, verify whether an equivalent already exists. Duplication is a defect.
- **Explicit is better than implicit.** Name things for what they do. Avoid magic, hidden side effects, or behavior that surprises the reader.
- **Fail loudly at boundaries.** Validate inputs at entry points. Do not propagate invalid state deep into the domain.

## Nullability

- `<Nullable>enable</Nullable>` is assumed in all projects.
- Never use `!` (null-forgiving operator) to silence a warning you introduced.
- Never use `object?` or `dynamic` as a type escape hatch.
- Use `required` on properties that must always be set at construction time.
- Use `ArgumentNullException.ThrowIfNull()` at all public method boundaries.

```csharp
// Wrong
public void Process(Order order) { var id = order!.Id; }

// Correct
public void Process(Order order)
{
    ArgumentNullException.ThrowIfNull(order);
    var id = order.Id;
}
```

## Type Design

- **Prefer records for immutable data.** Use `record` or `record class` for DTOs, value objects, and query results.
- **Prefer sealed classes** unless inheritance is explicitly designed for.
- **Use discriminated unions via OneOf or custom result types** instead of throwing exceptions for expected failure paths.
- **Do not expose internal implementation types** in public API contracts.
- **No anemic domain models.** Entities should contain behavior, not just properties.

```csharp
// Prefer result types over exceptions for expected failures
public Result<Post, CursorError> FindPaginated(int limit, string? cursor);

// Not this — exception as control flow for expected cases
public Post FindPaginated(int limit, string? cursor); // throws CursorException
```

## Error Handling

- **Do not use exceptions for control flow on expected failure paths.** Use result types (`Result<T, E>`, `OneOf<T, E>`, or custom discriminated unions).
- **Every `catch` block must either re-throw, log with context, or return a typed error.** Empty catch blocks are not permitted.
- **Catch specific exceptions, not `Exception`.** Broad catches hide bugs.
- **Do not swallow `OperationCanceledException`.** Let it propagate or re-throw it explicitly.

```csharp
// Wrong
try { await DoWork(); } catch (Exception) { }

// Correct
try { await DoWork(cancellationToken); }
catch (InvalidCursorException ex) { return Result.Fail(new CursorError(ex.Message)); }
```

## Async

- **All I/O is async.** Never use `.Result`, `.Wait()`, or `Task.Run()` to block on async code.
- **Always pass `CancellationToken`** through the full call chain from controller to repository.
- **Never use `async void`** except for event handlers that have no alternative.
- **Name async methods with the `Async` suffix** unless they return `Task` from an interface that does not use the convention.

```csharp
// Wrong
public Post GetPost(Guid id) => _repository.GetAsync(id).Result;

// Correct
public async Task<Post?> GetPostAsync(Guid id, CancellationToken ct)
    => await _repository.GetAsync(id, ct);
```

## Dependency Injection

- **Register dependencies by interface, not by concrete type**, unless the concrete type is the intended contract.
- **Do not use service locator pattern** (`IServiceProvider.GetService()` inside domain or application logic).
- **Do not inject `IServiceProvider`** into classes — inject the specific dependency you need.
- **Respect lifetimes.** Never inject a scoped service into a singleton.

## Code Style

- **No unnecessary comments.** If a comment explains *what* the code does, rewrite the code. Comments explain *why* — non-obvious constraints, external system requirements, intentional trade-offs.
- **No XML doc comments on internal or private members** unless the member is part of a public API surface.
- **No dead code.** Remove unused usings, variables, methods, and commented-out blocks.
- **Methods do one thing.** If a method needs a comment to separate two logical sections, it should be two methods.
- **No magic strings or numbers.** Use named constants, enums, or configuration values.

## LINQ

- **Prefer method syntax over query syntax** for consistency.
- **Do not materialize collections unnecessarily.** Chain LINQ before calling `.ToList()` or `.ToArray()`.
- **Do not use LINQ inside hot loops** where allocation matters — measure first.
- **Name lambda parameters meaningfully.** `posts.Select(p => p.Id)` is better than `posts.Select(x => x.Id)` when the type is not obvious from context.

## EF Core

- **All queries go through the repository.** No raw LINQ-to-EF in controllers or services.
- **No lazy loading.** Use explicit `.Include()` and `.ThenInclude()` for navigation properties.
- **No `SaveChanges()` in repositories.** The unit of work boundary is the service or command handler, not the repository.
- **Use `AsNoTracking()`** for read-only queries that do not need change tracking.
- **Never use `Find()` for queries that will filter or project** — use `Where()` with explicit projections.

## Testing

- Tests follow Arrange / Act / Assert structure.
- Test names follow `MethodName_Scenario_ExpectedResult`.
- No logic in tests — no `if`, no `for`, no `switch`.
- No test interdependence — each test runs in isolation.
- No `Thread.Sleep` — use fake clocks for time-dependent logic.

## Naming

- Classes, records, enums: `PascalCase`
- Methods, properties: `PascalCase`
- Local variables, parameters: `camelCase`
- Private fields: `_camelCase`
- Constants: `PascalCase`
- Interfaces: `IPascalCase`
- Async methods: `MethodNameAsync`

## General

- **Do not expand scope without explicit instruction.** If you notice an adjacent issue, flag it — do not fix it silently.
- **Do not change public interface signatures** without explicit approval.
- **Prefer reversible decisions.** When two approaches are equivalent, choose the one that is easier to undo.
