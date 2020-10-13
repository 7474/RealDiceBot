namespace RealDiceCameraCvModule
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Storage;
    using Microsoft.Azure.Storage.Blob;
    using Newtonsoft.Json;
    using OpenCvSharp;
    using OpenCvSharp.Extensions;
    using RealDiceCameraCvModule.Models;
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
        static object syncOutput = new object();
        static VideoOutputStream videoOutputStream;
        static FfmpegRtmpVideoOutputStream videoLiveStream;
        static System.Timers.Timer frameTimer;
        static object syncRoot = new object();
        static Mat _lastFrame;
        static Mat lastFrame
        {
            get { lock (syncRoot) { return _lastFrame?.Clone(); } }
            set
            {
                lock (syncRoot)
                {
                    _lastFrame?.Dispose();
                    _lastFrame = value;
                }
            }
        }
        static Mat _lastOriginalFrame;
        static Mat lastOriginalFrame
        {
            get { lock (syncRoot) { return _lastOriginalFrame?.Clone(); } }
            set
            {
                lock (syncRoot)
                {
                    _lastOriginalFrame?.Dispose();
                    _lastOriginalFrame = value;
                }
            }
        }
        static CaptionRequest caption;
        static System.Drawing.Font captionFont;
        static System.Drawing.Font smallFont;
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
            frameTimer.Stop();
            videoInputStream.Stop();
            videoInputStream.Dispose();
            videoLiveStream.Stop();
            videoLiveStream.Dispose();
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

            var rtmpUri = Environment.GetEnvironmentVariable("RTMP_URI");
            // XXX Size
            videoLiveStream = new FfmpegRtmpVideoOutputStream(rtmpUri, 15, new Size(320, 240));
            videoLiveStream.Start();

            caption = new CaptionRequest { Caption = "" };
            captionFont = new System.Drawing.Font("noto", 15f);
            smallFont = new System.Drawing.Font("noto", 10f);

            frameTimer = new System.Timers.Timer(1.0 / fps);
            frameTimer.Elapsed += UpdateFrame;
            frameTimer.Start();

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
            var requestObj = JsonConvert.DeserializeObject<CaptionRequest>(requestJson);

            caption = requestObj;

            WriteLog($"SetCaption {requestJson}");
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

                if (frame.Size() != GetOutputSize(frame.Size()))
                {
                    var x = frame;
                    frame = frame.Resize(GetOutputSize(frame.Size()));
                    x.Dispose();
                }
                if (!string.IsNullOrEmpty(caption?.Caption))
                {
                    var x = frame;
                    frame = PutCaption(frame, caption, captionFont);
                    x.Dispose();
                }
                videoLiveStream.Write(frame.Clone());
                lock (syncOutput)
                {
                    if (videoOutputStream != null)
                    {
                        WriteLog($"videoOutputStream.Write");
                        videoOutputStream.Write(frame);
                    }
                }
                lastFrame = frame;
            }
        }

        static int RatioToAbs(float ratio, int max)
        {
            return (int)(ratio * max);
        }
        static Mat PutCaption(Mat image, CaptionRequest caption, System.Drawing.Font font)
        {
            WriteLog($"PutCaption {caption?.Caption}");
            var size = image.Size();
            using (var bitmap = image.ToBitmap())
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                if (caption.CognitiveResult != null && caption.CognitiveResult.predictions != null)
                {
                    foreach (var prediction in caption.CognitiveResult.predictions)
                    {
                        var rect = new System.Drawing.Rectangle(
                            RatioToAbs(prediction.boundingBox.left, size.Width),
                            RatioToAbs(prediction.boundingBox.top, size.Height),
                            RatioToAbs(prediction.boundingBox.width, size.Width),
                            RatioToAbs(prediction.boundingBox.height, size.Height)
                        );
                        g.DrawRectangle(System.Drawing.Pens.Azure, rect);
                        g.DrawString($"{prediction.tagName}({prediction.probability})", font, System.Drawing.Brushes.Azure,
                            rect.Left,
                            rect.Bottom + 2);
                    }
                }

                var captionSize = g.MeasureString(caption.Caption, font);
                g.DrawString(caption.Caption, font, System.Drawing.Brushes.Azure,
                    size.Width / 2 - captionSize.Width / 2,
                    size.Height - captionSize.Height - 4);
                // XXX なんかVideoCaputureしたMatに位置情報が入っている感じがする
                var captionedImage = image.Clone();
                bitmap.ToMat(captionedImage);
                return captionedImage;
            }
        }

        //録画開始
        static Task StartRecording(HttpListenerContext context)
        {
            WriteLog($"StartRecording");

            var request = context.Request;
            var response = context.Response;

            lock (syncOutput)
            {
                if (videoOutputStream != null)
                {
                    throw new ApplicationException("Already recording");
                }

                var videoName = GetTimestampString() + videoExtension;
                var videoFilePath = Path.Combine(videoDir, videoName);
                WriteLog($"    {videoName}");
                Directory.CreateDirectory(Path.GetDirectoryName(videoFilePath));
                var frame = _lastOriginalFrame != null
                    ? lastOriginalFrame
                    : videoInputStream.Read();

                var frameSize = frame.Size();
                WriteLog($"    {frameSize.Width}x{frameSize.Height}");
                videoOutputStream = new VideoOutputStream(videoFilePath, FourCC.XVID, 15, GetOutputSize(frameSize));
                videoOutputStream.Start();
            }

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

            var hasOutput = false;
            var filePath = "";
            var videoFileName = "";
            lock (syncOutput)
            {
                if (videoOutputStream != null)
                {
                    hasOutput = true;
                    filePath = videoOutputStream.FileName;
                    WriteLog($"    {filePath}");
                    videoOutputStream.Stop();
                    WriteLog($"    videoOutputStream.Stop");
                    videoOutputStream.Dispose();
                    WriteLog($"    videoOutputStream.Dispose");
                    videoOutputStream = null;
                }
            }

            if (hasOutput)
            {
                videoFileName = GetDateString() + "/" + Guid.NewGuid().ToString() + videoExtension;
                WriteLog($"    {videoFileName}");
                var blob = cloudBlobContainer.GetBlockBlobReference(videoFileName);
                //blob.Properties.ContentType = "video/mp4";
                blob.Properties.ContentType = "video/x-msvideo";
                await blob.UploadFromFileAsync(filePath);
                WriteLog($"    UploadFromFileAsync end");
                // XXX やっぱファイル消すとダメな感じ。await もう少し見よう。
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
            WriteLog("TakePhotoInternal");
            var photoFileName = GetDateString() + "/" + Guid.NewGuid().ToString() + ".jpg";

            var frame = withCaption ? lastFrame : lastOriginalFrame;
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
