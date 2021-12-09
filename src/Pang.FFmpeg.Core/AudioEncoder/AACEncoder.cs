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

        private AudioEncoder AudioEncoder { get; } = new AudioEncoder();

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
            int Error;

            //IntPtr pcmBuffer = Marshal.AllocHGlobal(FrameBytes);
            //if (pcmBuffer == IntPtr.Zero)
            //{
            //    throw new InvalidOperationException("Pcm buffer malloc failed.");
            //}

            //Marshal.Copy(pcmBuffer, input, 0, FrameBytes);

            //IntPtr pcmTempBuffer = Marshal.AllocHGlobal(FrameBytes);
            //if (pcmTempBuffer == IntPtr.Zero)
            //{
            //    throw new InvalidOperationException(@"Pcm temp buffer malloc failed.");
            //}

            ffmpeg.av_frame_make_writable(_pFrame)
                .ThrowExceptionIfError("av_frame_make_writable failed");

            byte** sourceData = null;
            byte** destinationData = null;

            int sourceLineSize;
            int sourceChannelsCount = ffmpeg.av_get_channel_layout_nb_channels(ffmpeg.AV_CH_LAYOUT_MONO);
            ffmpeg.av_samples_alloc_array_and_samples(&sourceData, &sourceLineSize, sourceChannelsCount,
                _pFrame->nb_samples, InputSampleFormat, 0)
                .ThrowExceptionIfError(@"Could not allocate source samples.");

            int destinationSampleCount =
                (int)ffmpeg.av_rescale_rnd(_pFrame->nb_samples, _pFrame->sample_rate, SampleRate,
                    AVRounding.AV_ROUND_UP);
            int maxDestinationSampleCount = destinationSampleCount;

            int destinationLineSize;
            int destinationChannelsCount = ffmpeg.av_get_channel_layout_nb_channels(_pFrame->channel_layout);
            ffmpeg.av_samples_alloc_array_and_samples(&destinationData, &destinationLineSize, destinationChannelsCount,
                destinationSampleCount, OutputSampleFormat, 0)
                .ThrowExceptionIfError(@"Could not allocate destination samples");

            double toneLevel = 0;
            do
            {
                FillSamples((double*)sourceData[0], _pFrame->nb_samples, sourceChannelsCount, SampleRate, &toneLevel);

                destinationSampleCount =
                    (int)ffmpeg.av_rescale_rnd(
                        ffmpeg.swr_get_delay(_pSwrContext, SampleRate) + SampleRate, _pFrame->sample_rate,
                        SampleRate, AVRounding.AV_ROUND_UP);

                if (destinationSampleCount > maxDestinationSampleCount)
                {
                    ffmpeg.av_freep(&destinationData[0]);

                    Error = ffmpeg.av_samples_alloc(destinationData, &destinationLineSize, destinationChannelsCount,
                        destinationSampleCount, OutputSampleFormat, 1);

                    if (Error < 0)
                        break;

                    maxDestinationSampleCount = destinationSampleCount;
                }

                Error = ffmpeg.swr_convert(_pSwrContext, destinationData, destinationSampleCount, sourceData,
                    _pFrame->nb_samples)
                    .ThrowExceptionIfError(@"Error while converting");

                int destinationBufferSize = ffmpeg.av_samples_get_buffer_size(&destinationLineSize,
                    destinationChannelsCount,
                    Error, OutputSampleFormat, 1)
                    .ThrowExceptionIfError(@"Could not get sample buffer size");

                Console.WriteLine($"t: {toneLevel} in: {_pFrame->nb_samples} out: {Error}");

                AudioEncoder.Encode(pCodecContext: _pCodecContext, _pFrame, _pPacket, null);
            } while (toneLevel < 10);

            Error = getFormatFromSampleFormat(out var fmt, OutputSampleFormat)
                .ThrowExceptionIfError();
            Console.Error.Write("Resampling succeeded. Play the output file with the command:\n"
                                + $"ffplay -f {fmt} -channel_layout {_pFrame->channel_layout} -channels {destinationChannelsCount} -ar {_pFrame->sample_rate} {"filename"}\n",
                fmt, _pFrame->channel_layout, destinationChannelsCount, _pFrame->sample_rate, "filename");
        }

        private struct sample_fmt_entry
        {
            public AVSampleFormat sample_fmt;
            public string fmt_be, fmt_le;
        }

        private static int getFormatFromSampleFormat(out string fmt, AVSampleFormat sample_fmt)
        {
            var sample_fmt_entries = new[]{
                new sample_fmt_entry{ sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_U8,  fmt_be = "u8",    fmt_le = "u8"    },
                new sample_fmt_entry{ sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16, fmt_be = "s16be", fmt_le = "s16le" },
                new sample_fmt_entry{ sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S32, fmt_be = "s32be", fmt_le = "s32le" },
                new sample_fmt_entry{ sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT, fmt_be = "f32be", fmt_le = "f32le" },
                new sample_fmt_entry{ sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_DBL, fmt_be = "f64be", fmt_le = "f64le" },
            };
            fmt = null;
            for (var i = 0; i < sample_fmt_entries.Length; i++)
            {
                var entry = sample_fmt_entries[i];
                if (sample_fmt == entry.sample_fmt)
                {
                    fmt = ffmpeg.AV_HAVE_BIGENDIAN != 0 ? entry.fmt_be : entry.fmt_le;
                    return 0;
                }
            }

            Console.Error.WriteLine($"Sample format {ffmpeg.av_get_sample_fmt_name(sample_fmt)} not supported as output format");
            return ffmpeg.AVERROR(ffmpeg.EINVAL);
        }

        /**
         * Fill dst buffer with nb_samples, generated starting from t.
         */

        private static void FillSamples(double* dst, int samplesCount, int channelsCount, int sampleRate, double* toneLevel)
        {
            int i, j;
            double toneIncrement = 1.0 / sampleRate;
            double* dstp = dst;
            const double c = 2 * Math.PI * 440.0;

            /* generate sin tone with 440Hz frequency and duplicated channels */
            for (i = 0; i < samplesCount; i++)
            {
                *dstp = Math.Sin(c * *toneLevel);
                for (j = 1; j < channelsCount; j++)
                    dstp[j] = dstp[0];
                dstp += channelsCount;
                *toneLevel += toneIncrement;
            }
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}