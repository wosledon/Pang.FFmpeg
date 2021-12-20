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

        public AudioEncoder(int sampleRate,
            int channels,
            int bitRate = 64000,
            AVSampleFormat inputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S32P,
            AVSampleFormat outputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLTP,
            AudioCodec audioCodec = AudioCodec.Aac)
        {
            SampleRate = sampleRate;
            Channels = channels;
            BitRate = bitRate;
            InputSampleFormat = inputSampleFormat;
            OutputSampleFormat = outputSampleFormat;
            AudioCodec = audioCodec;

            int error;

            Codec = audioCodec switch
            {
                AudioCodec.Aac => ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC),
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

            // 设置码率, 采样格式
            CodecContext->bit_rate = BitRate;
            CodecContext->sample_fmt = OutputSampleFormat;

            // 检查采样格式是否支持
            //if (AudioEncoderHelper.CheckSampleFormat(Codec, CodecContext->sample_fmt))
            //{
            //    AudioEncoderHelper.ThrowFFmpegAllocException(@$"Encoder does not support sample format: {ffmpeg.av_get_sample_fmt_name(CodecContext->sample_fmt)}");
            //}

            // 设置采样率, 通道布局, 通道数
            CodecContext->sample_rate = SampleRate;
            CodecContext->channel_layout = AudioEncoderHelper.SelectChannelLayout(Codec);
            CodecContext->channels = ffmpeg.av_get_channel_layout_nb_channels(CodecContext->channel_layout);

            ffmpeg.avcodec_open2(CodecContext, Codec, null)
                .ThrowExceptionIfError(@"Could not open Codec.");

            FrameSize = CodecContext->frame_size * 2;
            MaxOutPut = FrameSize;

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

            ffmpeg.av_frame_get_buffer(Frame, 0)
                .ThrowExceptionIfError(@"Could not allocate audio data buffers.");
        }

        public byte[]? Encode(byte[] inputBuffer)
        {
            FrameCache.AddRange(inputBuffer);

            if (FrameCache.Count < FrameSize)
            {
                return null;
            }

            //var outputCache = new List<byte[]>();
            var outputCache = new List<byte>();

            ffmpeg.av_frame_make_writable(Frame);

            //Frame->data[0] = inputBuffer.ToArrayPointer();
            ffmpeg.avcodec_fill_audio_frame(Frame, CodecContext->channels, OutputSampleFormat,
                inputBuffer.ToArrayPointer(), inputBuffer.Length, 0);

            int ret;

            ret = ffmpeg.avcodec_send_frame(CodecContext, Frame)
                .ThrowExceptionIfError(@"Error sending the frame to the encoder.");

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_packet(CodecContext, Packet);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    break;
                else if (ret < 0)
                {
                    Console.Error.WriteLine($"Error encoding audio frame");
                    Environment.Exit(1);
                }

                //outputCache.Add(new ReadOnlySpan<byte>(Packet->data, Packet->size).ToArray());
                outputCache.AddRange(new ReadOnlySpan<byte>(Packet->data, Packet->size).ToArray());
                FrameCache = FrameCache.Skip(Packet->size).ToList();
                ffmpeg.av_packet_unref(Packet);
            }

            //return outputCache;
            return outputCache.ToArray();
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