using Hartonomous.Infrastructure.Configuration;
using Hartonomous.Infrastructure.Observability;
using Hartonomous.Infrastructure.Security;
using Hartonomous.Orchestration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add infrastructure services
builder.Services.AddHartonomousConfiguration(builder.Configuration);
builder.Services.AddHartonomousObservability();
builder.Services.AddHartonomousSecurity(builder.Configuration);

// Add orchestration services
builder.Services.AddOrchestrationServices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();