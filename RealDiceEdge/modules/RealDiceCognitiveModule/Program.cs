namespace RealDiceCognitiveModule
{
    using System;
    using System.IO;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Newtonsoft.Json;

    class Program
    {
        static Random randomizor = new Random();
        static HttpRouter http;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
            http.Stop();
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

            http = new HttpRouter(new string[] { "http://+:80/" });
            http.Register("/cognitive", OnRequest);
            http.Start();

            await Task.CompletedTask;
        }

        static Task OnRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.StatusCode = 200;
            response.ContentType = "application/json";
            response.OutputStream.WriteString(JsonConvert.SerializeObject(
                new
                {
                    Result = randomizor.Next(1, 6),
                    Score = randomizor.NextDouble(),
                    Status = randomizor.NextDouble() > 0.1 ? "ok" : "unknown",
                }
            ));
            response.Close();

            return Task.CompletedTask;
        }
    }
}
