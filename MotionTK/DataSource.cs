using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

namespace MotionTK {
	public unsafe class DataSource : IDisposable {
		private const int MaxAudioSamples = 192000;
		private const int MaxPacketQueue = 20;

		#region Fields and Properties

		private int _videoStreamId = -1;
		private Size _videoSize = Size.Empty;
		private AVCodecContext* _videoContext;
		private AVCodec* _videoCodec;
		private AVFrame* _videoRawFrame;
		private AVFrame* _videoRgbaFrame;
		private byte* _videoRawBuffer;
		private byte* _videoRgbaBuffer;
		private SwsContext* _videoSwContext;
		internal readonly List<VideoPlayback> VideoPlaybacks = new List<VideoPlayback>();

		private int _audioStreamId = -1;
		private AVCodecContext* _audioContext;
		private AVCodec* _audioCodec;
		private AVFrame* _audioRawBuffer;
		private byte* _audioPcmBuffer;
		private SwrContext* _audioSwContext;
		internal readonly List<AudioPlayback> AudioPlaybacks = new List<AudioPlayback>();

		private AVFormatContext* _formatContext;
		private Thread _decodeThread;
		private bool _runDecodeThread;
		private bool _playingToEof;
		private TimeSpan _playingOffset = TimeSpan.Zero;
		private DateTime _lastUpdate;
		internal object PlaybackLock = new object();

		public bool HasVideo => _videoStreamId != -1;
		public bool HasAudio => _audioStreamId != -1;
		public Size VideoSize => new Size(_videoSize.Width, _videoSize.Height);
		public PlayState State { get; private set; } = PlayState.Stopped;
		public int AudioChannelCount { get; private set; } = -1;
		public int AudioSampleRate => HasAudio ? _audioContext->sample_rate : -1;
		public TimeSpan FileLength { get; private set; } = TimeSpan.Zero;
		public bool IsEndOfFileReached { get; private set; }

		public bool IsFull {
			get {
				lock(PlaybackLock) {
					if(HasVideo) {
						foreach(var playback in VideoPlaybacks) {
							lock(playback.Lock) {
								if(playback.PacketQueue.Count < MaxPacketQueue) {
									return false;
								}
							}
						}
					}
					else if(HasAudio) {
						foreach(var playback in AudioPlaybacks) {
							lock(playback.Lock) {
								if(playback.PacketQueue.Count < MaxPacketQueue) {
									return false;
								}
							}
						}
					}
					return true;
				}
			}
		}
		public TimeSpan PlayingOffset {
			get => _playingOffset;
			set {
				if(!HasVideo && !HasAudio) return;
				StopDecodeThread();
				_playingOffset = value;
				_playingToEof = false;
				bool startPlaying = State == PlayState.Playing;

				if(State != PlayState.Stopped) {
					IsEndOfFileReached = true;
					NotifyStateChanged(PlayState.Stopped);
					State = PlayState.Stopped;
					IsEndOfFileReached = false;
				}

				if(HasVideo) {
					var timebase = _formatContext->streams[_videoStreamId]->time_base;
					float ftb = (float)timebase.den / timebase.num;
					long pos = (long)(PlayingOffset.TotalSeconds * ftb);
					av_seek_frame(_formatContext, _videoStreamId, pos, AVSEEK_FLAG_ANY);
					avcodec_flush_buffers(_videoContext);
				}

				if(HasAudio) {
					var timebase = _formatContext->streams[_audioStreamId]->time_base;
					float ftb = (float)timebase.den / timebase.num;
					long pos = (long)(PlayingOffset.TotalSeconds * ftb);
					av_seek_frame(_formatContext, _audioStreamId, pos, AVSEEK_FLAG_ANY);
					avcodec_flush_buffers(_audioContext);
				}

				StartDecodeThread();
				if(startPlaying) Play();
			}
		}
		public TimeSpan VideoFrameDuration {
			get {
				if(!HasVideo) return TimeSpan.Zero;

				double tickCount = _formatContext->streams[_videoStreamId]->duration;
				double frameCount = _formatContext->streams[_videoStreamId]->nb_frames;
				double ticksPerFrame = tickCount / frameCount;
				double tickDuration = (double)_formatContext->streams[_videoStreamId]->time_base.num / _formatContext->streams[_videoStreamId]->time_base.den;

				double frameDuration = ticksPerFrame * tickDuration;
				return TimeSpan.FromTicks((long)(frameDuration * TimeSpan.TicksPerSecond));

				double fileDuration = _formatContext->duration;
				long frameDuration2 = (long)(fileDuration / frameCount * TimeSpan.TicksPerMillisecond) / 1000;
				return TimeSpan.FromTicks(frameDuration2);


				var r1 = _formatContext->streams[_videoStreamId]->avg_frame_rate;
				var r2 = _formatContext->streams[_videoStreamId]->r_frame_rate;

				if((r1.num == 0 || r1.den == 0) && (r2.num == 0 || r2.den == 0)) {
					return TimeSpan.FromSeconds(1.0 / 29.97);
				}
				if(r1.num != 0 && r1.den != 0) {
					return TimeSpan.FromSeconds(1.0 / ((double)r1.num / r1.den));
				}

				return TimeSpan.FromSeconds(1.0 / ((double)r2.num / r2.den));
			}
		}

