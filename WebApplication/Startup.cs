using System.IO;
using System.Net.Http;
using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebApplication.Definitions;
using WebApplication.Processing;
using WebApplication.Utilities;

namespace WebApplication
{
    public class Startup
    {
        private const string ForgeSectionKey = "Forge";
        private const string AppBundleZipPathsKey = "AppBundleZipPaths";
        private const string DefaultProjectsSectionKey = "DefaultProjects";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "ClientApp/build";
            });

            services.AddHttpClient();

            // NOTE: eventually we might want to use `AddForgeService()`, but right now it might break existing stuff
            // https://github.com/Autodesk-Forge/forge-api-dotnet-core/blob/master/src/Autodesk.Forge.Core/ServiceCollectionExtensions.cs
            services.Configure<ForgeConfiguration>(Configuration.GetSection(ForgeSectionKey));
            services.AddSingleton<ResourceProvider>();
            services.AddTransient<IForgeOSS, ForgeOSS>(); // ER: TODO: this will fail on token expiration, need extra work to refresh token
            services.Configure<AppBundleZipPaths>(Configuration.GetSection(AppBundleZipPathsKey));
            services.AddSingleton<FdaClient>();
            services.Configure<DefaultProjectsConfiguration>(Configuration.GetSection(DefaultProjectsSectionKey));
            services.AddTransient<Initializer>();
            services.AddTransient<Arranger>();
            services.AddSingleton<DesignAutomationClient>(provider =>
                                    {
                                        var forge = provider.GetService<IForgeOSS>();
                                        var httpMessageHandler = new ForgeHandler(Options.Create(forge.Configuration))
                                        {
                                            InnerHandler = new HttpClientHandler()
                                        };
                                        var forgeService = new ForgeService(new HttpClient(httpMessageHandler));
                                        return new DesignAutomationClient(forgeService);
                                    });
            services.AddSingleton<Publisher>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, Initializer initializer, ILogger<Startup> logger)
        {
            if(Configuration.GetValue<bool>("clear"))
            {
                logger.LogInformation("-- Clean up --");
                initializer.ClearAsync().Wait();
            }

            if(Configuration.GetValue<bool>("initialize"))
            {
                logger.LogInformation("-- Initialization --");
                initializer.InitializeAsync().Wait();
            }

            if (env.IsDevelopment())
            {
                logger.LogInformation("In Development environment");
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            // expose 'LocalCache' dir as 'data' virtual dir to serve locally cached OSS files
            var localDir = Path.Combine(Directory.GetCurrentDirectory(), "LocalCache");
            // make sure that directory exists
            System.IO.Directory.CreateDirectory(localDir);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(localDir),
                RequestPath = new PathString("/data")
            });

            app.UseSpaStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "ClientApp";

                if (env.IsDevelopment())
                {
                    spa.UseReactDevelopmentServer(npmScript: "start");
                }
            });
        }
    }
}