var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Hartonomous_API>("hartonomous-api");

builder.AddProject<Projects.Hartonomous_Web>("hartonomous-web")
    .WithExternalHttpEndpoints()
    .WithReference(api);

builder.Build().Run();