		#endregion

		#region Setup

		private const string LibAvFormat = "avformat-58";

		[DllImport(LibAvFormat)]
		private static extern void av_register_all();

		public DataSource(string path, bool enableVideo = true, bool enableAudio = true) {
			//av_register_all();
			AVFormatContext* formatContext;
			if(avformat_open_input(&formatContext, path, null, null) != 0) {
				Console.WriteLine("Motion: Failed to open file: " + path);
				return;
			}
			_formatContext = formatContext;
			if(avformat_find_stream_info(formatContext, null) != 0) {
				Console.WriteLine("Motion: Failed to find stream information: " + path);
				return;
			}
			for(int i = 0; i < formatContext->nb_streams; i++) {
				switch(formatContext->streams[i]->codec->codec_type) {
					case AVMediaType.AVMEDIA_TYPE_VIDEO:
						if(_videoStreamId == -1 && enableVideo) _videoStreamId = i;
						break;
					case AVMediaType.AVMEDIA_TYPE_AUDIO:
						if(_audioStreamId == -1 && enableAudio) _audioStreamId = i;
						break;
				}
			}
			InitVideo();
			InitAudio();
			if(_formatContext->duration != AV_NOPTS_VALUE) FileLength = TimeSpan.FromTicks((long)(_formatContext->duration / 1000d * TimeSpan.TicksPerMillisecond));
			if(HasVideo || HasAudio) {
				StartDecodeThread();
				lock(PlaybackLock) {
					foreach(var playback in VideoPlaybacks) {
						playback.SourceReloaded();
					}
					foreach(var playback in AudioPlaybacks) {
						playback.SourceReloaded();
					}
				}
			}
			else {
				Console.WriteLine("Motion: Failed to load audio or video");
				Dispose();
			}
		}

