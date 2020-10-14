using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Timers;

namespace RealDiceCameraCvModule
{
    public abstract class AbstractVideoOutputStream : IDisposable
    {
        private Timer frameTimer;
        private ConcurrentQueue<Mat> frameQueue;
        private bool disposedValue;

        protected bool isBusy { get; set; }

        public AbstractVideoOutputStream(double fps)
        {
            frameQueue = new ConcurrentQueue<Mat>();
            frameTimer = new Timer(1000.0 / fps);
            frameTimer.Elapsed += WriteFrame;
        }

        protected abstract void WriteFrame(object sender, ElapsedEventArgs e);

        public virtual void Start()
        {
            frameTimer.Start();
        }

        public virtual void Stop()
        {
            // https://docs.microsoft.com/en-us/dotnet/api/system.timers.timer.stop?view=netcore-2.1#examples
            // タイマー処理が終わるまで待つサンプルが提示されているけれど
            // 面倒くさいので簡易にやる
            frameTimer.Stop();
            while (isBusy) { Task.Delay(50).Wait(); }
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

        protected Mat Read()
        {
            Mat image;
            frameQueue.TryDequeue(out image);

            // 成否に関らず返す。
            return image;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // マネージド状態を破棄します (マネージド オブジェクト)
                    frameTimer.Dispose();
                }

                // アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected static void WriteLog(string message)
        {
            Console.WriteLine(
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff") + "]"
                + " " + message);
        }
    }
}
