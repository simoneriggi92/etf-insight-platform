---
name: dotnet-tests
description: >
  Runs .NET unit and integration tests and interprets results.
  Use this when asked to run tests, verify behavior after a change, or check
  test coverage on a specific component. Covers xUnit, NUnit, and MSTest projects.
---

# .NET Tests Skill

## When to Use This Skill

Load this skill when:
- Asked to "run tests", "verify this works", or "check if tests pass"
- After implementing a feature to verify no regressions
- When a test failure needs to be diagnosed and fixed
- When writing new tests for a feature in `plan.md`

## How to Run Tests

### Run all tests
```bash
dotnet test --no-build
```

### Run tests in a specific project
```bash
dotnet test tests/MyProject.Tests/MyProject.Tests.csproj --no-build
```

### Run tests matching a filter
```bash
# By test name (partial match)
dotnet test --no-build --filter "FullyQualifiedName~FindPaginated"

# By category/trait
dotnet test --no-build --filter "Category=Unit"
dotnet test --no-build --filter "Category=Integration"
```

### Run with detailed output
```bash
dotnet test --no-build --logger "console;verbosity=detailed"
```

### Run with coverage (if configured)
```bash
dotnet test --no-build --collect:"XPlat Code Coverage"
```

## Test Project Convention

```
tests/
  MyProject.Unit.Tests/       ← pure logic, no I/O, no database
  MyProject.Integration.Tests/ ← tests with real DB (use TestContainers or SQLite)
```

Unit tests must not touch the database, filesystem, or network.
Integration tests that need a database use `Testcontainers` or an in-memory provider.
Never use the production connection string in tests.

## Interpreting Results

### All tests pass
→ Continue. Mark the current task `[x]` in `plan.md` if applicable.

### Tests you broke
→ Fix them before moving to the next task.
→ Do not delete a failing test to make the suite green — that is worse than the failure.
→ If fixing requires a design change, surface it explicitly.

### Pre-existing failing tests (not caused by your changes)
→ Do not fix silently.
→ Note in `plan.md` under `## Observations`: `Pre-existing failing test: <TestClass>.<MethodName>`
→ Continue with your task.

## Writing Tests

### Naming convention
```
MethodName_Scenario_ExpectedResult
```

Examples:
- `FindPaginated_WithNoCursor_ReturnsFirstPage`
- `DecodeCursor_WithMalformedBase64_ThrowsInvalidCursorException`
- `CreatePost_WhenTitleIsEmpty_ReturnsBadRequest`

### Structure (Arrange / Act / Assert)
```csharp
[Fact]
public async Task FindPaginated_WithNoCursor_ReturnsFirstPage()
{
    // Arrange
    var repository = new PostsRepository(_dbContext);
    await SeedPosts(count: 25);

    // Act
    var result = await repository.FindPaginated(limit: 20, cursor: null);

    // Assert
    Assert.Equal(20, result.Items.Count);
    Assert.NotNull(result.NextCursor);
}
```

### Rules for test quality

- **One clear failure reason per test.** A test that can fail for five different reasons is not a test — it is a guess.
- **No logic in tests.** No `if`, no `for`, no `switch` inside a test body. If you need multiple cases, use `[Theory]` with `[InlineData]`.
- **No magic values.** Name your constants. `limit: 20` with a comment is better than a bare `20`.
- **No test interdependence.** Each test must be runnable in isolation. Do not rely on execution order.
- **No `Thread.Sleep` or arbitrary delays.** Use async/await properly. If timing matters, use a fake clock.

### Mocking
Use the project's established mocking library. Check existing tests to determine which is in use:
- `Moq` → `Mock<IService>()`
- `NSubstitute` → `Substitute.For<IService>()`
- `FakeItEasy` → `A.Fake<IService>()`

Do not mix mocking libraries within the same test project.

## Hard Rules

- Never delete a failing test to make the suite green
- Never use `Thread.Sleep` — use `await Task.Delay` only when absolutely necessary and with a comment
- Never share mutable state between tests (no static fields, no shared DbContext instances)
- Never test private methods directly — test behavior through the public interface
- Never assert on `ToString()` output of complex objects — assert on specific properties