		private void InitVideo() {
			if(!HasVideo) return;

			_videoContext = _formatContext->streams[_videoStreamId]->codec;
			if(_videoContext == null) {
				Console.WriteLine("Motion: Failed to get video codec context");
				_videoStreamId = -1;
				return;
			}

			_videoCodec = avcodec_find_decoder(_videoContext->codec_id);
			if(_videoCodec == null) {
				Console.WriteLine("Motion: Failed to find video codec");
				_videoStreamId = -1;
				return;
			}

			if(avcodec_open2(_videoContext, _videoCodec, null) != 0) {
				Console.WriteLine("Motion: Failed to load video codec");
				_videoStreamId = -1;
				return;
			}

			_videoSize = new Size(_videoContext->width, _videoContext->height);
			_videoRawFrame = CreateVideoFrame(_videoContext->pix_fmt, _videoSize.Width, _videoSize.Height, ref _videoRawBuffer);
			_videoRgbaFrame = CreateVideoFrame(AVPixelFormat.AV_PIX_FMT_RGBA, _videoSize.Width, _videoSize.Height, ref _videoRgbaBuffer);

			if(_videoRawFrame == null || _videoRgbaFrame == null) {
				Console.WriteLine("Motion: Failed to create video frames");
				_videoStreamId = -1;
				return;
			}

			int swapmode = SWS_FAST_BILINEAR;
			if(_videoSize.Width * _videoSize.Height <= 500000 && _videoSize.Width % 8 != 0) swapmode |= SWS_ACCURATE_RND;
			_videoSwContext = sws_getCachedContext(null, _videoSize.Width, _videoSize.Height, _videoContext->pix_fmt, _videoSize.Width, _videoSize.Height, AVPixelFormat.AV_PIX_FMT_RGBA, swapmode, null, null, null);

			if(_videoSwContext == null) {
				Console.WriteLine("Motion: Failed to create video scaling context");
				_videoStreamId = -1;
			}
		}

