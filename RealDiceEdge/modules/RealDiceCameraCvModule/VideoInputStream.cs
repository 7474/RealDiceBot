using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RealDiceCameraCvModule
{
    public class VideoInputStream : IDisposable
    {
        private BackgroundWorker worker;
        private VideoCapture videoCapture;
        private ConcurrentQueue<Mat> frameQueue;
        private int desireFps;
        private Stopwatch stopwatch;

        public VideoInputStream(int videoIndex)
        {
            desireFps = 15;
            stopwatch = new Stopwatch();
            stopwatch.Start();
            frameQueue = new ConcurrentQueue<Mat>();
            videoCapture = new VideoCapture(videoIndex);
            worker = new BackgroundWorker();
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += Update;
        }

        public void Dispose()
        {
            if (videoCapture != null && !videoCapture.IsDisposed)
            {
                videoCapture.Release();
                videoCapture.Dispose();
            }
            worker.DoWork -= Update;
        }

        private void Update(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            var loopStart = stopwatch.ElapsedMilliseconds;
            while (true)
            {
                //WriteLog("loop Start");
                if (worker.CancellationPending)
                {
                    return;
                }

                Mat image = new Mat();
                if (!videoCapture.Read(image))
                {
                    WriteLog("loop Read false");
                    Stop();
                    return;
                }
                //WriteLog("loop Read true");

                frameQueue.Enqueue(image);
                // 最新のフレームだけを保持する
                while (frameQueue.Count > 2)
                {
                    //WriteLog("loop Count > 1");
                    Read();
                }
                var sleepMillis = 1000 / desireFps - (stopwatch.ElapsedMilliseconds - loopStart);
                loopStart = stopwatch.ElapsedMilliseconds;
                WriteLog($"loop End. Delay s {stopwatch.ElapsedMilliseconds} {sleepMillis}");
                Task.Delay((int)Math.Min(1000 / desireFps, Math.Max(0, sleepMillis))).Wait();
                WriteLog($"loop End. Delay e {stopwatch.ElapsedMilliseconds} {stopwatch.ElapsedMilliseconds - loopStart}");
            }
        }

        public void Start()
        {
            worker.RunWorkerAsync();
        }

        public void Stop()
        {
            worker.CancelAsync();
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
