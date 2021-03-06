﻿using Common.DataAccess;
using Common.Logging;
using KlipperApi.Controllers.Attendance;
using KlipperApi.DataAccess;
using KlipperApi.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Models.Core.Authentication;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Text;

namespace KlipperApi
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                //.AddJsonFile("AuthorizationPolicies.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();

            Log.Logger = new LoggerConfiguration()
                    .WriteTo.Async(a => a.File("KlipperApi_log_.log", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true), blockWhenFull: true)
                    .Enrich.FromLogContext()
                    .MinimumLevel.ControlledBy(new LoggingLevelSwitch() { MinimumLevel = LogEventLevel.Information })
                    .Enrich.WithEnvironmentUserName()
                    .Enrich.WithMachineName()
                    .Enrich.WithThreadId()
                    .CreateLogger();

            Log.Information("KlipperApi starting up...");
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
            });

            services.AddMvcCore(options =>
                    {
                        //For policy server... this sets up a default authorization policy for the application in this case, 
                        //authenticated users are required (besides controllers/actions that have [AllowAnonymous]
                        var policy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build();
                        options.Filters.Add(new AuthorizeFilter(policy));
                    }
                )
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1)
                .AddApiExplorer()
                .AddAuthorization()
                .AddJsonFormatters();

            services.Configure<DBConnectionSettings>(options =>
            {
                options.ConnectionString = Configuration.GetSection("MongoConnection:ConnectionString").Value;
                options.Database = Configuration.GetSection("MongoConnection:Database").Value;
            });

            services.AddIdentity<User, IdentityRole>()
                .AddDefaultTokenProviders();

            services.AddSingleton<IUserRepository, UserRepository>();
            services.AddSingleton<IEmployeeAccessor, EmployeeAccessor>();
            services.AddSingleton<IAttendanceAccessor, AttendanceAccessor>();

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.SaveToken = true;
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidAudience = "http://www.Klingelnberg.com",
                        ValidIssuer = "http://www.Klingelnberg.com",
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("KlipperSigningKey"))
                    };
                });

            // Sets up the PolicyServer client library and policy provider - 
            //configuration is loaded from AuthorizationPolicies.json
            //IConfiguration configSection = Configuration.GetSection("Policy");
            //services.AddPolicyServerClient(configSection)
            //    .AddAuthorizationPermissionPolicies();

            // Adds the necessary handler for our custom additional requirements
            services.AddTransient<IPolicyEvaluator, PolicyEvaluator>();
            //KK: This could be separated into a separate DLL so that 
            //requirements can be (more or less) dynamically configured
            services.AddCustomPolicyRequirements();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            app.UseCors("CorsPolicy");

            loggerFactory.AddSerilog(Log.Logger);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }
            // add this middleware to make roles and permissions available as claims
            // this is mainly useful for using the classic [Authorize(Roles="foo")] and IsInRole functionality
            // this is not needed if you use the client library directly or the new policy-based authorization framework in ASP.NET Core
            //app.UsePolicyServerClaims();
            app.UseMiddleware<SerilogMiddleware>();
            app.UseAuthentication();
            //app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseMvc();
        }
    }
}
