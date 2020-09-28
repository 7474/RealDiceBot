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
        static string videoExtension = ".avi";

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
            WriteLog("IoT Hub module client initialized.");

            var connectionString = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");
            var containerName = Environment.GetEnvironmentVariable("RESULT_CONTAINER_NAME");
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var cloudBlobClient = storageAccount.CreateCloudBlobClient();
            cloudBlobContainer = cloudBlobClient.GetContainerReference(containerName);
            await cloudBlobContainer.CreateIfNotExistsAsync();

            // XXX 環境変数に
            videoInputStream = new VideoInputStream(0);
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
            http.Register("/photo_with_caption", TakePhotoWithCaption);
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

            WriteLog($"SetCaption {caption}");
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
                WriteLog($"UpdateFrame {frameSize.Width}x{frameSize.Height}");
                if (frame.Size() != GetOutputSize(frame.Size()))
                {
                    frame = frame.Resize(GetOutputSize(frame.Size()));
                }
                WriteLog($"UpdateFrame {frame.Width}x{frame.Height}");
                if (!string.IsNullOrEmpty(caption))
                {
                    //PutCaption(frame, caption, captionFont);
                }
                lastFrame = frame;
                videoOutputStream.Write(frame.Clone());
            }
        }

        static void PutCaption(Mat image, string caption, System.Drawing.Font font)
        {
            WriteLog($"PutCaption {caption}");
            var size = image.Size();
            using (var bitmap = image.ToBitmap())
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                var captionSize = g.MeasureString(caption, font);
                g.DrawString(caption, font, System.Drawing.Brushes.Azure,
                    size.Width / 2 - captionSize.Width / 2,
                    size.Height - captionSize.Height - 4);
                bitmap.ToMat(image);
            }
        }

        //録画開始
        static Task StartRecording(HttpListenerContext context)
        {
            WriteLog($"StartRecording");
            if (videoOutputStream != null)
            {
                throw new ApplicationException("Already recording");
            }
            var request = context.Request;
            var response = context.Response;

            var videoName = GetTimestampString() + videoExtension;
            var videoFilePath = Path.Combine(videoDir, videoName);
            WriteLog($"    {videoName}");
            Directory.CreateDirectory(Path.GetDirectoryName(videoFilePath));
            var frame = lastOriginalFrame != null
                ? lastOriginalFrame
                : videoInputStream.Read();

            var frameSize = frame.Size();
            WriteLog($"    {frameSize.Width}x{frameSize.Height}");
            videoOutputStream = new VideoOutputStream(videoFilePath, FourCC.XVID, 15, GetOutputSize(frameSize));
            videoOutputStream.Start();
            frameTimer.Start();

            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();

            return Task.CompletedTask;
        }

        //録画終了
        static async Task EndRecording(HttpListenerContext context)
        {
            WriteLog($"EndRecording");
            var request = context.Request;
            var response = context.Response;
            var videoFileName = "";

            if (videoOutputStream != null)
            {
                var filePath = videoOutputStream.FileName;
                WriteLog($"    {filePath}");

                frameTimer.Stop();
                videoOutputStream.Stop();
                videoOutputStream.Close();
                videoOutputStream = null;

                videoFileName = GetDateString() + "/" + Guid.NewGuid().ToString() + videoExtension;
                WriteLog($"    {videoFileName}");
                var blob = cloudBlobContainer.GetBlockBlobReference(videoFileName);
                blob.Properties.ContentType = "video/mp4";
                await blob.UploadFromFileAsync(filePath);
                WriteLog($"    UploadFromFileAsync end");
                // XXX ファイル消すと死んでいる疑惑。
                //File.Delete(filePath);
                //WriteLog($"    File.Delete end");
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
            await TakePhotoInternal(response, false);
        }

        static async Task TakePhotoWithCaption(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            await TakePhotoInternal(response, true);
        }

        private static async Task TakePhotoInternal(HttpListenerResponse response, bool withCaption)
        {
            var photoFileName = GetDateString() + "/" + Guid.NewGuid().ToString() + ".jpg";

            var frame = withCaption ? lastFrame.Clone() : lastOriginalFrame.Clone();
            if (frame.Size() != GetOutputSize(frame.Size()))
            {
                frame = frame.Resize(GetOutputSize(frame.Size()));
            }
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
            // XXX 640x480 なので入力ママでいいかな
            //return new Size(frameSize.Width / 4, frameSize.Height / 4);
            return frameSize;
        }

        static void WriteLog(string message)
        {
            Console.WriteLine(
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff") + "]"
                + " " + message);
        }
    }
}
