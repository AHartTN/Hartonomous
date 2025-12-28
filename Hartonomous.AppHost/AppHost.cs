var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Hartonomous_Web>("hartonomous-web");

builder.AddProject<Projects.Hartonomous_Worker>("hartonomous-worker");

builder.Build().Run();
