using OpenCvSharp;
using System;
using System.Collections.Concurrent;
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
            frameTimer = new Timer(1000.0 / fps);
            frameTimer.Elapsed += WriteFrame;
        }

        private void WriteFrame(object sender, ElapsedEventArgs e)
        {
            WriteLog("WriteFrame");
            Mat image = Read();
            if (image != null)
            {
                WriteLog("  image");
                if (lastFrame != null) { lastFrame.Dispose(); }
                lastFrame = image;
                videoWriter.Write(image);
            }
            else if (lastFrame != null)
            {
                WriteLog("  lastFrame");
                // XXX どうもフレーム毎に位置情報が付加されている感じがする
                videoWriter.Write(lastFrame);
            }
        }

        public void Dispose()
        {
            if (videoWriter != null && !videoWriter.IsDisposed)
            {
                videoWriter.Dispose();
            }
            frameTimer.Elapsed -= WriteFrame;
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
            frameQueue.Enqueue(image.Clone());
            while (frameQueue.Count > 2)
            {
                var disposeImage = Read();
                if (disposeImage != null) { disposeImage.Dispose(); }
            }
        }

        private Mat Read()
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