using Azure.Storage.Queues;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using RealDiceCommon.Models.Roll;
using RealDiceCommon.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace RealDiceBot.Services
{
    public class RollService : IRollService
    {
        private readonly QueueClient requestQueueClient;

        public RollService(IConfiguration config)
        {
            var queueName = config["RollRequestQueueName"];
            var connectionString = config["QueueStorageConnection"];

            requestQueueClient = new QueueClient(connectionString, queueName);
        }

        public async Task RequestAsync(IActivity referenceActivity, RollRequest rollRequest)
        {
            var context = new RollContext
            {
                Id = Guid.NewGuid().ToString(),
                MetaData = new Dictionary<string, string>
                {
                    ["RequestActivity"] = RealDiceConverter.Serialize(referenceActivity),
                },
                Requests = new RollRequest[] { rollRequest },
            };
            var message = Convert.ToBase64String(Encoding.UTF8.GetBytes(RealDiceConverter.Serialize(context)));
            await requestQueueClient.SendMessageAsync(message);
        }
    }
}
