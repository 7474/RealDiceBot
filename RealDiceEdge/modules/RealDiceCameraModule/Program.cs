using Swan;

namespace RealDiceCameraModule
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
    using RealDiceCommon;
    using Unosquare.RaspberryIO;
    using Unosquare.RaspberryIO.Camera;

    class Program
    {
        static HttpRouter http;
        static readonly string videoDir = "/var/realdice/video";
        static readonly string photoDir = "/var/realdice/photo";

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
            http.Register("/caption", SetCaption);
            http.Register("/video/start", StartRecording);
            http.Register("/video/end", EndRecording);
            http.Register("/photo", TakePhoto);
            http.Start();

            await Task.CompletedTask;
        }

        //�L���v�V�����ݒ�
        static Task SetCaption(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();

            return Task.CompletedTask;
        }

        //�^��J�n
        static Task StartRecording(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // XXX raspistill �̓r�f�I�X�g���[���J���Ă��鎞�Ɏg���Ȃ��̂ł܂����Ȃ�
            //var videoSetting = GetVideoSetting();
            //Console.WriteLine($"videoSetting: {videoSetting.CreateProcessArguments()}");
            //Pi.Camera.OpenVideoStream(videoSetting, OnVideoFrame, OnVideoComplete);

            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();

            return Task.CompletedTask;
        }

        static void OnVideoFrame(byte[] bytes)
        {
            Console.WriteLine($"OnVideoFrame: Frame bytes length = {bytes.Length}");
        }

        static void OnVideoComplete()
        {
            Console.WriteLine("OnVideoComplete");
        }

        //�^��I��
        static Task EndRecording(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            //// XXX �J���Ă��Ȃ��������Ƃ��A�����Ȃ��������Ƃ�
            //Pi.Camera.CloseVideoStream();

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.OutputStream.WriteString(JsonConvert.SerializeObject(
                new
                {
                    VideoFileName = "",
                }
            ));
            response.Close();

            return Task.CompletedTask;
        }

        //�Î~��擾
        static async Task TakePhoto(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            var photoFileName = GetTimestampString() + ".jpg";
            var filePath = Path.Combine(photoDir, photoFileName);
            var photoSetting = GetPhotoSetting();
            Console.WriteLine($"photoSetting: {photoSetting.CreateProcessArguments()}");
            var photoBytes = await Pi.Camera.CaptureImageAsync(photoSetting);
            File.WriteAllBytes(filePath, photoBytes);

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

        static string GetTimestampString()
        {
            return DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        }

        static CameraVideoSettings GetVideoSetting()
        {
            return new CameraVideoSettings()
            {
                CaptureTimeoutMilliseconds = 0,
                CaptureDisplayPreview = false,
                ImageFlipVertically = true,
                CaptureExposure = CameraExposureMode.Night,
                CaptureWidth = 640,
                CaptureHeight = 480,
            };
        }
        static CameraStillSettings GetPhotoSetting()
        {
            return new CameraStillSettings
            {
                CaptureWidth = 640,
                CaptureHeight = 480,
                CaptureJpegQuality = 90,
                CaptureTimeoutMilliseconds = 300
            };
        }
    }
}
