// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio EchoBot v4.10.2

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RealDiceBot.Models;
using RealDiceBot.Services;
using RealDiceCommon.Models.Roll;
using RealDiceCommon.Utils;

namespace RealDiceBot.Bots
{
    public class RealDiceBot : ActivityHandler
    {
        private readonly string _botId;
        private readonly ILogger<RealDiceBot> logger;
        private readonly IRollService rollService;
        private readonly StaticAssets staticAssets;

        public RealDiceBot(
            IConfiguration configuration,
            IRollService rollService,
            StaticAssets staticAssets,
            ILoggerFactory logger)
        {
            _botId = configuration["MicrosoftAppId"] ?? Guid.NewGuid().ToString();
            this.logger = logger.CreateLogger<RealDiceBot>();
            this.rollService = rollService;
            this.staticAssets = staticAssets;
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var rollRequest = RollRequest.Real1D6;
            await rollService.RequestAsync(turnContext.Activity, rollRequest);

            var replyText = $"1d6? Wait a minute!";
            await turnContext.SendActivityAsync(MessageFactory.Text(replyText, replyText), cancellationToken);
        }

        // XXX さしあたってウェルカムメッセージ要らん気がする。
        //protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        //{
        //    var welcomeText = "Hello. I'm Real DiceBot. " +
        //        "I can roll 1d6 Only! " +
        //        "Unfortunately it's not \"real\" now.";
        //    foreach (var member in membersAdded)
        //    {
        //        if (member.Id != turnContext.Activity.Recipient.Id)
        //        {
        //            await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText, welcomeText), cancellationToken);
        //        }
        //    }
        //}

        private IList<Attachment> GetAttachments(RollResult res)
        {
            if (string.IsNullOrEmpty(res.PhotoUrl))
            {
                return res.Results
                    .Select(x => $"{x}.jpg")
                    .Select(x => staticAssets.Files[x])
                    .Select(x => new Attachment
                    {
                        Name = x.Name,
                        ContentType = x.ContentType,
                        ContentUrl = x.Url,
                    }).ToList();
            }
            else
            {
                var attachements = new List<Attachment>();
                if (!string.IsNullOrEmpty(res.VideoUrl))
                {
                    attachements.Add(new Attachment
                    {
                        Name = Path.GetFileName(res.VideoUrl),
                        ContentType = "video/x-msvideo",
                        ContentUrl = res.VideoUrl,
                    });
                }
                if (!string.IsNullOrEmpty(res.PhotoUrl))
                {
                    attachements.Add(new Attachment
                    {
                        Name = Path.GetFileName(res.PhotoUrl),
                        ContentType = "image/jpg",
                        ContentUrl = res.PhotoUrl,
                    });
                }

                return attachements;
            }
        }

        protected override async Task OnEventActivityAsync(ITurnContext<IEventActivity> turnContext, CancellationToken cancellationToken)
        {
            if (turnContext.Activity.ChannelId == Channels.Directline && turnContext.Activity.Name == "RollResult")
            {
                var continueConversationActivity = (turnContext.Activity.Value as JObject)?.ToObject<Activity>();
                await turnContext.Adapter.ContinueConversationAsync(_botId, continueConversationActivity.GetConversationReference(), async (context, cancellation) =>
                {
                    logger.LogInformation(continueConversationActivity.Value as string);
                    var res = RealDiceConverter.Deserialize<RollContext>(continueConversationActivity.Value as string);
                    var res0 = res.Results[0];
                    string exMessage = "";
                    if (res0.Status != "Success")
                    {
                        exMessage = $"({res0.Status})\n";
                    }
                    var message =
                        $"1d6 = {res0.Results[0]} !\n" +
                        $"(Score: {res0.Score})\n" +
                        exMessage +
                        $"> {continueConversationActivity.Text}";

                    var activity = MessageFactory.Text(message);
                    activity.Attachments = GetAttachments(res0);

                    await context.SendActivityAsync(activity, cancellationToken);
                }, cancellationToken);
            }
            else
            {
                await base.OnEventActivityAsync(turnContext, cancellationToken);
            }
        }
    }
}
