namespace RealDiceEdgeModule
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Newtonsoft.Json;
    using RealDiceEdgeModule.Models;
    using System;
    using System.Runtime.Loader;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static int counter;
        static Random randomizer = new Random();

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            // Register Module Method.
            await ioTHubModuleClient.SetMethodHandlerAsync("Roll", Roll, ioTHubModuleClient);
        }

        static async Task<MethodResponse> Roll(MethodRequest methodRequest, object userContext)
        {
            try
            {
                Console.WriteLine($"Exec Roll {methodRequest.DataAsJson}");
                // 完了を待たずに受付した旨を返す
                _ = RollInternal(methodRequest, userContext);

                return await Task.FromResult(
                    new MethodResponse(202)
                );
            }
            catch (Exception ex)
            {
                // XXX 例外のハンドリング具合が分からん。
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }

        static async Task RollInternal(MethodRequest methodRequest, object userContext)
        {
            try
            {
                Console.WriteLine($"Start RollInternal {methodRequest.DataAsJson}");

                var moduleClient = userContext as ModuleClient;
                var req = JsonConvert.DeserializeObject<RollRequest>(methodRequest.DataAsJson);

                // TODO 実装する
                var rollResult = randomizer.Next(1, 6);
                var res = new RollResponse
                {
                    Id = req.Id,
                    Result = rollResult,
                    PhotoName = req.Id + ".jpg",
                    VideoName = req.Id + ".mp4",
                };
                await moduleClient.SendEventAsync(
                    "RollResult",
                    new Message(Encoding.UTF8.GetBytes(
                        JsonConvert.SerializeObject(res)
                    )
                ));
                Console.WriteLine($"End RollInternal {methodRequest.DataAsJson}");
            }
            catch (Exception ex)
            {
                // XXX 例外のハンドリング具合が分からん。
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub.
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                using (var pipeMessage = new Message(messageBytes))
                {
                    foreach (var prop in message.Properties)
                    {
                        pipeMessage.Properties.Add(prop.Key, prop.Value);
                    }
                    await moduleClient.SendEventAsync("output1", pipeMessage);

                    Console.WriteLine("Received message sent");
                }
            }
            return MessageResponse.Completed;
        }
    }
}
