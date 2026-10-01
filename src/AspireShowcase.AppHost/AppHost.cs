var builder = DistributedApplication.CreateBuilder(args);

builder.UseReadableAzureResourceNames();

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry).
var acaEnv = builder.AddAzureContainerAppEnvironment("aca-env");

var web = builder.AddWeb().WithFailureCommands();
builder.AddAzureMonitoring(acaEnv, web);

var postgres = builder.AddPostgresServer();
web.WithAppDatabase(postgres);
web.WithCache();

var logto = builder.AddLogto(postgres.AddLogtoDatabase());
web.WithLogto(logto);

builder.AddFrontend(web);

builder.Build().Run();
