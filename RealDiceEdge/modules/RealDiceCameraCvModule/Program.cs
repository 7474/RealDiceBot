namespace RealDiceCameraCvModule
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Storage;
    using Microsoft.Azure.Storage.Blob;
    using Newtonsoft.Json;
    using OpenCvSharp;
    using OpenCvSharp.Extensions;
    using RealDiceCommon;
    using System;
    using System.IO;
    using System.Net;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Timers;

    class Program
    {
        static HttpRouter http;
        static readonly string videoDir = "/var/realdice/video";
        static double fps = 15;
        static CloudBlobContainer cloudBlobContainer;
        static VideoInputStream videoInputStream;
        static VideoOutputStream videoOutputStream;
        static System.Timers.Timer frameTimer;
        static Mat lastFrame;
        static Mat lastOriginalFrame;
        static string caption;
        static System.Drawing.Font captionFont;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
            http.Stop();
            videoInputStream.Stop();
            videoInputStream.Dispose();
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
            await cloudBlobContainer.CreateIfNotExistsAsync();

            // TODO 環境変数ないし論理的な固定パスに
            videoInputStream = new VideoInputStream("/dev/video0");
            videoInputStream.Start();

            caption = "";
            captionFont = new System.Drawing.Font("noto", 10.5f);

            frameTimer = new System.Timers.Timer(1.0 / fps);
            frameTimer.Elapsed += UpdateFrame;

            http = new HttpRouter(new string[] { "http://+:80/" });
            http.Register("/caption", SetCaption);
            http.Register("/video/start", StartRecording);
            http.Register("/video/end", EndRecording);
            http.Register("/photo", TakePhoto);
            http.Start();

            await Task.CompletedTask;
        }

        //キャプション設定
        static Task SetCaption(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var requestJson = request.InputStream.ReadString();
            var requestObj = JsonConvert.DeserializeObject<dynamic>(requestJson);

            caption = requestObj.Caption;

            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();

            return Task.CompletedTask;
        }

        static void UpdateFrame(object sender, ElapsedEventArgs e)
        {
            var frame = videoInputStream.Read();
            if (frame != null)
            {
                lastOriginalFrame = frame.Clone();

                var frameSize = frame.Size();
                Console.WriteLine($"{frameSize.Width}x{frameSize.Height}");
                frame.Resize(GetOutputSize(frameSize));
                if (!string.IsNullOrEmpty(caption))
                {
                    PutCaption(frame, caption, captionFont);
                }
                lastFrame = frame;
            }
        }

        static void PutCaption(Mat image, string caption, System.Drawing.Font font)
        {
            var size = image.Size();
            using (var bitmap = image.ToBitmap())
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                // XXX 見やすく描画する
                var captionSize = g.MeasureString(caption, font);
                g.DrawString(caption, font, System.Drawing.Brushes.Azure,
                    size.Width - captionSize.Width / 2,
                    size.Height - captionSize.Height - 4);
                bitmap.ToMat().CopyTo(image);
            }
        }

        //録画開始
        static Task StartRecording(HttpListenerContext context)
        {
            if (videoOutputStream != null)
            {
                throw new ApplicationException("Already recording");
            }
            var request = context.Request;
            var response = context.Response;

            var videoName = GetTimestampString() + ".mp4";
            var videoFilePath = Path.Combine(videoDir, videoName);
            var frame = lastOriginalFrame != null
                ? lastOriginalFrame
                : videoInputStream.Read();

            var frameSize = frame.Size();
            Console.WriteLine($"{frameSize.Width}x{frameSize.Height}");
            videoOutputStream = new VideoOutputStream(videoFilePath, FourCC.MPG4, 15, GetOutputSize(frameSize));
            frameTimer.Start();

            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();

            return Task.CompletedTask;
        }

        //録画終了
        static async Task EndRecording(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var videoFileName = "";

            if (videoOutputStream != null)
            {
                var filePath = videoOutputStream.FileName;

                frameTimer.Stop();
                videoOutputStream.Stop();
                videoOutputStream.Close();
                videoOutputStream = null;

                videoFileName = GetDateString() + "/" + Guid.NewGuid().ToString() + ".jpg";
                var blob = cloudBlobContainer.GetBlockBlobReference(videoFileName);
                blob.Properties.ContentType = "video/mp4";
                await blob.UploadFromFileAsync(filePath);
                File.Delete(filePath);
                response.StatusCode = (int)HttpStatusCode.OK;
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
            }

            response.ContentType = "application/json";
            response.OutputStream.WriteString(JsonConvert.SerializeObject(
                new
                {
                    VideoFileName = videoFileName,
                }
            ));
            response.Close();
        }

        //静止画取得
        static async Task TakePhoto(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            var photoFileName = GetDateString() + "/" + Guid.NewGuid().ToString() + ".jpg";

            var frame = lastOriginalFrame.Clone();
            frame.Resize(GetOutputSize(frame.Size()));
            var photoStream = new MemoryStream();
            frame.WriteToStream(photoStream, ".jpg");
            photoStream.Position = 0;

            var blob = cloudBlobContainer.GetBlockBlobReference(photoFileName);
            blob.Properties.ContentType = "image/jpg";
            await blob.UploadFromStreamAsync(photoStream);

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.OutputStream.WriteString(JsonConvert.SerializeObject(
                new
                {
                    PhotoFileName = photoFileName,
                }
            ));
            response.Close();
        }

        static string GetDateString()
        {
            return DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        }
        static string GetTimestampString()
        {
            return DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        }
        static Size GetOutputSize(Size frameSize)
        {
            return new Size(frameSize.Width / 4, frameSize.Height / 4);
        }
    }
}
