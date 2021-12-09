using System;
using System.Runtime.InteropServices;
using System.Transactions;
using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.Helpers;

namespace Pang.FFmpeg.Core.AudioEncoder
{
    public sealed unsafe class AACEncoder : IDisposable
    {
        /// <summary>
        /// 采样率
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// 码率
        /// </summary>
        public int BitRate { get; }

        /// <summary>
        /// 通道数
        /// </summary>
        public int Channels { get; }

        /// <summary>
        /// 帧大小
        /// </summary>
        public int FrameSize { get; set; }

        private int FrameBytes { get; set; }

        /// <summary>
        /// 输入的样本格式
        /// </summary>
        public AVSampleFormat InputSampleFormat { get; }

        /// <summary>
        /// 输出的样本格式
        /// </summary>
        public AVSampleFormat OutputSampleFormat { get; } = AVSampleFormat.AV_SAMPLE_FMT_FLTP;

        /// <summary>
        /// 编码器上下文
        /// </summary>
        private AVCodecContext* _pCodecContext;

        /// <summary>
        /// 编码器
        /// </summary>
        private AVCodec* _pCodec;

        /// <summary>
        /// 转码器上下文
        /// </summary>
        private SwrContext* _pSwrContext;

        private AVFrame* _pFrame;
        private AVPacket* _pPacket;

        public AACEncoder(int sampleRate = 8000, int bitRate = 64000, int channels = 1, AVSampleFormat sampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S32)
        {
            SampleRate = sampleRate;
            BitRate = bitRate;
            Channels = channels;
            InputSampleFormat = sampleFormat;

            int Error;

            // 找到编码器
            _pCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
            if (_pCodec is null)
            {
                throw new InvalidOperationException(@"Codec not found");
            }

            // 初始化编码器上下文
            _pCodecContext = ffmpeg.avcodec_alloc_context3(_pCodec);
            if (_pCodecContext is null)
            {
                throw new InvalidOperationException(@"Could not allocate audio codec context.");
            }

            // 设置码率
            _pCodecContext->bit_rate = BitRate;
            // 设置采样格式
            _pCodecContext->sample_fmt = OutputSampleFormat;
            // 检查采样格式是否支持
            if (!AudioEncoder.CheckSampleFormat(_pCodec, _pCodecContext->sample_fmt))
            {
                throw new InvalidOperationException($"Encoder does not support sample format: {ffmpeg.av_get_sample_fmt_name(_pCodecContext->sample_fmt)}");
            }

            // 设置采样率, 通道布局, 通道数
            _pCodecContext->sample_rate = sampleRate;
            _pCodecContext->channel_layout = AudioEncoder.SelectChannelLayout(_pCodec);
            _pCodecContext->channels = ffmpeg.av_get_channel_layout_nb_channels(_pCodecContext->channel_layout);

            Error = ffmpeg.avcodec_open2(_pCodecContext, _pCodec, null)
                .ThrowExceptionIfError(@"Could not open codec.");

            // 初始化转码器的内存
            _pSwrContext = ffmpeg.swr_alloc();

            _pSwrContext = ffmpeg.swr_alloc_set_opts(_pSwrContext,
                (long)_pCodecContext->channel_layout, OutputSampleFormat,
                _pCodecContext->sample_rate,
                ffmpeg.AV_CH_LAYOUT_MONO, InputSampleFormat,
                SampleRate, 0, null);

            ffmpeg.swr_init(_pSwrContext)
                .ThrowExceptionIfError(@"Failed to initialize the swr context.");

            // 分配Frame空间
            _pFrame = ffmpeg.av_frame_alloc();
            if (_pFrame is null)
            {
                throw new InvalidOperationException(@"Could not allocate audio frame.");
            }

            // 分配Packet空间
            _pPacket = ffmpeg.av_packet_alloc();
            if (_pPacket is null)
            {
                throw new InvalidOperationException(@"Could not allocate audio packet.");
            }

            FrameSize = _pCodecContext->frame_size;

            _pFrame->nb_samples = _pCodecContext->frame_size;
            _pFrame->format = (int)_pCodecContext->sample_fmt;
            _pFrame->channel_layout = _pCodecContext->channel_layout;
            _pFrame->channels = ffmpeg.av_get_channel_layout_nb_channels(_pFrame->channel_layout);

            Console.WriteLine($"Frame nb_samples: {_pFrame->nb_samples}");
            Console.WriteLine($"Frame sample_fmt: {_pFrame->format}");
            Console.WriteLine($"Frame channel_layout: {_pFrame->channel_layout}");

            ffmpeg.av_frame_get_buffer(_pFrame, 0)
                .ThrowExceptionIfError(@"Could not allocate audio data buffers.");

            FrameBytes = (ffmpeg.av_get_bytes_per_sample((AVSampleFormat)_pFrame->format)
                                 * _pFrame->channels * _pFrame->nb_samples);
        }

        public void Encode(byte[] input)
        {
            IntPtr pcmBuffer = Marshal.AllocHGlobal(FrameBytes);
            if (pcmBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Pcm buffer malloc failed.");
            }

            Marshal.Copy(pcmBuffer, input, 0, FrameBytes);

            IntPtr pcmTempBuffer = Marshal.AllocHGlobal(FrameBytes);
            if (pcmTempBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(@"Pcm temp buffer malloc failed.");
            }

            ffmpeg.av_frame_make_writable(_pFrame)
                .ThrowExceptionIfError("av_frame_make_writable failed");

            if (AVSampleFormat.AV_SAMPLE_FMT_S16 == (AVSampleFormat)_pFrame->format)
            {
                ffmpeg.av_samples_fill_arrays(IntPtr.)
            }
            else
            {
            }
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}