using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.Extensions;
using Pang.FFmpeg.Core.Helpers;

namespace Pang.FFmpeg.Core.Audio
{
    public sealed unsafe class AudioConverter : IDisposable
    {
        public int InChannelLayout { get; }
        public int InSampleRate { get; }
        public AVSampleFormat InSampleFormat { get; }

        public int SourceSamplesCount { get; }

        private readonly AVCodecContext* _codecContext;

        // 转码器
        private SwrContext* SwrContext;

        // 声道
        private long OutCHLayout { get; }

        public AudioConverter(AVCodecContext* codecContext,
            int inChannelLayout,
            int inSampleRate,
            AVSampleFormat inSampleFormat)
        {
            InChannelLayout = inChannelLayout;
            InSampleRate = inSampleRate;
            InSampleFormat = inSampleFormat;
            _codecContext = codecContext;

            SourceSamplesCount = _codecContext->frame_size;
            // 初始化转码器
            SwrContext = ffmpeg.swr_alloc();

            //SwrContext = ffmpeg.swr_alloc_set_opts(SwrContext,
            //    _codecContext->channels, _codecContext->sample_fmt,
            //    _codecContext->sample_rate,
            //    inChannelLayout, inputSampleFormat,
            //    inSampleRate, 0, null);

            ffmpeg.av_opt_set_int(SwrContext, "in_channel_layout", inChannelLayout, 0);
            ffmpeg.av_opt_set_int(SwrContext, "in_sample_rate", inSampleRate, 0);
            ffmpeg.av_opt_set_sample_fmt(SwrContext, "in_sample_fmt", inSampleFormat, 0);

            ffmpeg.av_opt_set_int(SwrContext, "out_channel_layout", (long)_codecContext->channel_layout, 0);
            ffmpeg.av_opt_set_int(SwrContext, "out_sample_rate", _codecContext->sample_rate, 0);
            ffmpeg.av_opt_set_sample_fmt(SwrContext, "out_sample_fmt", _codecContext->sample_fmt, 0);

            ffmpeg.swr_init(SwrContext)
                .ThrowExceptionIfError(@"Failed to initialize the swr context.");
        }

        public byte[] Convert(byte[] input)
        {
            var data = new List<byte>();

            byte** sourceData = null;
            byte** destinationData = null;

            #region 丢弃

            int ret;

            int sourceLinesize;
            int sourceChannelsCount = ffmpeg.av_get_channel_layout_nb_channels((ulong)InChannelLayout);
            ret = ffmpeg.av_samples_alloc_array_and_samples(&sourceData, &sourceLinesize, sourceChannelsCount,
                    SourceSamplesCount, InSampleFormat, 0)
                .ThrowExceptionIfError(@"Could not allocate source samples");

            int destinationSampleCount =
                (int)ffmpeg.av_rescale_rnd(SourceSamplesCount, _codecContext->sample_rate,
                    InSampleRate, AVRounding.AV_ROUND_UP);
            int maxDestinationSampleCount = destinationSampleCount;

            int destinationLinesize;
            int destinationChannelsCount = ffmpeg.av_get_channel_layout_nb_channels(_codecContext->channel_layout);
            ret = ffmpeg.av_samples_alloc_array_and_samples(&destinationData, &destinationLinesize,
                    destinationChannelsCount,
                    destinationSampleCount, _codecContext->sample_fmt, 0)
                .ThrowExceptionIfError(@"Could not allocate destination samples.");

            /* Generate synthetic audio */
            FillSamples((double*)sourceData[0], SourceSamplesCount, sourceChannelsCount, InSampleRate, input);
            //FillSamples((double*)sourceData[0], SourceSamplesCount, sourceChannelsCount, InSampleRate, input);
            /* Compute destination number of samples */
            destinationSampleCount = (int)ffmpeg.av_rescale_rnd(ffmpeg.swr_get_delay(SwrContext, _codecContext->sample_rate) +
                                                                SourceSamplesCount, _codecContext->sample_rate, InSampleRate, AVRounding.AV_ROUND_UP);
            if (destinationSampleCount > maxDestinationSampleCount)
            {
                ffmpeg.av_freep(&destinationData[0]);
                ret = ffmpeg.av_samples_alloc(destinationData, &destinationLinesize, destinationChannelsCount,
                        destinationSampleCount, _codecContext->sample_fmt, 1)
                    .ThrowExceptionIfError(@"Sample allocate failed.");

                maxDestinationSampleCount = destinationSampleCount;
            }

            /* Convert to destination format */
            ret = ffmpeg.swr_convert(SwrContext,
                    destinationData, destinationSampleCount, sourceData, SourceSamplesCount)
                .ThrowExceptionIfError(@"Error while converting.");

            int destinationBufferSize = ffmpeg.av_samples_get_buffer_size(&destinationLinesize,
                    destinationChannelsCount, ret, _codecContext->sample_fmt, 1)
                .ThrowExceptionIfError(@"Could not get sample buffer size");

            data.AddRange(new IntPtr(destinationData![0]).ToArray(destinationBufferSize));

            #endregion 丢弃

            ffmpeg.av_free(sourceData);
            ffmpeg.av_free(destinationData);

            return data.ToArray();
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

        private static void FillSamples(double* dst, int samplesCount, int channelsCount, int sampleRate, byte[] buffer)
        {
            int i, j;
            double* dstp = dst;

            /* generate sin tone with 440Hz frequency and duplicated channels */
            for (i = 0; i < buffer.Length; i++)
            {
                dstp[i] = buffer[i];

                dstp += 1;
            }
        }

        public void Dispose()
        {
            ffmpeg.av_free(SwrContext);
        }
    }
}