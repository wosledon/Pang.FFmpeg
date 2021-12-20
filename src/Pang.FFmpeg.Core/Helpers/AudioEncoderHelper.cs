using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Pang.FFmpeg.Core.Helpers
{
    public sealed unsafe class AudioEncoderHelper
    {
        /// <summary>
        /// 检查采样格式
        /// </summary>
        /// <param name="codec">        </param>
        /// <param name="sampleFormat"> </param>
        /// <returns> </returns>
        public static bool CheckSampleFormat(AVCodec* codec, AVSampleFormat sampleFormat)
        {
            AVSampleFormat* p = codec->sample_fmts;

            while (*p != AVSampleFormat.AV_SAMPLE_FMT_NONE)
            {
                if (*p == sampleFormat)
                    return true;
                p++;
            }

            return false;
        }

        /// <summary>
        /// 选择采样率
        /// </summary>
        /// <param name="codec"> 编码器 </param>
        /// <returns> </returns>
        /// <remarks> 选择支持的最高采样率 </remarks>
        public static int SelectSampleRate(AVCodec* codec)
        {
            int* p;
            int bestSampleRate = 0;

            if (codec->supported_samplerates is null)
                return 44100;

            p = codec->supported_samplerates;
            while (*p != 0)
            {
                if (bestSampleRate == 0 || Math.Abs(44100 - *p) < Math.Abs(44100 - bestSampleRate))
                    bestSampleRate = *p;
                p++;
            }

            return bestSampleRate;
        }

        /// <summary>
        /// 选择通道布局
        /// </summary>
        /// <param name="codec"> </param>
        /// <returns> </returns>
        /// <remarks> 选择最高支持的通道数 </remarks>
        public static ulong SelectChannelLayout(AVCodec* codec)
        {
            ulong* p;
            ulong bestChLayout = 0;
            int bestNbChannels = 0;

            if (codec->channel_layouts is null)
            {
                return ffmpeg.AV_CH_LAYOUT_STEREO;
            }

            p = codec->channel_layouts;
            while (*p != 0)
            {
                int nbChannels = ffmpeg.av_get_channel_layout_nb_channels(*p);

                if (nbChannels > bestNbChannels)
                {
                    bestChLayout = *p;
                    bestNbChannels = nbChannels;
                }
                p++;
            }

            return bestChLayout;
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

        /// <summary>
        /// 抛出异常
        /// </summary>
        /// <param name="msg"> </param>
        public static void ThrowFFmpegAllocException(string msg)
        {
            throw new InvalidOperationException($"[Pang.FFmpeg] - {msg}");
        }
    }
}