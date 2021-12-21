using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.Enums;
using Pang.FFmpeg.Core.Extensions;
using Pang.FFmpeg.Core.Helpers;

namespace Pang.FFmpeg.Core.Audio
{
    public sealed unsafe class AudioEncoder : IDisposable
    {
        private AVCodec* Codec;
        private AVCodecContext* CodecContext;
        private AVFormatContext* FormatContext;
        private AVFrame* Frame;
        private AVPacket* Packet;

        /// <summary>
        /// 帧大小
        /// </summary>
        public int FrameSize { get; private set; }

        /// <summary>
        /// 最大输出
        /// </summary>
        public int MaxOutPut { get; private set; }

        public long Pts { get; private set; }

        /// <summary>
        /// 原始数据缓存器
        /// </summary>
        public List<byte> FrameCache { get; private set; } = new List<byte>();

        public AVSampleFormat InputSampleFormat { get; private set; }
        public AVSampleFormat OutputSampleFormat { get; private set; }

        /// <summary>
        /// 码率
        /// </summary>
        public int BitRate { get; private set; }

        /// <summary>
        /// 通道数
        /// </summary>
        public int Channels { get; private set; }

        /// <summary>
        /// 采样率
        /// </summary>
        public int SampleRate { get; private set; }

        /// <summary>
        /// 编码器
        /// </summary>
        public AudioCodec AudioCodec { get; private set; }

        public bool NeedAdts { get; }

        public AudioEncoder(int sampleRate,
            int channels,
            int bitRate = 8000,
            AVSampleFormat inputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16,
            AVSampleFormat outputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLTP,
            AudioCodec audioCodec = AudioCodec.Aac, bool needAdts = false)
        {
            SampleRate = sampleRate;
            Channels = channels;
            BitRate = bitRate;
            InputSampleFormat = inputSampleFormat;
            OutputSampleFormat = outputSampleFormat;
            AudioCodec = audioCodec;
            NeedAdts = needAdts;

            int error;

            Codec = audioCodec switch
            {
                AudioCodec.Aac => ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC),
                //AudioCodec.Aac => ffmpeg.avcodec_find_decoder_by_name("libfaac"),
                AudioCodec.Mp3 => ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MP3),
                _ => throw new ArgumentOutOfRangeException(nameof(audioCodec), audioCodec, null)
            };
            if (Codec is null)
            {
                AudioEncoderHelper.ThrowFFmpegAllocException(@"Codec not found.");
            }

            CodecContext = ffmpeg.avcodec_alloc_context3(Codec);
            if (CodecContext is null)
            {
                AudioEncoderHelper.ThrowFFmpegAllocException(@"Could not allocate audio codec context.");
            }

            CodecContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
            CodecContext->profile = ffmpeg.FF_PROFILE_AAC_LOW;

            // 设置码率, 采样格式
            CodecContext->bit_rate = BitRate;
            CodecContext->sample_fmt = OutputSampleFormat;

            //检查采样格式是否支持
            if (!AudioEncoderHelper.CheckSampleFormat(Codec, CodecContext->sample_fmt))
            {
                AudioEncoderHelper.ThrowFFmpegAllocException(@$"Encoder does not support sample format: {ffmpeg.av_get_sample_fmt_name(CodecContext->sample_fmt)}");
            }

            // 设置采样率, 通道布局, 通道数
            CodecContext->sample_rate = SampleRate;
            CodecContext->channel_layout = AudioEncoderHelper.SelectChannelLayout(Codec);
            CodecContext->channels = ffmpeg.av_get_channel_layout_nb_channels(CodecContext->channel_layout);

            ffmpeg.avcodec_open2(CodecContext, Codec, null)
                .ThrowExceptionIfError(@"Could not open Codec.");

            Packet = ffmpeg.av_packet_alloc();
            if (Packet is null)
            {
                AudioEncoderHelper.ThrowFFmpegAllocException(@"Could not allocate the packet.");
            }

            Frame = ffmpeg.av_frame_alloc();
            if (Frame is null)
            {
                AudioEncoderHelper.ThrowFFmpegAllocException(@"Could not allocate the frame.");
            }

            Frame->nb_samples = CodecContext->frame_size;
            Frame->format = (int)OutputSampleFormat;
            Frame->channel_layout = CodecContext->channel_layout;
            Frame->channels = ffmpeg.av_get_channel_layout_nb_channels(Frame->channel_layout);

            ffmpeg.av_frame_get_buffer(Frame, 0)
                .ThrowExceptionIfError(@"Could not allocate audio data buffers.");

