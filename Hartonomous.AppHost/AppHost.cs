using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUser = builder.AddParameter("postgres-user", "hartonomous");
var postgresPassword = builder.AddParameter("postgres-password", "hartonomous", secret: true);

var postgres = builder
	.AddPostgres("postgres", postgresUser, postgresPassword)
	.WithHostPort(5433)
	.WithImage("postgis/postgis", "17-3.5")
	.WithInitFiles("../Hartonomous.Native/sql");

var postgresDb = postgres.AddDatabase("Postgres", "hartonomous");

var redis = builder.AddRedis("Redis", port: 6379);

var dbUrl = "postgresql://hartonomous:hartonomous@localhost:5433/hartonomous";

builder.AddProject<Projects.Hartonomous_Web>("hartonomous-web")
	.WithReference(postgresDb)
	.WithReference(redis)
	.WithEnvironment("HARTONOMOUS_DB_URL", dbUrl)
	.WaitFor(postgresDb)
	.WaitFor(redis);

builder.AddProject<Projects.Hartonomous_Worker>("hartonomous-worker")
	.WithReference(postgresDb)
	.WithReference(redis)
	.WithEnvironment("HARTONOMOUS_DB_URL", dbUrl)
	.WaitFor(postgresDb)
	.WaitFor(redis);

builder.Build().Run();
