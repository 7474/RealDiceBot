using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using RealDiceCommon.Models.Roll;
using RealDiceCommon.Utils;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Bot.Connector.DirectLine;
using Newtonsoft.Json;

namespace RealDiceFunctions
{
    public static class RealDiceRoll
    {
        private static Random randomizer = new Random();

        [FunctionName("HandleRequest")]
        public static async Task HandleRequestAsync(
            [QueueTrigger("roll-request-items")] RollContext req,
            // TODO 結果じゃなくてRialDiceのロールへの要求にする。
            // Functionかまさないで直撃でそちらに行ってもいいのかもしれんけれど、それは後で考える。
            [Queue("roll-result-items")] IAsyncCollector<RollContext> rollQueue,
            ILogger log)
        {
            log.LogInformation($"HandleRequestAsync processed: {RealDiceConverter.Serialize(req)}");

            // 行儀は悪いが間に合わせなので良し。
            var res = req;
            res.Results = req.Requests.Select(x => new RollResult
            {
                Request = x,
                Results = Enumerable.Range(0, (int)x.N).Select(x => (uint)randomizer.Next(1, 7)).ToArray(),
            }).ToList();

            await rollQueue.AddAsync(res);
        }

        [FunctionName("HandleResult")]
        public static async Task HandleResultAsync(
            [QueueTrigger("roll-result-items")] RollContext res,
            ILogger log)
        {
            log.LogInformation($"HandleResultAsync processed: {RealDiceConverter.Serialize(res)}");

            var originalActivity = RealDiceConverter.Deserialize<Activity>(res.MetaData["RequestActivity"]);
            originalActivity.Value = RealDiceConverter.Serialize(res);
            var responseActivity = new Activity("event");
            responseActivity.Value = originalActivity;
            responseActivity.Name = "RollResult";
            responseActivity.From = new ChannelAccount("RealDiceFunctionsRollResult", "RealDiceFunctions");

            var directLineSecret = Environment.GetEnvironmentVariable("DirectLineSecret");
            using (DirectLineClient client = new DirectLineClient(directLineSecret))
            {
                var conversation = await client.Conversations.StartConversationAsync();
                await client.Conversations.PostActivityAsync(conversation.ConversationId, responseActivity);
            }
        }
    }
}
