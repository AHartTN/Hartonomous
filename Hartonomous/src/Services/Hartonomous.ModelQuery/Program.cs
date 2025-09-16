using Hartonomous.ModelQuery.Interfaces;
using Hartonomous.ModelQuery.Repositories;
using Hartonomous.ModelQuery.Services;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Repositories;
using Hartonomous.Infrastructure.Security;
using Hartonomous.Infrastructure.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured")))
        };
    });

builder.Services.AddAuthorization();

// Add infrastructure services (commented out until extension methods are available)
// builder.Services.AddSecurityServices();
// builder.Services.AddObservabilityServices();

// Add repositories
builder.Services.AddScoped<IModelRepository, ModelRepository>();
builder.Services.AddScoped<INeuralMapRepository, NeuralMapRepository>();
builder.Services.AddScoped<IModelWeightRepository, ModelWeightRepository>();
builder.Services.AddScoped<IModelArchitectureRepository, ModelArchitectureRepository>();
builder.Services.AddScoped<IModelVersionRepository, ModelVersionRepository>();

// Add services
builder.Services.AddScoped<IModelIntrospectionService, ModelIntrospectionService>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();