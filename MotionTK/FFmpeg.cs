using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

// ReSharper disable InconsistentNaming
// ReSharper disable MemberHidesStaticFromOuterClass
#pragma warning disable IDE1006

namespace MotionTK {
	internal static unsafe class FFmpeg {

		#region Macros

		public static long AV_NOPTS_VALUE = unchecked((long)0x8000000000000000L);

		public const int AVSEEK_FLAG_ANY = 0x4;

		public const int SWS_FAST_BILINEAR = 0x1;
		public const int SWS_ACCURATE_RND = 0x40000;

		public const int AV_CH_FRONT_LEFT = 0x1;
		public const int AV_CH_FRONT_RIGHT = 0x2;
		public const int AV_CH_FRONT_CENTER = 0x4;

		public const int AV_CH_LAYOUT_MONO = AV_CH_FRONT_CENTER;
		public const int AV_CH_LAYOUT_STEREO = AV_CH_FRONT_LEFT | AV_CH_FRONT_RIGHT;

		#endregion

		#region Methods

		#region avutil

		internal static void* av_malloc(ulong size) {
			return NativeMethods.av_malloc(size);
		}

		internal static void av_free(void* ptr) {
			NativeMethods.av_free(ptr);
		}

		internal static AVFrame* av_frame_alloc() {
			return NativeMethods.av_frame_alloc();
		}

		internal static void av_frame_unref(AVFrame* frame) {
			NativeMethods.av_frame_unref(frame);
		}

		internal static void av_frame_free(AVFrame** frame) {
			NativeMethods.av_frame_free(frame);
		}

		internal static int av_image_get_buffer_size(AVPixelFormat pixFmt, int width, int height, int align) {
			return NativeMethods.av_image_get_buffer_size(pixFmt, width, height, align);
		}

		internal static int av_image_fill_arrays(ref byte_ptrArray4 dstData, ref int_array4 dstLinesize, byte* src, AVPixelFormat pixFmt, int width, int height, int align) {
			return NativeMethods.av_image_fill_arrays(ref dstData, ref dstLinesize, src, pixFmt, width, height, align);
		}

		internal static int av_samples_get_buffer_size(int* linesize, int nbChannels, int nbSamples, AVSampleFormat sampleFmt, int align) {
			return NativeMethods.av_samples_get_buffer_size(linesize, nbChannels, nbSamples, sampleFmt, align);
		}

		internal static int av_samples_alloc(byte** audioData, int* linesize, int nbChannels, int nbSamples, AVSampleFormat sampleFmt, int align) {
			return NativeMethods.av_samples_alloc(audioData, linesize, nbChannels, nbSamples, sampleFmt, align);
		}

		internal static long av_get_default_channel_layout(int nbChannels) {
			return NativeMethods.av_get_default_channel_layout(nbChannels);
		}

		internal static int av_get_channel_layout_nb_channels(ulong channelLayout) {
			return NativeMethods.av_get_channel_layout_nb_channels(channelLayout);
		}

		internal static int av_opt_set_int(void* obj, [MarshalAs(UnmanagedType.LPStr)] string name, long val, int searchFlags) {
			return NativeMethods.av_opt_set_int(obj, name, val, searchFlags);
		}

		internal static int av_opt_set_sample_fmt(void* obj, [MarshalAs(UnmanagedType.LPStr)] string name, AVSampleFormat fmt, int searchFlags) {
			return NativeMethods.av_opt_set_sample_fmt(obj, name, fmt, searchFlags);
		}

		#endregion

		#region avformat

		internal static int avformat_open_input(AVFormatContext** ps, [MarshalAs(UnmanagedType.LPStr)] string url, AVInputFormat* fmt, AVDictionary** options) {
			return NativeMethods.avformat_open_input(ps, url, fmt, options);
		}

		internal static void avformat_close_input(AVFormatContext** s) {
			NativeMethods.avformat_close_input(s);
		}

		internal static int avformat_find_stream_info(AVFormatContext* ic, AVDictionary** options) {
			return NativeMethods.avformat_find_stream_info(ic, options);
		}

		internal static int av_read_frame(AVFormatContext* s, AVPacket* pkt) {
			return NativeMethods.av_read_frame(s, pkt);
		}

		internal static int av_seek_frame(AVFormatContext* s, int streamIndex, long timestamp, int flags) {
			return NativeMethods.av_seek_frame(s, streamIndex, timestamp, flags);
		}

		#endregion

		#region avcodec

		internal static AVCodec* avcodec_find_decoder(AVCodecID id) {
			return NativeMethods.avcodec_find_decoder(id);
		}

		internal static int avcodec_open2(AVCodecContext* avctx, AVCodec* codec, AVDictionary** options) {
			return NativeMethods.avcodec_open2(avctx, codec, options);
		}

		internal static int avcodec_close(AVCodecContext* avctx) {
			return NativeMethods.avcodec_close(avctx);
		}

		internal static AVPacket* av_packet_alloc() {
			return NativeMethods.av_packet_alloc();
		}

		internal static void av_init_packet(AVPacket* pkt) {
			NativeMethods.av_init_packet(pkt);
		}

		internal static void av_packet_unref(AVPacket* pkt) {
			NativeMethods.av_packet_unref(pkt);
		}

		internal static void av_packet_free(AVPacket** pkt) {
			NativeMethods.av_packet_free(pkt);
		}

		internal static int avcodec_send_packet(AVCodecContext* avctx, AVPacket* avpkt) {
			return NativeMethods.avcodec_send_packet(avctx, avpkt);
		}

		internal static int avcodec_receive_frame(AVCodecContext* avctx, AVFrame* frame) {
			return NativeMethods.avcodec_receive_frame(avctx, frame);
		}

