using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nexRemote.Server.API;
using nexRemote.Server.Data;
using nexRemote.Server.Services;
using nexRemote.Shared.Models;
using nexRemote.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Configuration;

namespace nexRemote.Tests
{
    [TestClass]
    public class IoCActivator
    {
        public static IServiceProvider ServiceProvider { get; set; }
        private static IWebHostBuilder builder;

        public static void Activate()
        {
            if (builder is null)
            {
                builder = WebHost.CreateDefaultBuilder()
                   .UseStartup<Startup>()
                   .CaptureStartupErrors(true)
                   .ConfigureAppConfiguration(config =>
                   {
                       config.AddInMemoryCollection(new Dictionary<string, string>()
                       {
                           ["ApplicationOptions:DBProvider"] = "InMemory"
                       });
                   });

                builder.Build();
            }
        }


        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            Activate();
        }
    }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDb, TestingDbContext>();

            services.AddIdentity<nexRemoteUser, IdentityRole>(options => options.Stores.MaxLengthForKeys = 128)
             .AddEntityFrameworkStores<AppDb>()
             .AddDefaultUI()
             .AddDefaultTokenProviders();

            services.AddTransient<IAppDbFactory, AppDbFactory>();
            services.AddTransient<IDataService, DataService>();
            services.AddTransient<IApplicationConfig, ApplicationConfig>();
            services.AddTransient<IEmailSenderEx, EmailSenderEx>();

            IoCActivator.ServiceProvider = services.BuildServiceProvider();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
        }
    }

}
