using Microsoft.OpenApi.Models;

namespace EasyWorkTogether.Api.Bootstrap;

public static class SwaggerSetup
{
    public static IServiceCollection AddApplicationSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "EasyWorkTogether API",
                Version = "v1",
                Description = "Backend API for EasyWorkTogether"
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Paste token like: Bearer {token}",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static WebApplication UseApplicationSwagger(this WebApplication app)
    {
        app.UseSwagger(options =>
        {
            options.PreSerializeFilters.Add((swagger, httpRequest) =>
            {
                var serverUrl = $"{httpRequest.Scheme}://{httpRequest.Host.Value}{httpRequest.PathBase.Value}".TrimEnd('/');

                swagger.Servers = new List<OpenApiServer>
                {
                    new() { Url = string.IsNullOrWhiteSpace(serverUrl) ? "/" : serverUrl }
                };
            });
        });

        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "EasyWorkTogether API v1");
            options.RoutePrefix = "swagger";
        });

        return app;
    }
}
