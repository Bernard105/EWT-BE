# Backend refactor notes

## Summary
- Reorganized backend source into `src/Bootstrap`, `src/Shared`, and `src/Features`.
- Split `Program.cs` into bootstrap helpers for Swagger, CORS, and dependency registration.
- Split `Models/ApiModels.cs` into feature-scoped contract files and shared common contracts.
- Preserved endpoint namespaces, service namespaces, route paths, request/response shapes, and runtime logic as much as possible.

## Large files split
- `Program.cs`
- `Models/ApiModels.cs`

## Structural additions
- Added `src/Shared/Infrastructure/Db/NpgsqlConnectionFactory.cs` and registered it in DI. This is additive and does not change existing endpoint/service behavior.

## Business-logic changes
- No intended business-rule changes.
- No API route, contract shape, auth flow, workspace flow, task flow, or realtime flow was intentionally changed.

## Verification
- Executable backend build was **not** run because this environment does not have `.NET SDK` / `dotnet`.
- Performed text-level verification only:
  - checked that old backend layout folders were removed after move
  - checked that old moved namespaces/usings were updated
  - checked for duplicate contract declarations after splitting `ApiModels.cs`

## Notes
- To minimize risk without a working `dotnet build`, endpoint and service namespaces were largely preserved even after moving files into feature folders.
- Shared utility code in `AppSupport.cs` was moved but not deeply rewritten.
