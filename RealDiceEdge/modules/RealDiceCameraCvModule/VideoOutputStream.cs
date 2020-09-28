using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Timers;

namespace RealDiceCameraCvModule
{
    public class VideoOutputStream : IDisposable
    {
        private VideoWriter videoWriter;
        private Timer frameTimer;
        private ConcurrentQueue<Mat> frameQueue;
        private Mat lastFrame;
        public string FileName { get; private set; }

        public VideoOutputStream(string fileName, FourCC fourcc, double fps, Size frameSize, bool isColor = true)
        {
            FileName = fileName;
            frameQueue = new ConcurrentQueue<Mat>();
            videoWriter = new VideoWriter(fileName, fourcc, fps, frameSize, isColor);
            frameTimer = new Timer(1.0 / fps);
            frameTimer.Elapsed += WriteFrame;
        }

        private void WriteFrame(object sender, ElapsedEventArgs e)
        {
            Mat image = Read();
            if (image != null)
            {
                lastFrame = image;
                videoWriter.Write(image);
            }
            else if (lastFrame != null)
            {
                videoWriter.Write(lastFrame);
            }
        }

        public void Dispose()
        {
            if (videoWriter != null && !videoWriter.IsDisposed)
            {
                videoWriter.Dispose();
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

        public void Close()
        {
            videoWriter.Release();
        }

        public void Write(Mat image)
        {
            frameQueue.Enqueue(image);
            while (frameQueue.Count > 2)
            {
                Read();
            }
        }

        private Mat Read()
        {
            Mat image;
            frameQueue.TryDequeue(out image);

            // 成否に関らず返す。
            return image;
        }
    }
}
