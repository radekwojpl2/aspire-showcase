var builder = DistributedApplication.CreateBuilder(args);

builder.UseReadableAzureResourceNames();

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry).
var acaEnv = builder.AddAzureContainerAppEnvironment("aca-env");

var web = builder.AddWeb();
var notifications = builder.AddNotifications();
web.WithNotifications(notifications);

var bff = builder.AddBff(web);

builder.AddAzureMonitoring(acaEnv, bff, web, notifications);

var postgres = builder.AddPostgresServer();
web.WithAppDatabase(postgres);
bff.WithBffDatabase(postgres);

var logto = builder.AddLogto(postgres.AddLogtoDatabase());
web.WithLogtoApi(logto);
bff.WithLogtoSignIn(logto);

builder.AddFrontend(bff);

builder.Build().Run();