		internal static void avcodec_flush_buffers(AVCodecContext* avctx) {
			NativeMethods.avcodec_flush_buffers(avctx);
		}

		#endregion

		#region swscale

		internal static SwsContext* sws_getCachedContext(SwsContext* context, int srcW, int srcH, AVPixelFormat srcFormat, int dstW, int dstH, AVPixelFormat dstFormat, int flags, SwsFilter* srcFilter, SwsFilter* dstFilter, double* param) {
			return NativeMethods.sws_getCachedContext(context, srcW, srcH, srcFormat, dstW, dstH, dstFormat, flags, srcFilter, dstFilter, param);
		}

		internal static int sws_scale(SwsContext* c, byte*[] srcSlice, int[] srcStride, int srcSliceY, int srcSliceH, byte*[] dst, int[] dstStride) {
			return NativeMethods.sws_scale(c, srcSlice, srcStride, srcSliceY, srcSliceH, dst, dstStride);
		}

		internal static void sws_freeContext(SwsContext* c) {
			NativeMethods.sws_freeContext(c);
		}

		#endregion

		#region swresample

		internal static SwrContext* swr_alloc() {
			return NativeMethods.swr_alloc();
		}

		internal static int swr_init(SwrContext* s) {
			return NativeMethods.swr_init(s);
		}

		internal static int swr_convert(SwrContext* s, byte** @out, int outCount, byte** @in, int inCount) {
			return NativeMethods.swr_convert(s, @out, outCount, @in, inCount);
		}

		internal static void swr_free(SwrContext** s) {
			NativeMethods.swr_free(s);
		}

		#endregion

		#endregion

		private static class NativeMethods {
			private const string LibAVUtil = "avutil-56";
			private const string LibAVFormat = "avformat-58";
			private const string LibAVCodec = "avcodec-58";
			private const string LibSwScale = "swscale-5";
			private const string LibSwResample = "swresample-3";

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void* av_malloc(ulong size);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void av_free(void* ptr);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern AVFrame* av_frame_alloc();

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void av_frame_unref(AVFrame* frame);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void av_frame_free(AVFrame** frame);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_image_get_buffer_size(AVPixelFormat pixFmt, int width, int height, int align);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_image_fill_arrays(ref byte_ptrArray4 dstData, ref int_array4 dstLinesize, byte* src, AVPixelFormat pixFmt, int width, int height, int align);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_samples_get_buffer_size(int* linesize, int nbChannels, int nbSamples, AVSampleFormat sampleFmt, int align);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_samples_alloc(byte** audioData, int* linesize, int nbChannels, int nbSamples, AVSampleFormat sampleFmt, int align);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern long av_get_default_channel_layout(int nbChannels);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_get_channel_layout_nb_channels(ulong channelLayout);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_opt_set_int(void* obj, [MarshalAs(UnmanagedType.LPStr)] string name, long val, int searchFlags);

			[DllImport(LibAVUtil, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_opt_set_sample_fmt(void* obj, [MarshalAs(UnmanagedType.LPStr)] string name, AVSampleFormat fmt, int searchFlags);


			[DllImport(LibAVFormat, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int avformat_open_input(AVFormatContext** ps, [MarshalAs(UnmanagedType.LPStr)] string url, AVInputFormat* fmt, AVDictionary** options);

			[DllImport(LibAVFormat, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void avformat_close_input(AVFormatContext** s);

			[DllImport(LibAVFormat, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int avformat_find_stream_info(AVFormatContext* ic, AVDictionary** options);

			[DllImport(LibAVFormat, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_read_frame(AVFormatContext* s, AVPacket* pkt);

			[DllImport(LibAVFormat, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int av_seek_frame(AVFormatContext* s, int streamIndex, long timestamp, int flags);


			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern AVCodec* avcodec_find_decoder(AVCodecID id);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int avcodec_open2(AVCodecContext* avctx, AVCodec* codec, AVDictionary** options);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int avcodec_close(AVCodecContext* avctx);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void av_packet_free(AVPacket** pkt);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void av_init_packet(AVPacket* pkt);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void av_packet_unref(AVPacket* pkt);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern AVPacket* av_packet_alloc();

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int avcodec_send_packet(AVCodecContext* avctx, AVPacket* avpkt);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int avcodec_receive_frame(AVCodecContext* avctx, AVFrame* frame);

			[DllImport(LibAVCodec, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void avcodec_flush_buffers(AVCodecContext* avctx);


			[DllImport(LibSwScale, CallingConvention = CallingConvention.Cdecl)]
			internal static extern SwsContext* sws_getCachedContext(SwsContext* context, int srcW, int srcH, AVPixelFormat srcFormat, int dstW, int dstH, AVPixelFormat dstFormat, int flags, SwsFilter* srcFilter, SwsFilter* dstFilter, double* param);

			[DllImport(LibSwScale, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int sws_scale(SwsContext* c, byte*[] srcSlice, int[] srcStride, int srcSliceY, int srcSliceH, byte*[] dst, int[] dstStride);

			[DllImport(LibSwScale, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void sws_freeContext(SwsContext* c);


			[DllImport(LibSwResample, CallingConvention = CallingConvention.Cdecl)]
			internal static extern SwrContext* swr_alloc();

			[DllImport(LibSwResample, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int swr_init(SwrContext* s);

			[DllImport(LibSwResample, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int swr_convert(SwrContext* s, byte** @out, int outCount, byte** @in, int inCount);

			[DllImport(LibSwResample, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void swr_free(SwrContext** s);
		}
	}
}

#pragma warning restore IDE1006