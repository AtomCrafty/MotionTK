using System.Diagnostics.CodeAnalysis;
using FFmpeg.AutoGen;
using OpenTK.Mathematics;
using static FFmpeg.AutoGen.ffmpeg;

namespace MotionTK;

public unsafe class DataSource {
	private const int MaxAudioSamples = 192000;
	private const int MaxQueuedPackets = 10;

	#region Fields

	private int _videoStreamId = -1;
	private AVCodecContext* _videoContext;
	private AVCodec* _videoCodec;
	private AVFrame* _videoRawFrame;
	private AVFrame* _videoRgbaFrame;
	private byte* _videoRawBuffer;
	private byte* _videoRgbaBuffer;
	private SwsContext* _videoSwContext;

	private int _audioStreamId = -1;
	private AVCodecContext* _audioContext;
	private AVCodec* _audioCodec;
	private AVFrame* _audioRawBuffer;
	private byte* _audioPcmBuffer;
	private SwrContext* _audioSwContext;

	private AVFormatContext* _formatContext;
	private Thread? _decodeThread;
	private bool _runDecodeThread;
	private bool _playingToEof;
	private TimeSpan _playingOffset = TimeSpan.Zero;
	private DateTime _lastUpdate;

	#endregion

	#region Properties

	[MemberNotNullWhen(true, nameof(VideoPlayback))]
	public bool HasVideo => _videoStreamId != -1;
	[MemberNotNullWhen(true, nameof(AudioPlayback))]
	public bool HasAudio => _audioStreamId != -1;
	public VideoPlayback? VideoPlayback { get; private set; }
	public AudioPlayback? AudioPlayback { get; private set; }
	public Vector2i VideoSize { get; private set; } = Vector2i.Zero;
	public PlayState State { get; private set; } = PlayState.Stopped;
	public int AudioChannelCount { get; private set; } = -1;
	public int AudioSampleRate => HasAudio ? _audioContext->sample_rate : -1;
	public TimeSpan FileLength { get; private set; } = TimeSpan.Zero;
	public bool IsEndOfFileReached { get; private set; }

	public bool IsFull => (!HasVideo || VideoPlayback.QueuedPackets >= MaxQueuedPackets)
					   && (!HasAudio || AudioPlayback.QueuedPackets >= MaxQueuedPackets);

