using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Timers;

namespace RealDiceCameraCvModule
{
    public class VideoInputStream : IDisposable
    {
        private VideoCapture videoCapture;
        private Timer frameTimer;
        private ConcurrentQueue<Mat> frameQueue;
        private int fps;
        private Stopwatch stopwatch;

        public VideoInputStream(int videoIndex)
        {
            fps = 15;
            stopwatch = new Stopwatch();
            stopwatch.Start();
            frameQueue = new ConcurrentQueue<Mat>();
            videoCapture = new VideoCapture(videoIndex);
            frameTimer = new Timer(1000.0 / fps);
            frameTimer.Elapsed += ReadFrame;
        }

        private void ReadFrame(object sender, ElapsedEventArgs e)
        {
            //WriteLog("ReadFrame");
            Mat image = new Mat();
            if (!videoCapture.Read(image))
            {
                WriteLog("loop Read false");
                return;
            }

            frameQueue.Enqueue(image);
            // 最新のフレームだけを保持する
            while (frameQueue.Count > 2)
            {
                var disposeImage = Read();
                if (disposeImage != null) { disposeImage.Dispose(); }
            }
        }

        public void Dispose()
        {
            try
            {
                if (videoCapture != null && !videoCapture.IsDisposed)
                {
                    videoCapture.Release();
                    videoCapture.Dispose();
                }
                frameTimer.Elapsed -= ReadFrame;
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
            }
        }

        public void Start()
        {
            frameTimer.Start();
        }

        public void Stop()
        {
            frameTimer.Stop();
        }

        public Mat Read()
        {
            Mat image;
            frameQueue.TryDequeue(out image);

            // 成否に関らず返す。
            return image;
        }

        static void WriteLog(string message)
        {
            Console.WriteLine(
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff") + "]"
                + " " + message);
        }
    }
}
