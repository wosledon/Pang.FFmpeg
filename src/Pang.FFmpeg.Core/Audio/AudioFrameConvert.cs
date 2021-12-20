using FFmpeg.AutoGen;

namespace Pang.FFmpeg.Core.Audio
{
    public sealed unsafe class AudioFrameConvert
    {
        // 转码器
        private SwrContext* SwrContext;

        // 声道
        private long OutCHLayout { get; }

        private AVSampleFormat outSampleFormat { get; }

        public AudioFrameConvert()
        {
            // 初始化转码器
            SwrContext = ffmpeg.swr_alloc();
        }

        public AVFrame? Convert(byte[] input)
        {
            return null;
        }
    }
}