	public TimeSpan PlayingOffset {
		get => _playingOffset;
		set {
			if(!HasVideo && !HasAudio) return;
			bool startDecode = _runDecodeThread;
			bool startPlaying = State == PlayState.Playing;
			StopDecodeThread();
			_playingOffset = value;
			_playingToEof = false;

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
				VideoPlayback.Flush();
			}

			if(HasAudio) {
				var timebase = _formatContext->streams[_audioStreamId]->time_base;
				float ftb = (float)timebase.den / timebase.num;
				long pos = (long)(PlayingOffset.TotalSeconds * ftb);
				av_seek_frame(_formatContext, _audioStreamId, pos, AVSEEK_FLAG_ANY);
				avcodec_flush_buffers(_audioContext);
				AudioPlayback.Flush();
			}

			if(startDecode) StartDecodeThread();
			if(startPlaying) Play();
		}
	}

	public TimeSpan FrameDuration {
		get {
			if(!HasVideo) return TimeSpan.Zero;

			var a = _formatContext->streams[_videoStreamId]->avg_frame_rate;
			if(a.num != 0 || a.den != 0) return TimeSpan.FromTicks(TimeSpan.TicksPerSecond * a.den / a.num);

			var r = _formatContext->streams[_videoStreamId]->r_frame_rate;
			if(r.num != 0 || r.den != 0) return TimeSpan.FromTicks(TimeSpan.TicksPerSecond * r.den / r.num);

			double tickCount = _formatContext->streams[_videoStreamId]->duration;
			double frameCount = _formatContext->streams[_videoStreamId]->nb_frames;
			double ticksPerFrame = tickCount / frameCount;
			double tickDuration = (double)_formatContext->streams[_videoStreamId]->time_base.num / _formatContext->streams[_videoStreamId]->time_base.den;

			double frameDuration = ticksPerFrame * tickDuration;
			return TimeSpan.FromTicks((long)(frameDuration * TimeSpan.TicksPerSecond));
		}
	}

	#endregion

	#region Setup

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

		if(enableVideo) {
			AVCodec* videoCodec;
			_videoStreamId = av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &videoCodec, 0);
			if(_videoStreamId == AVERROR_STREAM_NOT_FOUND) {
				_videoStreamId = -1;
				Console.WriteLine("Motion: Failed to find video stream: " + path);
			}
			else {
				_videoCodec = videoCodec;
				InitVideo();
			}
		}

		if(enableAudio) {
			AVCodec* audioCodec;
			_audioStreamId = av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &audioCodec, 0);
			if(_audioStreamId == AVERROR_STREAM_NOT_FOUND) {
				_audioStreamId = -1;
				Console.WriteLine("Motion: Failed to find audio stream: " + path);
			}
			else {
				_audioCodec = audioCodec;
				InitAudio();
			}
		}

		if(_formatContext->duration != AV_NOPTS_VALUE) FileLength = TimeSpan.FromTicks((long)(_formatContext->duration / 1000d * TimeSpan.TicksPerMillisecond));
		if(HasVideo || HasAudio) {
			StartDecodeThread();
			VideoPlayback?.SourceReloaded();
			AudioPlayback?.SourceReloaded();
		}
		else {
			Console.WriteLine("Motion: Failed to initialize data source");
			Dispose();
		}
	}

	private void InitVideo() {
		if(!HasVideo) return;

		_videoContext = avcodec_alloc_context3(_videoCodec);

		if(_videoContext == null) {
			Console.WriteLine("Motion: Failed to allocate video codec context");
			_videoStreamId = -1;
			return;
		}

		if(avcodec_parameters_to_context(_videoContext, _formatContext->streams[_videoStreamId]->codecpar) < 0) {
			Console.WriteLine("Motion: Failed to initialize video codec context");
			_videoStreamId = -1;
			return;
		}

		if(avcodec_open2(_videoContext, _videoCodec, null) != 0) {
			Console.WriteLine("Motion: Failed to load video codec");
			_videoStreamId = -1;
			return;
		}

		VideoSize = new Vector2i(_videoContext->width, _videoContext->height);
		_videoRawFrame = CreateVideoFrame(_videoContext->pix_fmt, VideoSize.X, VideoSize.Y, ref _videoRawBuffer);
		_videoRgbaFrame = CreateVideoFrame(AVPixelFormat.AV_PIX_FMT_RGBA, VideoSize.X, VideoSize.Y, ref _videoRgbaBuffer);

		if(_videoRawFrame == null || _videoRgbaFrame == null) {
			Console.WriteLine("Motion: Failed to create video frames");
			_videoStreamId = -1;
			return;
		}

		int swapMode = SWS_FAST_BILINEAR;
		if(VideoSize.X * VideoSize.Y <= 500000 && VideoSize.X % 8 != 0) swapMode |= SWS_ACCURATE_RND;
		_videoSwContext = sws_getCachedContext(null, VideoSize.X, VideoSize.Y, _videoContext->pix_fmt, VideoSize.X, VideoSize.Y, AVPixelFormat.AV_PIX_FMT_RGBA, swapMode, null, null, null);

		if(_videoSwContext == null) {
			Console.WriteLine("Motion: Failed to create video scaling context");
			_videoStreamId = -1;
		}

		VideoPlayback = new VideoPlayback(this);
	}

	private void InitAudio() {
		if(!HasAudio) return;

		_audioContext = avcodec_alloc_context3(_audioCodec);

		if(_audioContext == null) {
			Console.WriteLine("Motion: Failed to allocate audio codec context");
			_audioStreamId = -1;
			return;
		}

		if(avcodec_parameters_to_context(_audioContext, _formatContext->streams[_audioStreamId]->codecpar) < 0) {
			Console.WriteLine("Motion: Failed to initialize audio codec context");
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
		var bufferSize = av_samples_get_buffer_size(null, _audioContext->ch_layout.nb_channels, MaxAudioSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
		if(av_samples_alloc(&audioPcmBuffer, null, _audioContext->ch_layout.nb_channels, bufferSize, AVSampleFormat.AV_SAMPLE_FMT_S16, 0) < 0) {
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

		var inLayout = _audioContext->ch_layout;
		var outLayout = new AVChannelLayout {
			order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
			nb_channels = inLayout.nb_channels == 1 ? 1 : 2,
			u = { mask = inLayout.nb_channels == 1 ? AV_CH_LAYOUT_MONO : AV_CH_LAYOUT_STEREO }
		};

		av_opt_set_chlayout(_audioSwContext, "in_chlayout", &inLayout, 0);
		av_opt_set_chlayout(_audioSwContext, "out_chlayout", &outLayout, 0);
		av_opt_set_int(_audioSwContext, "in_sample_rate", _audioContext->sample_rate, 0);
		av_opt_set_int(_audioSwContext, "out_sample_rate", _audioContext->sample_rate, 0);
		av_opt_set_sample_fmt(_audioSwContext, "in_sample_fmt", _audioContext->sample_fmt, 0);
		av_opt_set_sample_fmt(_audioSwContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);

		if(swr_init(_audioSwContext) < 0) {
			Console.WriteLine("Motion: Failed to initialize resampling context");
			_audioStreamId = -1;
			return;
		}

		AudioChannelCount = outLayout.nb_channels;
		AudioPlayback = new AudioPlayback(this);
	}

	~DataSource() => Dispose();

	public void Dispose() {
		GC.SuppressFinalize(this);
		Stop();
		StopDecodeThread();
		_playingOffset = TimeSpan.Zero;
		FileLength = TimeSpan.Zero;
		VideoSize = Vector2i.Zero;
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
		}
		if(_videoRgbaFrame != null) {
			DestroyVideoFrame(ref _videoRgbaFrame, ref _videoRgbaBuffer);
			_videoRgbaFrame = null;
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

		VideoPlayback?.Dispose();
		AudioPlayback?.Dispose();
		VideoPlayback = null;
		AudioPlayback = null;
	}

	private static AVFrame* CreateVideoFrame(AVPixelFormat format, int width, int height, ref byte* buffer) {
		var frame = av_frame_alloc();
		if(ClearBuffer(frame, format, width, height, ref buffer)) return frame;

		av_frame_free(&frame);
		return null;
	}

	private static bool ClearBuffer(AVFrame* frame, AVPixelFormat format, int width, int height, ref byte* buffer) {
		if(frame == null) return false;

		ulong size = (ulong)av_image_get_buffer_size(format, width, height, sizeof(int));
		buffer = (byte*)av_malloc(size);
		if(buffer == null) return false;

		var data = new byte_ptrArray4();
		var lineSize = new int_array4();
		av_image_fill_arrays(ref data, ref lineSize, buffer, format, width, height, sizeof(int));
		for(uint i = 0; i < 4; i++) {
			frame->data[i] = data[i];
			frame->linesize[i] = lineSize[i];
		}
		return true;
	}

	private static void DestroyVideoFrame(ref AVFrame* frame, ref byte* buffer) {
		var f = frame;
		av_frame_free(&f);
		frame = null;
		av_free(buffer);
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
		StopDecodeThread();
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
		//if(deltaTime < TimeSpan.FromMilliseconds(100))
		VideoPlayback?.Update(deltaTime);
		AudioPlayback?.Update();
	}

	private void NotifyStateChanged(PlayState newState) {
		VideoPlayback?.StateChanged(State, newState);
		AudioPlayback?.StateChanged(State, newState);
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
		if(_decodeThread == null || !_runDecodeThread)
			return;
		_runDecodeThread = false;
		_decodeThread.Join();
		_decodeThread = null;
	}

	private void DecodeThreadRun() {
		var packet = av_packet_alloc();
		while(_runDecodeThread) {
			while(!IsFull && _runDecodeThread && !_playingToEof) {
				bool validPacket = false;
				while(!validPacket && _runDecodeThread) {
					if(av_read_frame(_formatContext, packet) == 0) {
						if(packet->stream_index == _videoStreamId) {
							validPacket = DecodeVideo(packet);
						}
						else if(packet->stream_index == _audioStreamId) {
							validPacket = DecodeAudio(packet);
						}
						else Console.WriteLine("Discarding packet for stream " + packet->stream_index);
					}
					else {
						_playingToEof = true;
						validPacket = true;
					}
					av_packet_unref(packet);
				}
			}
			Thread.Sleep(50);
		}
		av_packet_free(&packet);
	}

	private bool DecodeVideo(AVPacket* packet) {

		if(avcodec_send_packet(_videoContext, packet) < 0) return false;
		if(avcodec_receive_frame(_videoContext, _videoRawFrame) < 0) return false;

		if(sws_scale(_videoSwContext, _videoRawFrame->data, _videoRawFrame->linesize, 0, _videoContext->height, _videoRgbaFrame->data, _videoRgbaFrame->linesize) == 0) return false;

		var videoPacket = new VideoPacket(_videoRgbaFrame->data[0], TimeSpan.Zero);
		VideoPlayback?.PushPacket(videoPacket);
		ClearBuffer(_videoRgbaFrame, AVPixelFormat.AV_PIX_FMT_RGBA, VideoSize.X, VideoSize.Y, ref _videoRgbaBuffer);

		return true;
	}

	private bool DecodeAudio(AVPacket* packet) {

		if(avcodec_send_packet(_audioContext, packet) < 0) return false;
		if(avcodec_receive_frame(_audioContext, _audioRawBuffer) < 0) return false;

		var audioPcmBuffer = _audioPcmBuffer;
		int convertLength = swr_convert(_audioSwContext, &audioPcmBuffer, _audioRawBuffer->nb_samples, _audioRawBuffer->extended_data, _audioRawBuffer->nb_samples);

		if(convertLength <= 0) return false;

		var audioPacket = new AudioPacket(_audioPcmBuffer, convertLength, AudioChannelCount);
		AudioPlayback?.PushPacket(audioPacket);

		return true;
	}

	#endregion
}

public enum PlayState {
	Stopped, Playing, Paused
}
