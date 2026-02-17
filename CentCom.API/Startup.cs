using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Serialization;
using CentCom.API.Services;
using CentCom.API.Services.Implemented;
using CentCom.Common.Configuration;
using CentCom.Common.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CentCom.API.Data;

namespace CentCom.API;

public class Startup(IConfiguration configuration)
{
    public IConfiguration Configuration { get; } = configuration;

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
	services.AddDbContext<DatabaseContext>(options =>
	    options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection")));

	services.AddIdentity<IdentityUser, IdentityRole>()
	    .AddEntityFrameworkStores<AuthDbContext>()
	    .AddDefaultTokenProviders();

        services.AddControllersWithViews().AddJsonOptions(x =>
        {
            x.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            x.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        }).AddRazorRuntimeCompilation();

        // Add DB context
        var dbConfig = new DbConfig();
        Configuration.Bind("dbConfig", dbConfig);
        if (dbConfig == null)
        {
            throw new Exception("Failed to read DB configuration, please ensure you provide one in appsettings.json");
        }
        switch (dbConfig.DbType)
        {
            case DbType.Postgres:
                services.AddDbContext<DatabaseContext, NpgsqlDbContext>();
                break;
            case DbType.MariaDB:
            case DbType.MySql:
                services.AddDbContext<DatabaseContext, MySqlDbContext>();
                break;
        }
	services.AddDbContext<AuthDbContext>();

        services.AddIdentity<IdentityUser, IdentityRole>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        services.AddTransient<IBanService, BanService>();
        services.AddTransient<IBanSourceService, BanSourceService>();

        // Add status service
        var statusService = new AppStatusService();
        services.AddSingleton<IAppStatusService>(statusService);

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "CentCom",
                Version = statusService.GetVersion().ToString(),
                Description = "An API for accesing CentCom, a central ban intelligence service for Space Station 13 servers"
            });

            // Set the comments path for the Swagger JSON and UI.
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            c.IncludeXmlComments(xmlPath);
        });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public async void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        var statusService = app.ApplicationServices.GetRequiredService<IAppStatusService>();
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.RoutePrefix = "swagger";
            c.SwaggerEndpoint("/swagger/v1/swagger.json", $"CentCom {statusService.GetVersion()}");
        });

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        using (var scope = app.ApplicationServices.CreateScope())
        {
            var services = scope.ServiceProvider;
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            string[] roles = { "Sudo", "Admin", "User" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }

        using (var scope = app.ApplicationServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            string username = "sudo";
            string password = "password";
            if (await userManager.FindByNameAsync(username) == null)
            {
                var user = new IdentityUser
                {
                    UserName = username,
                    Email = null,
                    EmailConfirmed = false
                };
                await userManager.CreateAsync(user, password);
                await userManager.AddToRoleAsync(user, "Sudo");
            }
        }

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute("default", "{controller=Viewer}/{action=Login}/{id?}");
        });
    }
}
