using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.Helpers;

namespace Pang.FFmpeg.Core.AudioEncoder
{
    public sealed unsafe class AudioFileEncoder
    {
        public AudioFileEncoder()
        {
        }

        /// <summary>
        /// 解码音频数据, 并追加到文件中
        /// </summary>
        /// <param name="pCodecContext"> </param>
        /// <param name="pFrame">        </param>
        /// <param name="pPacket">       </param>
        /// <param name="fs">            </param>
        /// <remarks> About fs: need choose FileMode.Append </remarks>
        public void Encode(AVCodecContext* pCodecContext,
            AVFrame* pFrame, AVPacket* pPacket, FileStream fs)
        {
            int error;
            if ((error = ffmpeg.avcodec_send_frame(pCodecContext, pFrame)) < 0)
            {
                Console.WriteLine(@"Error sending the frame to encoder");
            }

            while (error >= 0)
            {
                error = ffmpeg.avcodec_receive_packet(pCodecContext, pPacket);
                if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN) || error == ffmpeg.AVERROR_EOF)
                    return;
                else if (error < 0)
                {
                    Console.WriteLine(@"Error encoding audio frame.");
                    error.ThrowExceptionIfError();
                }

                // 追加数据
                var writeBuffer = new byte[pPacket->size];
                Marshal.Copy((IntPtr)pPacket->data, writeBuffer, 0, pPacket->size * 1024);
                fs.Write(writeBuffer, 0, pPacket->size);
            }
        }

        /// <summary>
        /// 解码一帧音频数据, 并追加到文件
        /// </summary>
        /// <param name="pCodecContext"> </param>
        /// <param name="pFrame">        </param>
        /// <param name="pPacket">       </param>
        /// <param name="fs">            </param>
        public void EncodeOneFrame(AVCodecContext* pCodecContext,
            AVFrame* pFrame, AVPacket* pPacket, FileStream fs)
        {
            try
            {
                int error;
                do
                {
                    ffmpeg.avcodec_send_frame(pCodecContext, pFrame)
                        .ThrowExceptionIfError();

                    ffmpeg.av_packet_unref(pPacket);

                    error = ffmpeg.avcodec_receive_packet(pCodecContext, pPacket);
                } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

                error.ThrowExceptionIfError();

                // save to file
                var writeBuffer = new byte[pPacket->size];
                Marshal.Copy((IntPtr)pPacket->data, writeBuffer, 0, pPacket->size * 1024);
                fs.Write(writeBuffer, 0, pPacket->size);
            }
            finally
            {
            }
        }
    }
}