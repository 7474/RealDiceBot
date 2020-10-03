// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio EchoBot v4.10.2

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging.AzureAppServices;

namespace RealDiceBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((hostingContext, builder) =>
                {
                    // https://docs.microsoft.com/ja-jp/aspnet/core/fundamentals/logging/?view=aspnetcore-3.1
                    builder.AddAzureWebAppDiagnostics();

                    // https://docs.microsoft.com/ja-jp/azure/azure-monitor/app/ilogger#aspnet-core-applications
                    builder.AddApplicationInsights(hostingContext.Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"]);
                    builder.AddFilter<ApplicationInsightsLoggerProvider>("", LogLevel.Warning);
                })
                .ConfigureServices(serviceCollection => serviceCollection
                    .Configure<AzureFileLoggerOptions>(options =>
                    {
                        options.FileName = "azure-diagnostics-";
                        options.FileSizeLimit = 50 * 1024;
                        options.RetainedFileCountLimit = 5;
                    })
                    .Configure<AzureBlobLoggerOptions>(options =>
                    {
                        options.BlobName = "log.txt";
                    }))
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
