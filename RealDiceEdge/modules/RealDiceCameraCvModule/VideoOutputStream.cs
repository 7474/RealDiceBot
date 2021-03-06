using OpenCvSharp;
using System;
using System.Timers;

namespace RealDiceCameraCvModule
{
    public class VideoOutputStream : AbstractVideoOutputStream
    {
        private VideoWriter videoWriter;
        public string FileName { get; private set; }

        public VideoOutputStream(string fileName, FourCC fourcc, double fps, Size frameSize, bool isColor = true)
            : base(fps)
        {
            FileName = fileName;
            videoWriter = new VideoWriter(fileName, fourcc, fps, frameSize, isColor);
        }

        protected override void WriteFrame(object sender, ElapsedEventArgs e)
        {
            isBusy = true;
            try
            {
                //WriteLog("WriteFrame");
                Mat image = Read();
                if (image != null)
                {
                    //WriteLog("  image");
                    //if (lastFrame != null) { lastFrame.Dispose(); }
                    //lastFrame = image;
                    videoWriter.Write(image);
                    image.Dispose();
                }
                else
                {
                    WriteLog("VideoOutputStream#WriteFrame: Skip");
                }
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
            }
            finally
            {
                isBusy = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
                if (videoWriter != null && !videoWriter.IsDisposed)
                {
                    videoWriter.Release();
                    videoWriter.Dispose();
                }
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
            }
        }
    }
}
