using Microsoft.Azure.Devices;
using Microsoft.Azure.WebJobs;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RealDiceCommon.Models.Edge;
using RealDiceCommon.Models.Roll;
using RealDiceCommon.Utils;
using RealDiceFunctions.Modes;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RealDiceFunctions
{
    public static class RealDiceRoll
    {
        private static Random randomizer = new Random();

        [FunctionName("HandleRequest")]
        public static async Task HandleRequestAsync(
            [QueueTrigger("roll-request-items")] RollContext req,
            [Queue("roll-result-items")] IAsyncCollector<RollContext> rollQueue,
            [Table("rollcontext")] IAsyncCollector<RollContextTableRow> rollTable,
            ILogger log)
        {
            log.LogInformation($"HandleRequestAsync processed: {RealDiceConverter.Serialize(req)}");

            try
            {
                var iotHubServiceClient = ServiceClient.CreateFromConnectionString(
                    Environment.GetEnvironmentVariable("IoTHubConnectionString"));

                var resRow = JsonConvert.DeserializeObject<RollContextTableRow>(JsonConvert.SerializeObject(req));
                resRow.PartitionKey = "Bot";
                resRow.RowKey = resRow.Id;
                await rollTable.AddAsync(resRow);

                // https://github.com/Azure-Samples/azure-iot-samples-csharp/blob/master/iot-hub/Quickstarts/back-end-application/BackEndApplication.cs
                var edgeMethodInvocation = new CloudToDeviceMethod(
                    "Roll", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)
                );
                edgeMethodInvocation.SetPayloadJson(JsonConvert.SerializeObject(new EdgeRollRequest
                {
                    Id = req.Id,
                    // XXX メッセージを受け取る。
                    Message = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }));
                var testres = await iotHubServiceClient.GetServiceStatisticsAsync();
                log.LogInformation($"   GetServiceStatisticsAsync: {RealDiceConverter.Serialize(testres)}");
                // XXX 複数Edgeを扱えるようになればDeviceIdは動的にバランスさせたい。
                var deviceId = Environment.GetEnvironmentVariable("IoTHubRealDiceEdgeDeviceId");
                var moduleId = Environment.GetEnvironmentVariable("IoTHubRealDiceEdgeModuleId");

                var edgeResponse = await iotHubServiceClient.InvokeDeviceMethodAsync(
                    deviceId, moduleId, edgeMethodInvocation
                );
                log.LogInformation($"   edgeResponse: {RealDiceConverter.Serialize(edgeResponse)}");

                if (edgeResponse.Status < 300)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"   Edge Request Failed: {ex.Message}");
            }

            // Edgeへのリクエストが失敗したらFunctionsで応答するためのキューに結果を入れる。
            var res = req;
            res.Results = req.Requests.Select(x => new RollResult
            {
                Request = x,
                Status = "Offline",
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

            await SendResult(res);
        }

        [FunctionName("HandleEdgeResult")]
        public static async Task HandleEdgeResultAsync(
            [EventHubTrigger("%IoTHubEventHubsName%", Connection = "IoTHubEventHubsConnectionString")]
            EdgeRollResponse res,
            [Table("rollcontext", "Bot", "{id}")] RollContextTableRow rollContext,
            ILogger log)
        {
            log.LogInformation($"HandleResultAsync processed: {RealDiceConverter.Serialize(res)}");
            log.LogInformation($"   RollContextTableRow: {RealDiceConverter.Serialize(rollContext)}");

            if (rollContext == null)
            {
                // どうすることもできない
                return;
            }

            var containerUrl = Environment.GetEnvironmentVariable("ResultContainerBaseUri");
            var photoUrl = new Uri(new Uri(containerUrl), res.PhotoName);
            var videoName = new Uri(new Uri(containerUrl), res.VideoName);
            rollContext.Results = rollContext.Requests.Select(x => new RollResult
            {
                Request = x,
                Status = res.Status,
                Score = res.Score,
                Results = new uint[] { (uint)res.Result },
                PhotoUrl = string.IsNullOrEmpty(res.PhotoName) ? null : photoUrl.ToString(),
                VideoUrl = string.IsNullOrEmpty(res.VideoName) ? null : videoName.ToString(),
            }).ToList();

            await SendResult(rollContext);
        }

        private static async Task SendResult(RollContext res)
        {
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
