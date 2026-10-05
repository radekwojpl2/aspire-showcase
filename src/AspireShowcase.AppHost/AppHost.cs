var builder = DistributedApplication.CreateBuilder(args);

builder.UseReadableAzureResourceNames();

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry).
var acaEnv = builder.AddAzureContainerAppEnvironment("aca-env");

// The message bus MassTransit runs on: booking events from web to the notifications service.
var messaging = builder.AddMessaging();

var web = builder.AddWeb().WithSampleDataCommands().WithMessaging(messaging);
var notifications = builder.AddNotifications().WithMessaging(messaging);
web.WithNotifications(notifications);

var bff = builder.AddBff(web);

builder.AddAzureMonitoring(acaEnv, bff, web, notifications);

var postgres = builder.AddPostgresServer();
web.WithAppDatabase(postgres);
bff.WithBffDatabase(postgres);
notifications.WithNotificationsDatabase(postgres);

var logto = builder.AddLogto(postgres.AddLogtoDatabase());
web.WithLogtoApi(logto);
bff.WithLogtoSignIn(logto);

var frontend = builder.AddFrontend(bff);
notifications.WithEmail(bff, frontend);

builder.Build().Run();
