var builder = DistributedApplication.CreateBuilder(args);

builder.UseReadableAzureResourceNames();

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry).
var acaEnv = builder.AddAzureContainerAppEnvironment("aca-env");

var web = builder.AddWeb();
builder.AddAzureMonitoring(acaEnv, web);

var logtoDb = builder.AddLogtoDatabase();
var logto = builder.AddLogto(logtoDb);
web.WithLogto(logto);

builder.AddFrontend(web);

builder.Build().Run();
