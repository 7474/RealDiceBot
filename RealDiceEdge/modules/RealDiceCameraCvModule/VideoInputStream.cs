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
            videoCapture = new VideoCapture(path);
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
        }

        private void Update(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            while (true)
            {
                if (worker.CancellationPending)
                {
                    return;
                }

                Mat image = new Mat();
                if (!videoCapture.Read(image))
                {
                    Stop();
                    return;
                }

                frameQueue.Enqueue(image);
                // 最新のフレームだけを保持する
                while (frameQueue.Count > 2)
                {
                    Read();
                }
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
