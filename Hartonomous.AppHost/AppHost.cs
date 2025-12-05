var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Hartonomous_Api>("hartonomous-api");

builder.AddProject<Projects.Hartonomous_Worker>("hartonomous-worker");

// Remove MAUI app from AppHost; it's not a deployable service in Aspire
// builder.AddProject<Projects.Hartonomous_App>("hartonomous-app");

builder.AddProject<Projects.Hartonomous_App_Web>("hartonomous-app-web");

builder.Build().Run();
