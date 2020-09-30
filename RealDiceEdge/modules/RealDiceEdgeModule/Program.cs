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
    using System.Device.Gpio;
    using System.Diagnostics;
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
        // XXX GPIOを別モジュールに切り離す
        const int GPIO_MOTAR_STBY = 17;
        const int GPIO_MOTAR_AIN1 = 27;
        const int GPIO_MOTAR_AIN2 = 22;
        static GpioController gpioController;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
            gpioController.ClosePin(GPIO_MOTAR_STBY);
            gpioController.ClosePin(GPIO_MOTAR_AIN1);
            gpioController.ClosePin(GPIO_MOTAR_AIN2);
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
            WriteLog("IoT Hub module client initialized.");

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

            gpioController = new GpioController();
            gpioController.OpenPin(GPIO_MOTAR_STBY, PinMode.Output);
            gpioController.OpenPin(GPIO_MOTAR_AIN1, PinMode.Output);
            gpioController.OpenPin(GPIO_MOTAR_AIN2, PinMode.Output);
            gpioController.Write(GPIO_MOTAR_STBY, PinValue.Low);
            gpioController.Write(GPIO_MOTAR_AIN1, PinValue.Low);
            gpioController.Write(GPIO_MOTAR_AIN2, PinValue.Low);
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
                    WriteLog(ex.Message);
                }
            }
        }

        static async Task<MethodResponse> Roll(MethodRequest methodRequest, object userContext)
        {
            try
            {
                WriteLog($"Exec Roll {methodRequest.DataAsJson}");
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
                WriteLog(ex.Message);
                WriteLog(ex.StackTrace);
                throw;
            }
        }

        static async Task<CloudBlockBlob> ConvertVideoToGif(CloudBlockBlob videoBlob)
        {
            var gifFileName = GetDateString() + "/" + Guid.NewGuid().ToString() + ".gif";
            var gifBlob = cloudBlobContainer.GetBlockBlobReference(gifFileName);
            var fps = 15;

            var process = new Process()
            {
                // https://qiita.com/yusuga/items/ba7b5c2cac3f2928f040
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i - -f gif -" +
                        $" -filter_complex \"[0:v] fps = {fps},scale = 320:-1,split[a][b];[a] palettegen[p];[b][p] paletteuse=dither=none\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            WriteLog("ConvertVideoToGif Start");
            process.Start();
            _ = (await videoBlob.OpenReadAsync())
                .CopyToAsync(process.StandardInput.BaseStream);
            await gifBlob.UploadFromStreamAsync(process.StandardOutput.BaseStream);
            process.WaitForExit();
            WriteLog("ConvertVideoToGif End");

            return gifBlob;
        }

        static async Task RollInternal(MethodRequest methodRequest, ModuleClient moduleClient)
        {
            try
            {
                WriteLog($"Start RollInternal {methodRequest.DataAsJson}");

                var req = JsonConvert.DeserializeObject<RollRequest>(methodRequest.DataAsJson);
                string reqMessage = req.Message;

                //キャプション設定
                WriteLog($"reqCaption");
                var reqCaptionResult = await cameraClient.PostAsync("caption",
                    new StringContent(JsonConvert.SerializeObject(new
                    {
                        Caption = reqMessage,
                    })));
                WriteLog($"reqCaptionResult: {reqCaptionResult.StatusCode}");

                //録画開始
                WriteLog($"recStart");
                var recStartResult = await cameraClient.PostAsync("/video/start", new StringContent(""));
                WriteLog($"recStartResult: {recStartResult.StatusCode}");

                //ダイスオン
                WriteLog($"gpio start");
                gpioController.Write(GPIO_MOTAR_STBY, PinValue.High);
                gpioController.Write(GPIO_MOTAR_AIN1, PinValue.Low);
                gpioController.Write(GPIO_MOTAR_AIN2, PinValue.High);

                // 回す時間分だけ待つ。
                await Task.Delay(randomizer.Next(1500, 2000));

                //ダイスオフ
                gpioController.Write(GPIO_MOTAR_AIN1, PinValue.High);
                gpioController.Write(GPIO_MOTAR_AIN2, PinValue.High);
                await Task.Delay(500);
                gpioController.Write(GPIO_MOTAR_STBY, PinValue.Low);
                gpioController.Write(GPIO_MOTAR_AIN1, PinValue.Low);
                gpioController.Write(GPIO_MOTAR_AIN2, PinValue.Low);
                WriteLog($"gpio end");
                // 止まる見込みまで待つ。
                // XXX ビデオで停止を認識できるとカッコいい。
                // 動体がなければいいので比較的平易にできるはず。
                await Task.Delay(1500);

                //静止画取得
                WriteLog($"takePhoto");
                var takePhotoResult = await cameraClient.PostAsync("/photo", new StringContent(""));
                WriteLog($"takePhotoResult: {takePhotoResult.StatusCode}");
                var takePhotoResultStr = await takePhotoResult.Content.ReadAsStringAsync();
                WriteLog($"    {takePhotoResultStr}");
                dynamic takePhotoResultObj = JsonConvert.DeserializeObject(takePhotoResultStr);
                string photoFileName = takePhotoResultObj.PhotoFileName;

                WriteLog($"waitCaption");
                var waitCaptionResult = await cameraClient.PostAsync("caption",
                    new StringContent(JsonConvert.SerializeObject(new
                    {
                        Caption = "Recognizing...",
                    })));
                WriteLog($"waitCaptionResult: {waitCaptionResult.StatusCode}");

                //静止画認識
                WriteLog($"cognitive");
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
                WriteLog($"cognitiveResult: {cognitiveResult.StatusCode}");
                var cognitiveResultString = await cognitiveResult.Content.ReadAsStringAsync();
                WriteLog(cognitiveResultString);
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
                WriteLog($"resCaption");
                var resCaptionResult = await cameraClient.PostAsync("caption",
                    new StringContent(JsonConvert.SerializeObject(new
                    {
                        Caption = $"1d6 = {rollResult} ! (Score: {rollResultScore})",
                    })));
                WriteLog($"resCaptionResult: {resCaptionResult.StatusCode}");

                // キャプションが入るまで待つ
                await Task.Delay(150);

                try
                {
                    // 返却用の結果静止画
                    WriteLog($"takePhotoWithCaption");
                    takePhotoResult = await cameraClient.PostAsync("/photo_with_caption", new StringContent(""));
                    WriteLog($"takePhotoWithCaptionResult: {takePhotoResult.StatusCode}");
                    takePhotoResultStr = await takePhotoResult.Content.ReadAsStringAsync();
                    WriteLog($"    {takePhotoResultStr}");
                    takePhotoResultObj = JsonConvert.DeserializeObject(takePhotoResultStr);
                    photoFileName = takePhotoResultObj.PhotoFileName;
                }
                catch { }

                await Task.Delay(2000);

                //録画終了
                WriteLog($"recEnd");
                var recEndResult = await cameraClient.PostAsync("/video/end", new StringContent(""));
                var recEndResultStr = await recEndResult.Content.ReadAsStringAsync();
                WriteLog($"    {recEndResultStr}");
                dynamic recEndResultObj = JsonConvert.DeserializeObject(recEndResultStr);
                string videoFileName = recEndResultObj.VideoFileName;
                WriteLog($"recEndResult: {recEndResult.StatusCode}");

                // GIF化
                try
                {
                    var gifBlob = await ConvertVideoToGif(cloudBlobContainer.GetBlockBlobReference(videoFileName));
                    videoFileName = gifBlob.Name;
                }
                catch (Exception ex)
                {
                    WriteLog(ex.Message);
                }

                await cameraClient.PostAsync("caption",
                    new StringContent(JsonConvert.SerializeObject(new
                    {
                        Caption = "",
                    })));

                //ダイスロール応答メッセージ
                WriteLog($"resMessage");
                var res = new RollResponse
                {
                    Id = req.Id,
                    Status = resultStatus,
                    Result = rollResult,
                    Score = rollResultScore,
                    PhotoName = photoFileName,
                    VideoName = videoFileName,
                };
                await moduleClient.SendEventAsync(
                    "RollResult",
                    new Message(Encoding.UTF8.GetBytes(
                        JsonConvert.SerializeObject(res)
                    )
                ));
                WriteLog($"resMessageEnd");

                WriteLog($"End RollInternal {methodRequest.DataAsJson}");
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
                WriteLog(ex.StackTrace);
                throw;
            }
            finally
            {
                try
                {
                    gpioController.Write(GPIO_MOTAR_STBY, PinValue.Low);
                    gpioController.Write(GPIO_MOTAR_AIN1, PinValue.Low);
                    gpioController.Write(GPIO_MOTAR_AIN2, PinValue.Low);
                }
                catch { }
            }
        }

        static string GetDateString()
        {
            return DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        }

        static void WriteLog(string message)
        {
            Console.WriteLine(
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff") + "]"
                + " " + message);
        }
    }

    public class RollRequestContext
    {
        public MethodRequest Request { get; set; }
        public ModuleClient UserContext { get; set; }
    }
}