            FrameSize = CodecContext->frame_size;
            MaxOutPut = FrameSize;
        }

        public byte[] Encode(byte[] inputBuffer)
        {
            FrameCache.AddRange(inputBuffer);

            if (FrameCache.Count < FrameSize)
            {
                return Array.Empty<byte>();
            }

            //var outputCache = new List<byte[]>();
            var outputCache = new List<byte>();

            byte* pcmBuffer = FrameCache.Take(FrameSize).ToArray().ToArrayPointer();

            byte* pcmTempBuffer = (byte*)Marshal.AllocHGlobal(FrameSize);

            // 确保Frame可写
            ffmpeg.av_frame_make_writable(Frame).ThrowExceptionIfError(@"Frame unable read or write.");

            if (AVSampleFormat.AV_SAMPLE_FMT_S16 == (AVSampleFormat)Frame->format)
            {
                ffmpeg.avcodec_fill_audio_frame(Frame, CodecContext->channels, OutputSampleFormat,
                    pcmBuffer, FrameSize, 0);
            }
            else
            {
                //F32leConvertToFltp((float*)pcmBuffer, (float*)pcmTempBuffer, Frame->nb_samples, Channels);
                ffmpeg.avcodec_fill_audio_frame(Frame, CodecContext->channels, OutputSampleFormat,
                    pcmBuffer, FrameSize, 0);
            }

            Pts += Frame->nb_samples;
            Frame->pts = Pts;

            #region 测试指针与数组互转是否成功

            //var temp = new byte[FrameSize];
            //Marshal.Copy(new IntPtr(pcmBuffer), temp, 0, FrameSize);

            //var temp2 = new byte[FrameSize];
            //Marshal.Copy(new IntPtr(pcmBuffer), temp2, 0, FrameSize);

            #endregion 测试指针与数组互转是否成功

            int ret;

            ret = ffmpeg.avcodec_send_frame(CodecContext, Frame)
                .ThrowExceptionIfError(@"Error sending the frame to the encoder.");

            var packetSize = 0;

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_packet(CodecContext, Packet);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    continue;
                else if (ret < 0)
                {
                    Console.Error.WriteLine($"Error encoding audio frame");
                    break;
                }

                //if (NeedAdts)
                //{
                //    byte* aacHeader = (byte*)Marshal.AllocHGlobal(7);
                //    GetAdtsHeader(CodecContext, aacHeader, Packet->size);
                //    var header = new byte[7];
                //    Marshal.Copy(new IntPtr(aacHeader), header, 0, 7);
                //    outputCache.InsertRange(0, header);

                //    Marshal.FreeHGlobal(new IntPtr(aacHeader));
                //}
                packetSize += Packet->size;

                //outputCache.Add(new ReadOnlySpan<byte>(Packet->data, Packet->size).ToArray());
                outputCache.AddRange(new ReadOnlySpan<byte>(Packet->data, Packet->size).ToArray());
                FrameCache = FrameCache.Skip(FrameSize).ToList();
                ffmpeg.av_packet_unref(Packet);
            }

            if (NeedAdts && outputCache.Any())
            {
                byte* aacHeader = (byte*)Marshal.AllocHGlobal(7);
                GetAdtsHeader(CodecContext, aacHeader, packetSize);
                var header = new byte[7];
                Marshal.Copy(new IntPtr(aacHeader), header, 0, 7);
                outputCache.InsertRange(0, header);

                Marshal.FreeHGlobal(new IntPtr(aacHeader));
            }

            Marshal.FreeHGlobal(new IntPtr(pcmBuffer));
            Marshal.FreeHGlobal(new IntPtr(pcmTempBuffer));

            //return outputCache;
            return outputCache.ToArray();
        }

        private static void GetAdtsHeader(AVCodecContext* codecContext, byte* adtsHeader, int aacLength)
        {
            int freqIndex = 0;

            switch (codecContext->sample_rate)
            {
                case 96000: freqIndex = 0; break;
                case 88200: freqIndex = 1; break;
                case 64000: freqIndex = 2; break;
                case 48000: freqIndex = 3; break;
                case 44100: freqIndex = 4; break;
                case 32000: freqIndex = 5; break;
                case 24000: freqIndex = 6; break;
                case 22050: freqIndex = 7; break;
                case 16000: freqIndex = 8; break;
                case 12000: freqIndex = 9; break;
                case 11025: freqIndex = 10; break;
                case 8000: freqIndex = 11; break;
                case 7350: freqIndex = 12; break;
                default: freqIndex = 4; break;
            }

            int channelConfig = codecContext->channels;

            int frameLength = aacLength + 7;

            adtsHeader[0] = 0xFF;
            adtsHeader[1] = 0xF1;
            adtsHeader[2] = (byte)(((codecContext->profile) << 6) + (freqIndex << 2) + (channelConfig >> 2));
            adtsHeader[3] = (byte)(((channelConfig & 3) << 6) + (frameLength >> 11));
            adtsHeader[4] = (byte)((frameLength & 0x7FF) >> 3);
            adtsHeader[5] = (byte)(((frameLength & 7) << 5) + 0x1F);
            adtsHeader[6] = 0xFC;
        }

        private static void F32leConvertToFltp(float* f32le, float* fltp, int nb_samples, int channels)
        {
            float* fltp_left = fltp;
            float* fltp_right = fltp + nb_samples;

            for (int i = 0; i < nb_samples; i++)
            {
                fltp_left[i] = f32le[i * 2];
                fltp_right[i] = f32le[i * 2 + 1];
            }
        }

        public void Dispose()
        {
            ffmpeg.av_free(Codec);
            ffmpeg.av_free(CodecContext);
            ffmpeg.av_free(FormatContext);
            ffmpeg.av_free(Frame);
            ffmpeg.av_free(Packet);
        }
    }
}