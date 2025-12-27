var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Hartonomous_App>("hartonomous-app");

builder.AddProject<Projects.Hartonomous_App_Web>("hartonomous-app-web");

builder.AddProject<Projects.Hartonomous_Worker>("hartonomous-worker");

builder.Build().Run();
