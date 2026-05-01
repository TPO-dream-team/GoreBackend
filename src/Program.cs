using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ML;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using src.AI;
using src.Models;
using System.Text;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<GoreDBContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<ModelStorageOptions>(
    builder.Configuration.GetSection(ModelStorageOptions.SectionName));

var modelPathConfigured = builder.Configuration[$"{ModelStorageOptions.SectionName}:ModelPath"] ?? "Assets/model.zip";
var modelPath = Path.IsPathRooted(modelPathConfigured)
    ? modelPathConfigured
    : Path.Combine(builder.Environment.ContentRootPath, modelPathConfigured);

var modelDirectory = Path.GetDirectoryName(modelPath);
if (!string.IsNullOrEmpty(modelDirectory))
{
    Directory.CreateDirectory(modelDirectory);
}

builder.Services.AddPredictionEnginePool<ModelInput, ModelOutput>()
    .FromFile(modelName: "ClassifierModel", filePath: modelPath, watchForChanges: true);

builder.Services.AddSingleton<IPredictionService, PredictionService>();

builder.Services.AddSingleton<IModelMetricsStore, JsonModelMetricsStore>();

builder.Services.AddSingleton<IModelManager>(sp =>
{
    var predictionService = sp.GetRequiredService<IPredictionService>();
    var metricsStore = sp.GetRequiredService<IModelMetricsStore>();
    var options = sp.GetRequiredService<IOptions<ModelStorageOptions>>().Value;

    var path = Path.IsPathRooted(options.ModelPath)
        ? options.ModelPath
        : Path.Combine(builder.Environment.ContentRootPath, options.ModelPath);

    return new ModelManager(predictionService, metricsStore, path, options.RequiredTotalRows);
});

var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "FallbackSecretKeyThatIsAtLeast32CharsLong");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Info.Title = "Gore API specification";
        document.Info.Version = "v1.0";

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste your access token here (without 'Bearer ')"
        };

        document.Security = new List<OpenApiSecurityRequirement>
        {
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer"),
                    new List<string>()
                }
            }
        };

        return Task.CompletedTask;
    });
});

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowEveryone", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// --- Middleware Pipeline ---
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
        options.OAuthUsePkce();
    });
}

app.UseCors("AllowEveryone");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();