		private void InitAudio() {
			if(!HasAudio) return;

			_audioContext = _formatContext->streams[_audioStreamId]->codec;
			if(_audioContext == null) {
				Console.WriteLine("Motion: Failed to get audio codec context");
				_audioStreamId = -1;
				return;
			}

			_audioCodec = avcodec_find_decoder(_audioContext->codec_id);
			if(_audioCodec == null) {
				Console.WriteLine("Motion: Failed to find audio codec");
				_audioStreamId = -1;
				return;
			}

			if(avcodec_open2(_audioContext, _audioCodec, null) != 0) {
				Console.WriteLine("Motion: Failed to load audio codec");
				_audioStreamId = -1;
				return;
			}

			_audioRawBuffer = av_frame_alloc();
			if(_audioRawBuffer == null) {
				Console.WriteLine("Motion: Failed to allocate audio buffer");
				_audioStreamId = -1;
				return;
			}

			var audioPcmBuffer = _audioPcmBuffer;
			if(av_samples_alloc(&audioPcmBuffer, null, _audioContext->channels, av_samples_get_buffer_size(null, _audioContext->channels, MaxAudioSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 0), AVSampleFormat.AV_SAMPLE_FMT_S16, 0) < 0) {
				Console.WriteLine("Motion: Failed to create audio samples buffer");
				_audioStreamId = -1;
				return;
			}
			_audioPcmBuffer = audioPcmBuffer;

			av_frame_unref(_audioRawBuffer);
			_audioSwContext = swr_alloc();
			if(_videoSwContext == null) {
				Console.WriteLine("Motion: Failed to create audio resampling context");
				_audioStreamId = -1;
				return;
			}

			ulong inchanlayout = _audioContext->channel_layout;
			if(inchanlayout == 0) inchanlayout = (ulong)av_get_default_channel_layout(_audioContext->channels);
			ulong outchanlayout = inchanlayout;
			if(outchanlayout != AV_CH_LAYOUT_MONO) outchanlayout = AV_CH_LAYOUT_STEREO;
			av_opt_set_int(_audioSwContext, "in_channel_layout", (long)inchanlayout, 0);
			av_opt_set_int(_audioSwContext, "out_channel_layout", (long)outchanlayout, 0);
			av_opt_set_int(_audioSwContext, "in_sample_rate", _audioContext->sample_rate, 0);
			av_opt_set_int(_audioSwContext, "out_sample_rate", _audioContext->sample_rate, 0);
			av_opt_set_sample_fmt(_audioSwContext, "in_sample_fmt", _audioContext->sample_fmt, 0);
			av_opt_set_sample_fmt(_audioSwContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
			swr_init(_audioSwContext);
			AudioChannelCount = av_get_channel_layout_nb_channels(outchanlayout);
		}

		~DataSource() {
			lock(PlaybackLock) {
				foreach(var playback in VideoPlaybacks) {
					playback.Destroy();
				}
				foreach(var playback in AudioPlaybacks) {
					playback.Destroy();
				}
			}
		}

		public void Dispose() {
			Stop();
			StopDecodeThread();
			_playingOffset = TimeSpan.Zero;
			FileLength = TimeSpan.Zero;
			_videoSize = Size.Empty;
			_videoStreamId = -1;
			_audioStreamId = -1;
			AudioChannelCount = -1;

			if(_videoContext != null) {
				avcodec_close(_videoContext);
				_videoContext = null;
			}
			_videoCodec = null;
			if(_audioContext != null) {
				avcodec_close(_audioContext);
				_audioContext = null;
			}
			_audioCodec = null;
			if(_videoRawFrame != null) {
				DestroyVideoFrame(ref _videoRawFrame, ref _videoRawBuffer);
				_videoRawFrame = null;
				_videoRawBuffer = null;
			}
			if(_videoRgbaFrame != null) {
				DestroyVideoFrame(ref _videoRgbaFrame, ref _videoRgbaBuffer);
				_videoRgbaFrame = null;
				_videoRgbaBuffer = null;
			}
			if(_audioRawBuffer != null) {
				var audioRawBuffer = _audioRawBuffer;
				av_frame_free(&audioRawBuffer);
				_audioRawBuffer = null;
			}
			if(_audioPcmBuffer != null) {
				av_free(_audioPcmBuffer);
				_audioPcmBuffer = null;
			}
			if(_videoSwContext != null) {
				sws_freeContext(_videoSwContext);
				_videoSwContext = null;
			}
			if(_audioSwContext != null) {
				var audioSwContext = _audioSwContext;
				swr_free(&audioSwContext);
				_audioSwContext = null;
			}
			if(_formatContext != null) {
				var formatContext = _formatContext;
				avformat_close_input(&formatContext);
				_formatContext = null;
			}

			lock(PlaybackLock) {
				while(VideoPlaybacks.Any()) {
					var playback = VideoPlaybacks[0];
					VideoPlaybacks.Remove(playback);
					playback.Destroy();
				}
				while(AudioPlaybacks.Any()) {
					var playback = AudioPlaybacks[0];
					AudioPlaybacks.Remove(playback);
					playback.Destroy();
				}
			}
		}

		private static AVFrame* CreateVideoFrame(AVPixelFormat format, int width, int height, ref byte* buffer) {
			AVFrame* frame = av_frame_alloc();
			if(frame == null) return null;
			ulong size = (ulong)avpicture_get_size(format, width, height);
			buffer = (byte*)av_malloc(size);
			if(buffer == null) {
				av_frame_free(&frame);
				return null;
			}
			avpicture_fill((AVPicture*)frame, buffer, format, width, height);
			return frame;
		}

		private static void DestroyVideoFrame(ref AVFrame* frame, ref byte* buffer) {
			var f = frame;
			av_free(buffer);
			av_frame_free(&f);
			frame = null;
			buffer = null;
		}

		#endregion

		#region Playpack

		public void Play() {
			if(!HasVideo && !HasAudio || State == PlayState.Playing) return;
			IsEndOfFileReached = false;
			_lastUpdate = DateTime.Now;
			NotifyStateChanged(PlayState.Playing);
			State = PlayState.Playing;
		}

		public void Pause() {
			if(State != PlayState.Playing) return;
			NotifyStateChanged(PlayState.Paused);
			State = PlayState.Paused;
		}

		public void Stop() {
			if(State == PlayState.Stopped) return;
			IsEndOfFileReached = true;
			NotifyStateChanged(PlayState.Stopped);
			State = PlayState.Stopped;
			IsEndOfFileReached = false;
			PlayingOffset = TimeSpan.Zero;
		}

		public void Update() {
			if(PlayingOffset > FileLength) {
				Stop();
				IsEndOfFileReached = true;
			}
			var now = DateTime.Now;
			var deltaTime = now - _lastUpdate;
			_lastUpdate = now;
			if(State == PlayState.Playing) _playingOffset += deltaTime;

			// avoid huge jumps
			if(deltaTime > TimeSpan.FromMilliseconds(100)) return;

			lock(PlaybackLock) {
				foreach(var playback in VideoPlaybacks) {
					playback.Update(deltaTime);
				}
			}
		}

		private void NotifyStateChanged(PlayState newState) {
			lock(PlaybackLock) {
				foreach(var playback in VideoPlaybacks) {
					playback.StateChanged(State, newState);
				}
				foreach(var playback in AudioPlaybacks) {
					playback.StateChanged(State, newState);
				}
			}
		}

		#endregion

		#region Decode

		private void StartDecodeThread() {
			if(_decodeThread != null)
				return;
			_runDecodeThread = true;
			_decodeThread = new Thread(DecodeThreadRun) { Name = "Video Decode" };
			_decodeThread.Start();
		}

		private void StopDecodeThread() {
			if(_decodeThread == null)
				return;
			_runDecodeThread = false;
			_decodeThread.Join();
			_decodeThread = null;
		}

		private void DecodeThreadRun() {
			while(_runDecodeThread) {
				while(!IsFull && _runDecodeThread && !_playingToEof) {
					bool validPacket = false;
					while(!validPacket && _runDecodeThread) {
						var packet = av_packet_alloc();
						av_init_packet(packet);
						if(av_read_frame(_formatContext, packet) == 0) {
							if(packet->stream_index == _videoStreamId) {
								validPacket = DecodeVideo(packet);
							}
							else if(packet->stream_index == _audioStreamId) {
								validPacket = DecodeAudio(packet);
							}
							av_packet_unref(packet);
							av_packet_free(&packet);
						}
						else {
							_playingToEof = true;
							validPacket = true;
							av_packet_unref(packet);
							av_packet_free(&packet);
						}
					}
				}
				Thread.Sleep(5);
			}
		}

		private bool DecodeVideo(AVPacket* packet) {
			int decodeResult = 0;
			if(avcodec_decode_video2(_videoContext, _videoRawFrame, &decodeResult, packet) < 0 || decodeResult == 0) return false;

			if(sws_scale(_videoSwContext, _videoRawFrame->data, _videoRawFrame->linesize, 0, _videoContext->height, _videoRgbaFrame->data, _videoRgbaFrame->linesize) == 0) return false;


			var videoPacket = new VideoPacket(_videoRgbaFrame->data[0], _videoSize.Width, _videoSize.Height, TimeSpan.Zero);
			lock(PlaybackLock) {
				foreach(var playback in VideoPlaybacks) {
					lock(playback.Lock) {
						playback.PacketQueue.Add(videoPacket);
					}
				}
			}

			return true;
		}

		private bool DecodeAudio(AVPacket* packet) {
			int decodeResult = 0;
			if(avcodec_decode_audio4(_audioContext, _audioRawBuffer, &decodeResult, packet) <= 0 || decodeResult == 0) return false;

			var audioPcmBuffer = _audioPcmBuffer;
			int convertlength = swr_convert(_audioSwContext, &audioPcmBuffer, _audioRawBuffer->nb_samples, _audioRawBuffer->extended_data, _audioRawBuffer->nb_samples);

			if(convertlength <= 0) return false;

			var audioPacket = new AudioPacket(_audioPcmBuffer, convertlength, AudioChannelCount);
			lock(PlaybackLock) {
				foreach(var playback in AudioPlaybacks) {
					lock(playback.Lock) {
						playback.PacketQueue.Add(audioPacket);
					}
				}
			}

			return true;
		}

		#endregion
	}

	public enum PlayState {
		Stopped, Playing, Paused
	}
}
