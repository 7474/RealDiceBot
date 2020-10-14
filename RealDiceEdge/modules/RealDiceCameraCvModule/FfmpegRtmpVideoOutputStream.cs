using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;

namespace RealDiceCameraCvModule
{
    public class FfmpegRtmpVideoOutputStream : AbstractVideoOutputStream
    {
        private Process liveStreamProcess;
        private Size frameSize;

        public FfmpegRtmpVideoOutputStream(string rtmpUri, double fps, Size frameSize)
            : base(fps)
        {
            this.frameSize = frameSize;
            liveStreamProcess = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    // XXX size
                    Arguments =
                        //$"-re -y " +
                        "-re " +
                        "-ar 44100 -ac 2 -acodec pcm_s16le -f s16le -ac 2 -i /dev/zero " +
                        $"-f rawvideo -pixel_format bgr24 -video_size 640x480 -framerate {fps} -i - " +
                        // XXX build ffmpeg
                        //$"-c:v h264_omx " +
                        $"-c:v libx264 -pix_fmt yuv420p -profile:v baseline -level:v 4.1 -s {frameSize.Width}x{frameSize.Height} " +
                        "-minrate 256k -maxrate 512k -bufsize 512k " +
                        "-acodec aac -ab 128k -g 2 " +
                        "-strict experimental " +
                        // https://stackoverflow.com/questions/45220915/ffmpeg-streaming-error-failed-to-update-header-with-correct-filesize-and-durati
                        $"-f flv -flvflags no_duration_filesize {rtmpUri} ",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                }
            };
        }

        public override void Start()
        {
            liveStreamProcess.Start();
            base.Start();
        }

        public override void Stop()
        {
            base.Stop();
            liveStreamProcess.Kill();
        }

        bool logged = false;
        protected override void WriteFrame(object sender, ElapsedEventArgs e)
        {
            isBusy = true;
            try
            {
                if (liveStreamProcess.HasExited)
                {
                    liveStreamProcess.Start();
                }
                Mat image = Read();
                if (image != null)
                {
                    if (!logged)
                    {
                        WriteLog("FfmpegRtmpVideoOutputStream#WriteFrame:");
                        WriteLog($" image.Total(): {image.Total()}");
                        WriteLog($" image.Channels(): {image.Channels()}");
                        WriteLog($" image.Depth(): {image.Depth()}");
                        WriteLog($" image.Size(): {image.Size().Width}x{image.Size().Height}");
                        WriteLog($" image.ElemSize(): {image.ElemSize()}");
                        logged = true;
                    }
                    var raw = new byte[image.ElemSize() * image.Size().Width * image.Size().Height];
                    Marshal.Copy(image.Data, raw, 0, raw.Length);
                    if (raw.Length > 0)
                    {
                        liveStreamProcess.StandardInput.BaseStream.Write(raw, 0, raw.Length);
                    }
                    image.Dispose();
                }
                else
                {
                    WriteLog("FfmpegRtmpVideoOutputStream#WriteFrame: Skip");
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
                liveStreamProcess.Kill();
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
            }
        }
    }
}
