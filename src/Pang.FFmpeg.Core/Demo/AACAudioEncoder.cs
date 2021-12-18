using System;
using System.Collections.Generic;
using System.IO;
using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.AudioEncoder;
using Pang.FFmpeg.Core.Helpers;

namespace PM.FFmpeg.AudioEncoderDecoders
{
    public sealed unsafe class AACAudioEncoder : IDisposable
    {
        private AVCodec* _pCodec;
        private AVCodecContext* _pCodecContext;
        private SwrContext* _pSwrContext;
        private AVFormatContext* _pFormatContext;
        private AVCodecContext* _pReceiveCodecContext;
        private AVIOContext* _pAVIOContext;

        private AVFrame* _pFrame;
        private AVFrame* _receiveFrame;
        private AVPacket* _pPacket;

        private readonly int _inputChannelLayout = ffmpeg.AV_CH_LAYOUT_STEREO;
        private readonly int _outputChannelLayout = ffmpeg.AV_CH_LAYOUT_STEREO;

        private readonly AVSampleFormat _inputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
        private readonly AVSampleFormat _outSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLTP;

        private int _inputSamplesCount = 1024;

        private readonly AudioEncoder _audioEncoder;

        private int Error;

        private IntPtr PcmPointer;

        public AACAudioEncoder(int bitRate = 64000, int sampleRate = 44100)
        {
            _audioEncoder = new AudioEncoder();

            _pCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);

            if (_pCodec is null)
                throw new InvalidOperationException("Codec not found.");

            _pCodecContext = ffmpeg.avcodec_alloc_context3(_pCodec);
            if (_pCodecContext is null)
                throw new InvalidOperationException("Could not allocate audio codec context.");

            _pCodecContext->bit_rate = bitRate;

            _pCodecContext->sample_fmt = _outSampleFormat;
            if (AudioEncoder.CheckSampleFormat(_pCodec, _pCodecContext->sample_fmt))
            {
                Console.WriteLine(@$"Encoder does not support sample format{ffmpeg.av_get_sample_fmt_name(_pCodecContext->sample_fmt)}");
                Error.ThrowExceptionIfError();
            }

            _pCodecContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
            _pCodecContext->codec = _pCodec;
            _pCodecContext->codec_id = AVCodecID.AV_CODEC_ID_AAC;
            _pCodecContext->sample_rate = AudioEncoder.SelectSampleRate(_pCodec);
            _pCodecContext->channel_layout = (ulong)AudioEncoder.SelectChannelLayout(_pCodec);
            _pCodecContext->channels = ffmpeg.av_get_channel_layout_nb_channels(_pCodecContext->channel_layout);
            //_pCodecContext->sample_rate = 8000;

            _pSwrContext = ffmpeg.swr_alloc_set_opts(_pSwrContext,
                ffmpeg.av_get_default_channel_layout(_pCodecContext->channels), _outSampleFormat,
                _pCodecContext->sample_rate,
                ffmpeg.av_get_default_channel_layout(_pCodecContext->channels), _inputSampleFormat,
                8000, 0, null);

            if (_pSwrContext is null)
            {
                Console.Error.WriteLine("Could not allocate SwrContext.");
                Error = ffmpeg.AVERROR(ffmpeg.EAGAIN).ThrowExceptionIfError();
            }

            if ((Error = ffmpeg.swr_init(_pSwrContext)) < 0)
            {
                Console.WriteLine("Swr Init Error");
                Error.ThrowExceptionIfError();
            }

            if ((Error = ffmpeg.avcodec_open2(_pCodecContext, _pCodec, null)) < 0)
            {
                Console.WriteLine("Could not open codec.");
                Error.ThrowExceptionIfError();
            }
            _pPacket = ffmpeg.av_packet_alloc();
            if (_pPacket is null)
                throw new InvalidOperationException("Could not allocate the packet.");

            _pFrame = ffmpeg.av_frame_alloc();
            if (_pFrame is null)
                throw new InvalidOperationException("Could not allocate audio frame.");
        }

