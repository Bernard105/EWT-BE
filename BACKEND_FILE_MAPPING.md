| Old path | New path |
|---|---|
| `Endpoints/AuthEndpoints.cs` | `src/Features/Auth/Endpoints/AuthEndpoints.cs` |
| `Endpoints/ChatEndpoints.cs` | `src/Features/Chat/Endpoints/ChatEndpoints.cs` |
| `Endpoints/FriendEndpoints.cs` | `src/Features/Friend/Endpoints/FriendEndpoints.cs` |
| `Endpoints/NotificationEndpoints.cs` | `src/Features/Notification/Endpoints/NotificationEndpoints.cs` |
| `Endpoints/RealtimeEndpoints.cs` | `src/Features/Realtime/Endpoints/RealtimeEndpoints.cs` |
| `Endpoints/TaskEndpoints.cs` | `src/Features/Task/Endpoints/TaskEndpoints.cs` |
| `Endpoints/UploadEndpoints.cs` | `src/Features/Upload/Endpoints/UploadEndpoints.cs` |
| `Endpoints/WorkspaceEndpoints.cs` | `src/Features/Workspace/Endpoints/WorkspaceEndpoints.cs` |
| `Filters/RequireSessionFilter.cs` | `src/Shared/Infrastructure/Auth/RequireSessionFilter.cs` |
| `Infrastructure/AppSupport.cs` | `src/Shared/Utils/AppSupport.cs` |
| `Infrastructure/DbInitializer.cs` | `src/Shared/Infrastructure/Db/DbInitializer.cs` |
| `Infrastructure/DeploymentSupport.cs` | `src/Bootstrap/DeploymentSupport.cs` |
| `Middleware/ExceptionHandlingMiddleware.cs` | `src/Shared/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs` |
| `Models/ApiModels.cs` | `src/Features/*/Contracts/*.cs + src/Shared/Contracts/Common/*.cs` |
| `Program.cs` | `src/Bootstrap/Program.cs` |
| `Services/AuthService.cs` | `src/Features/Auth/Services/AuthService.cs` |
| `Services/ChatService.cs` | `src/Features/Chat/Services/ChatService.cs` |
| `Services/DeadlineNotificationWorker.cs` | `src/Features/Notification/Services/DeadlineNotificationWorker.cs` |
| `Services/EmailService.cs` | `src/Features/Auth/Services/EmailService.cs` |
| `Services/NotificationService.cs` | `src/Features/Notification/Services/NotificationService.cs` |
| `Services/PasswordService.cs` | `src/Features/Auth/Services/PasswordService.cs` |
| `Services/RealtimeConnectionManager.cs` | `src/Features/Realtime/Services/RealtimeConnectionManager.cs` |
