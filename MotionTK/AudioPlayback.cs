using System.Linq;
using System.Threading;
using OpenTK.Audio.OpenAL;

namespace MotionTK {
	public class AudioPlayback : Playback<AudioPacket> {

		protected int _sourceHandle;
		protected int _channelCount;
		protected int _sampleRate;
		protected Thread _playbackThread;

		internal AudioPlayback(DataSource dataSource) : base(dataSource) {
			_sourceHandle = AL.GenSource();
			_playbackThread = new Thread(PlaybackThread) { Name = "Audio Playback" };
			_playbackThread.Start();
		}

		public override void Dispose() {
			if(_sourceHandle == -1) return;

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

			_playbackThread.Join();
			_playbackThread = null;

			base.Dispose();
		}

		internal override void SourceReloaded() {
			if(!DataSource.HasAudio) return;
			_channelCount = DataSource.AudioChannelCount;
			_sampleRate = DataSource.AudioSampleRate;
			StateChanged(DataSource.State, DataSource.State);
		}

		internal override void StateChanged(PlayState oldState, PlayState newState) {
			switch(newState) {
				case PlayState.Playing when DataSource.HasAudio:
					AL.SourcePlay(_sourceHandle);
					break;
				case PlayState.Paused when DataSource.HasAudio:
					AL.SourcePause(_sourceHandle);
					break;
				case PlayState.Stopped:
					AL.SourceStop(_sourceHandle);
					while(PacketQueue.Any()) {
						PacketQueue.Take();
					}
					break;
			}
		}

		protected void PlaybackThread() {
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
	}
}
