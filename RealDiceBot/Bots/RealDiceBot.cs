// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio EchoBot v4.10.2

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RealDiceBot.Services;
using RealDiceCommon.Models.Roll;
using RealDiceCommon.Utils;

namespace RealDiceBot.Bots
{
    public class RealDiceBot : ActivityHandler
    {
        private readonly string _botId;
        private readonly ILogger<RealDiceBot> logger;
        private readonly Random randomizer = new Random();
        private readonly IRollService rollService;

        public RealDiceBot(IConfiguration configuration, IRollService rollService, ILoggerFactory logger)
        {
            _botId = configuration["MicrosoftAppId"] ?? Guid.NewGuid().ToString();
            this.logger = logger.CreateLogger<RealDiceBot>();
            this.rollService = rollService;
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var rollRequest = RollRequest.Real1D6;
            await rollService.RequestAsync(turnContext.Activity, rollRequest);

            var result = randomizer.Next(1, 7);
            var replyText = $"1d6 = {result} ?";
            await turnContext.SendActivityAsync(MessageFactory.Text(replyText, replyText), cancellationToken);
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            var welcomeText = "Hello. I'm Real DiceBot. " +
                "I can roll 1d6 Only! " +
                "Unfortunately it's not \"real\" now.";
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText, welcomeText), cancellationToken);
                }
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

                    await context.SendActivityAsync("Result by Functions: " + RealDiceConverter.Serialize(res.Results));
                }, cancellationToken);
            }
            else
            {
                await base.OnEventActivityAsync(turnContext, cancellationToken);
            }
        }
    }
}
