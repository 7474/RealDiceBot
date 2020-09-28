using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace RealDiceCameraCvModule
{
    public class VideoInputStream : IDisposable
    {
        private BackgroundWorker worker;
        private VideoCapture videoCapture;
        private ConcurrentQueue<Mat> frameQueue;

        public VideoInputStream(string path)
        {
            frameQueue = new ConcurrentQueue<Mat>();
            // XXX Test video selecto
            videoCapture = new VideoCapture(0);
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
            while (true)
            {
                Console.WriteLine(DateTime.Now + " " + "loop Start");
                if (worker.CancellationPending)
                {
                    return;
                }

                Mat image = new Mat();
                if (!videoCapture.Read(image))
                {
                    Console.WriteLine(DateTime.Now + " " + "loop Read false");
                    Stop();
                    return;
                }
                Console.WriteLine(DateTime.Now + " " + "loop Read true");

                frameQueue.Enqueue(image);
                // 最新のフレームだけを保持する
                while (frameQueue.Count > 1)
                {
                    Console.WriteLine(DateTime.Now + " " + "loop Count > 1");
                    Read();
                }
                Console.WriteLine(DateTime.Now + " " + "loop End");
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
    }
}
