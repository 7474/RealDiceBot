namespace RealDiceEdgeModule
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Storage;
    using Microsoft.Azure.Storage.Blob;
    using Newtonsoft.Json;
    using RealDiceEdgeModule.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.Loader;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static Random randomizer = new Random();
        // XXX userContextで取りまわした方が良い？
        static CloudBlobContainer cloudBlobContainer;
        static HttpClient cameraClient;
        static HttpClient cognitiveClient;
        static ConcurrentQueue<RollRequestContext> rollRequestContexts;
        static BackgroundWorker rollRequestWorker;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
            rollRequestWorker.CancelAsync();
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

            var connectionString = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");
            var containerName = Environment.GetEnvironmentVariable("RESULT_CONTAINER_NAME");
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var cloudBlobClient = storageAccount.CreateCloudBlobClient();
            cloudBlobContainer = cloudBlobClient.GetContainerReference(containerName);

            cameraClient = new HttpClient()
            {
                BaseAddress = new Uri("http://RealDiceCameraModule/"),
            };
            cognitiveClient = new HttpClient()
            {
                BaseAddress = new Uri("http://RealDiceCognitivePyModule/"),
            };
            rollRequestContexts = new ConcurrentQueue<RollRequestContext>();

            // Register Module Method.
            await ioTHubModuleClient.SetMethodHandlerAsync("Roll", Roll, ioTHubModuleClient);
            rollRequestWorker = new BackgroundWorker();
            rollRequestWorker.DoWork += RollRequestWorkerDoWork;
            rollRequestWorker.RunWorkerAsync();
        }

        private static void RollRequestWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            while (!e.Cancel)
            {
                try
                {
                    while (!rollRequestContexts.IsEmpty)
                    {
                        RollRequestContext context;
                        if (rollRequestContexts.TryDequeue(out context))
                        {
                            RollInternal(context.Request, context.UserContext).Wait();
                        }
                    }
                    Task.Delay(100).Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        static async Task<MethodResponse> Roll(MethodRequest methodRequest, object userContext)
        {
            try
            {
                Console.WriteLine($"Exec Roll {methodRequest.DataAsJson}");
                // 完了を待たずに受付した旨を返す
                rollRequestContexts.Enqueue(new RollRequestContext
                {
                    Request = methodRequest,
                    UserContext = userContext as ModuleClient,
                });

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

        static async Task RollInternal(MethodRequest methodRequest, ModuleClient moduleClient)
        {
            try
            {
                Console.WriteLine($"Start RollInternal {methodRequest.DataAsJson}");

                var req = JsonConvert.DeserializeObject<RollRequest>(methodRequest.DataAsJson);

                //キャプション設定
                var reqCaptionResult = await cameraClient.PostAsync("caption", new StringContent(""));
                Console.WriteLine($"reqCaptionResult: {reqCaptionResult.StatusCode}");

                //録画開始
                var recStartResult = await cameraClient.PostAsync("/video/start", new StringContent(""));
                Console.WriteLine($"recStartResult: {recStartResult.StatusCode}");

                //ダイスオン
                // TODO 実装する

                // 回す時間分だけ待つ。
                await Task.Delay(randomizer.Next(300, 800));

                //ダイスオフ
                // TODO 実装する

                //静止画取得
                var takePhotoResult = await cameraClient.PostAsync("/photo", new StringContent(""));
                Console.WriteLine($"takePhotoResult: {takePhotoResult.StatusCode}");
                var takePhotoResultStr = await takePhotoResult.Content.ReadAsStringAsync();
                Console.WriteLine($"    {takePhotoResultStr}");
                dynamic takePhotoResultObj = JsonConvert.DeserializeObject(takePhotoResultStr);
                string photoFileName = takePhotoResultObj.PhotoFileName;

                //静止画認識
                int rollResult = randomizer.Next(1, 6);
                double rollResultScore = 0.0;
                string resultStatus = "Error";
                var blob = cloudBlobContainer.GetBlockBlobReference(photoFileName);
                var cognitiveRequestContent = new MultipartFormDataContent();
                var imageDataContent = new StreamContent(await blob.OpenReadAsync());
                imageDataContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = "imageData",
                    FileName = Path.GetFileName(photoFileName),
                };
                cognitiveRequestContent.Add(imageDataContent, "imageData", Path.GetFileName(photoFileName));
                var cognitiveResult = await cognitiveClient.PostAsync("/image", cognitiveRequestContent);
                Console.WriteLine($"cognitiveResult: {cognitiveResult.StatusCode}");
                var cognitiveResultString = await cognitiveResult.Content.ReadAsStringAsync();
                Console.WriteLine(cognitiveResultString);
                if (cognitiveResult.StatusCode == HttpStatusCode.OK)
                {
                    // TODO 型を付ける
                    dynamic rollResultJson = JsonConvert.DeserializeObject(cognitiveResultString);
                    IList<dynamic> predictions = rollResultJson.predictions.ToObject<List<dynamic>>();
                    if (predictions.Any())
                    {
                        var prediction = predictions[0];
                        string labelName = prediction.tagName;
                        int labelResult;
                        if (int.TryParse(new string(labelName.TakeLast(1).ToArray()), out labelResult))
                        {
                            rollResult = labelResult;
                            rollResultScore = prediction.probability;
                            resultStatus = "Success";
                        }
                    }
                    else
                    {
                        resultStatus = "UnRecognized";
                    }
                }

                //キャプション設定
                var resCaptionResult = await cameraClient.PostAsync("caption", new StringContent(""));
                Console.WriteLine($"resCaptionResult: {resCaptionResult.StatusCode}");

                //録画終了
                var recEndResult = await cameraClient.PostAsync("/video/end", new StringContent(""));
                Console.WriteLine($"recEndResult: {recEndResult.StatusCode}");

                //ダイスロール応答メッセージ
                var res = new RollResponse
                {
                    Id = req.Id,
                    Status = resultStatus,
                    Result = rollResult,
                    Score = rollResultScore,
                    PhotoName = photoFileName,
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
    }

    public class RollRequestContext
    {
        public MethodRequest Request { get; set; }
        public ModuleClient UserContext { get; set; }
    }
}
