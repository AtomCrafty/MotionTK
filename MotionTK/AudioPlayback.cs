using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using OpenTK.Audio.OpenAL;

namespace MotionTK {
	public class AudioPlayback {
		public readonly DataSource DataSource;
		internal object Lock = new object();
		internal BlockingCollection<AudioPacket> PacketQueue = new BlockingCollection<AudioPacket>();
		private int _sourceHandle;
		private int _channelCount;
		private int _sampleRate;
		private TimeSpan _audioPosition;
		private TimeSpan _audioOffsetCorrection;
		public TimeSpan AudioOffsetCorrection {
			get { lock(Lock) return _audioOffsetCorrection; }
			set { lock(Lock) _audioOffsetCorrection = value; }
		}

		public AudioPlayback(DataSource dataSource, TimeSpan audioOffsetCorrection = default(TimeSpan)) {
			DataSource = dataSource;
			_audioOffsetCorrection = audioOffsetCorrection;
			lock(DataSource.PlaybackLock) DataSource.AudioPlaybacks.Add(this);
			SourceReloaded();

			_sourceHandle = AL.GenSource();
			new Thread(PlaybackThread) { Name = "Audio Playback" }.Start();
		}

		private void PlaybackThread() {
			while(_sourceHandle != -1) {
				const int maxQueue = 40;

				AL.GetSource(_sourceHandle, ALGetSourcei.SourceState, out int state);
				AL.GetSource(_sourceHandle, ALGetSourcei.BuffersQueued, out int queued);
				AL.GetSource(_sourceHandle, ALGetSourcei.BuffersProcessed, out int processed);

				// unqueue processed buffers
				if(processed > 0) {
					var processedBuffers = new int[processed];
					AL.SourceUnqueueBuffers(_sourceHandle, processed, processedBuffers);
					foreach(int bufferHandle in processedBuffers) {
						AudioBuffer.ByHandle(bufferHandle).MakeAvailable();
					}
				}

				// queue new buffers
				while(queued++ < maxQueue && PacketQueue.TryTake(out var packet)) {
					var buffer = AudioBuffer.Get(packet.TotalSampleCount, DataSource.AudioChannelCount == 2 ? ALFormat.Stereo16 : ALFormat.Mono16, _sampleRate);
					buffer.Data = packet.SampleBuffer;
					buffer.Bind();

					AL.SourceQueueBuffer(_sourceHandle, buffer.Handle);
				}

				// restart if necessary
				if((ALSourceState)state != ALSourceState.Playing && DataSource.State == PlayState.Playing) {
					//Console.WriteLine("Playing source (" + (ALSourceState)state + ", " + processed + "/" + queued + ")");
					//AL.SourcePause(_sourceHandle);
					AL.SourcePlay(_sourceHandle);
				}
			}
		}

		public void Destroy() {
			if(_sourceHandle == -1) return;
			lock(DataSource.PlaybackLock) {
				DataSource.AudioPlaybacks.Remove(this);

				// delete remaining packets
				// ReSharper disable once EmptyEmbeddedStatement
				while(PacketQueue.TryTake(out _)) ;

				// free queued buffers
				AL.SourceStop(_sourceHandle);
				AL.GetSource(_sourceHandle, ALGetSourcei.BuffersQueued, out int queued);
				if(queued > 0) {
					var buffers = new int[queued];
					AL.SourceUnqueueBuffers(_sourceHandle, queued, buffers);
					foreach(int b in buffers) {
						AL.DeleteBuffer(b);
					}
				}
				AL.DeleteSource(_sourceHandle);
				_sourceHandle = -1;
			}
		}

		internal void SourceReloaded() {
			if(!DataSource.HasAudio) return;
			_channelCount = DataSource.AudioChannelCount;
			_sampleRate = DataSource.AudioSampleRate;
			StateChanged(DataSource.State, DataSource.State);
		}

		internal void StateChanged(PlayState oldState, PlayState newState) {
			switch(newState) {
				case PlayState.Playing when DataSource.HasAudio:
					AL.SourcePlay(_sourceHandle);
					break;
				case PlayState.Paused when DataSource.HasAudio:
					AL.SourcePause(_sourceHandle);
					break;
				case PlayState.Stopped:
					AL.SourceStop(_sourceHandle);
					lock(Lock) {
						while(PacketQueue.Any()) {
							PacketQueue.Take();
						}
					}
					break;
			}
		}
	}
}
