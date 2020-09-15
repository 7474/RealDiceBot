// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio EchoBot v4.10.2

using Azure.Storage.Queues;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RealDiceBot.Models.Options;
using RealDiceBot.Services;
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

            // Create the Bot Framework Adapter with error handling enabled.
            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            // Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
            services.AddTransient<IBot, Bots.RealDiceBot>();

            services.AddSingleton<IRollService, RollService>();

            // 正直どこで動かすのか考えあぐねているが、
            // 今後Steam処理やWebhookでのコールバックを主にしていく場合Functionsではなく、
            // かつWebのエンドポイントを設けることになるのでServiceはDIコンテナに入れることになるだろう。
            // しかし、Bot Frameworkのストリームは会話ID毎なので、スケールするには会話ID毎に責務分けする必要がある。
            // それを自前実装しなくてはならない（多分）とか、独自のチャンネルとDirectLineさせる気はあるんだろうか。
            var twitterBotIntegrationManager = CreateTwitterBotIntegrationManager(Configuration);
            services.AddSingleton(twitterBotIntegrationManager);
            if (Env.IsDevelopment())
            {
                twitterBotIntegrationManager.Start();
            }
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
