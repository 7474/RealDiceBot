﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio EchoBot v4.10.2

using Azure.Storage.Queues;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.ApplicationInsights;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RealDiceBot.Models;
using RealDiceBot.Models.Options;
using RealDiceBot.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Policy;
using TwitterBotFWIntegration;

namespace RealDiceBot
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            Env = env;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Env { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers().AddNewtonsoftJson();


            // https://docs.microsoft.com/ja-jp/azure/bot-service/bot-builder-telemetry?view=azure-bot-service-4.0&tabs=csharp\
            // Create the Bot Framework Adapter with error handling enabled.
            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            // Add Application Insights services into service collection
            services.AddApplicationInsightsTelemetry();

            // Create the telemetry client.
            services.AddSingleton<IBotTelemetryClient, BotTelemetryClient>();

            // Add telemetry initializer that will set the correlation context for all telemetry items.
            services.AddSingleton<ITelemetryInitializer, OperationCorrelationTelemetryInitializer>();

            // Add telemetry initializer that sets the user ID and session ID (in addition to other bot-specific properties such as activity ID)
            services.AddSingleton<ITelemetryInitializer, TelemetryBotIdInitializer>();

            // Create the telemetry middleware to initialize telemetry gathering
            services.AddSingleton<TelemetryInitializerMiddleware>();

            // Create the telemetry middleware (used by the telemetry initializer) to track conversation events
            services.AddSingleton<TelemetryLoggerMiddleware>();

            // Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
            services.AddTransient<IBot, Bots.RealDiceBot>();

            // XXX アダプタを追加する場合はApplication Insightsに対応している必要がある 

            services.AddSingleton<IRollService, RollService>();

            // 正直どこで動かすのか考えあぐねているが、
            // 今後Steam処理やWebhookでのコールバックを主にしていく場合Functionsではなく、
            // かつWebのエンドポイントを設けることになるのでServiceはDIコンテナに入れることになるだろう。
            // しかし、Bot Frameworkのストリームは会話ID毎なので、スケールするには会話ID毎に責務分けする必要がある。
            // それを自前実装しなくてはならない（多分）とか、独自のチャンネルとDirectLineさせる気はあるんだろうか。
            var twitterBotIntegrationManager = CreateTwitterBotIntegrationManager(Configuration);
            services.AddSingleton(twitterBotIntegrationManager);
            if (!Env.IsDevelopment())
                {
                twitterBotIntegrationManager.Start();
            }

            //var basePath = Env.ContentRootPath;
            var baseUrl = new Uri(Configuration["BaseUrl"]);
            var diceFiles = new string[] { "1.jpg", "2.jpg", "3.jpg", "4.jpg", "5.jpg", "6.jpg", }
                .Select(x => Path.Combine("images", "dice", x))
                .Select(x => new StaticAssetFile
                {
                    ContentType = "image/jpeg",
                    Path = x,
                    Url = new Uri(baseUrl, x).ToString(),
                })
                .ToList();
            services.AddSingleton(new StaticAssets(diceFiles));
            services.AddApplicationInsightsTelemetry();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHttpsRedirection();
            }

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseWebSockets()
                .UseRouting()
                .UseAuthorization()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });

            // Allow the bot to use named pipes.
            app.UseNamedPipes(System.Environment.GetEnvironmentVariable("APPSETTING_WEBSITE_SITE_NAME") + ".directline");
        }

        private TwitterBotIntegrationManager CreateTwitterBotIntegrationManager(IConfiguration configuration)
        {
            var directLineSecret = Configuration["DirectLineSecret"];
            var twitterOptions = new TwitterOptions();
            Configuration.GetSection("Twitter").Bind(twitterOptions);

            return new TwitterBotIntegrationManager(
                directLineSecret,
                twitterOptions.ConsumerKey,
                twitterOptions.ConsumerSecret,
                twitterOptions.BearerToken,
                twitterOptions.AccessToken,
                twitterOptions.AccessTokenSecret);
        }
    }
}
