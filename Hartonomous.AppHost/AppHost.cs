var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Hartonomous_API>("hartonomous-api");

builder.Build().Run();