        public byte[] Encoder(byte[] _buffer, long frameNum)
        {
            byte** _inputData = null;
            byte** _outputData = null;
            _pFrame->nb_samples = _pCodecContext->frame_size;
            _pFrame->format = (int)_outSampleFormat;
            _pFrame->channel_layout = _pCodecContext->channel_layout;
            _pFrame->pts = frameNum * _pFrame->nb_samples;
            Console.WriteLine($"[{frameNum}]时间基数-{_pFrame->pts}");

            Error = ffmpeg.av_frame_get_buffer(_pFrame, 0).ThrowExceptionIfError();

            //TODO:就是这儿的问题

            #region pcm数据转码为aac

            int sourceLinesie;
            int sourceChannelsCount = ffmpeg.av_get_channel_layout_nb_channels((ulong)_inputChannelLayout);

            Error = ffmpeg.av_samples_alloc_array_and_samples(&_inputData,
                &sourceLinesie, sourceChannelsCount, _inputSamplesCount, _inputSampleFormat, 0);

            if (Error < 0)
            {
                Console.Error.WriteLine("Could not allocate source samples");
                Error.ThrowExceptionIfError();
            }

            int destinationSampleCount = (int)ffmpeg.av_rescale_rnd(_inputSamplesCount,
                _pCodecContext->sample_rate, 8000, AVRounding.AV_ROUND_UP);

            int maxDestinationSampleCount = _pCodecContext->channels;

            int destinationLinesize;
            int destionationChannelsCount = ffmpeg.av_get_channel_layout_nb_channels(_pCodecContext->channel_layout);
            Error = ffmpeg.av_samples_alloc_array_and_samples(&_outputData, &destinationLinesize,
                destionationChannelsCount, destinationSampleCount, _outSampleFormat, 0);

            if (Error < 0)
            {
                Console.Error.WriteLine("Could not allocate destination samples.");
                Error.ThrowExceptionIfError();
            }

            /* Compute destination number of samples */
            destinationSampleCount = (int)ffmpeg.av_rescale_rnd(ffmpeg.swr_get_delay(_pSwrContext, 8000) +
                                                                _inputSamplesCount, _pCodecContext->sample_rate, 8000, AVRounding.AV_ROUND_UP);
            if (destinationSampleCount > maxDestinationSampleCount)
            {
                ffmpeg.av_freep(&_outputData[0]);
                Error = ffmpeg.av_samples_alloc(_outputData, &destinationLinesize, destionationChannelsCount,
                    destinationSampleCount, _pCodecContext->sample_fmt, 1);
                if (Error < 0)
                {
                    Error.ThrowExceptionIfError();
                }

                maxDestinationSampleCount = destinationSampleCount;
            }

            /* Convert to destination format */
            Error = ffmpeg.swr_convert(_pSwrContext, _outputData, destinationSampleCount, _inputData, _inputSamplesCount);
            if (Error < 0)
            {
                Console.Error.Write("Error while converting\n");
                Error.ThrowExceptionIfError();
            }

            int destinationBufferSize = ffmpeg.av_samples_get_buffer_size(&destinationLinesize, destionationChannelsCount,
                Error, _pCodecContext->sample_fmt, 1);
            if (destinationBufferSize < 0)
            {
                Console.Error.Write("Could not get sample buffer size\n");
                destinationBufferSize.ThrowExceptionIfError();
            }

            #endregion pcm数据转码为aac

            _pFrame->data[0] = _outputData[0];
            _pFrame->data[1] = _outputData[1];
            //using var fs = File.Open("", FileMode.Open);
            //AudioEncoder.Encode(_pCodecContext, _pFrame, _pPacket, fs);
            //ffmpeg.av_frame_unref(_pFrame);
            return null;

            //AudioEncoder.Encode(_pCodecContext, _pFrame, _pPacket, out var buffer);
            //AudioEncoder.Encode(_pCodecContext, null, _pPacket, out buffer);
            //ffmpeg.av_free(_pFrame);

            //return buffer;
        }

        public void Dispose()
        {
            var frame = _pFrame;
            ffmpeg.av_frame_free(&frame);

            var packet = _pPacket;
            ffmpeg.av_packet_free(&packet);

            var pCodecContext = _pCodecContext;
            ffmpeg.avcodec_free_context(&pCodecContext);

            ffmpeg.av_free(_pCodec);
        }

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
    }
}