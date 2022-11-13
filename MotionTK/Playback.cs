using System.Collections.Concurrent;

namespace MotionTK; 

public abstract class Playback<TPacket> : IDisposable where TPacket : Packet {

	public readonly DataSource DataSource;
	protected readonly BlockingCollection<TPacket> _packetQueue = new();

	internal int QueuedPackets => _packetQueue.Count;

	protected Playback(DataSource dataSource) {
		DataSource = dataSource;
	}

	internal abstract void SourceReloaded();
	internal abstract void StateChanged(PlayState oldState, PlayState newState);

	internal virtual void Flush() {
		while(_packetQueue.TryTake(out var packet)) packet.Dispose();
	}

	internal void PushPacket(TPacket packet) {
		_packetQueue.Add(packet);
	}

	public virtual void Dispose() {
		Flush();
		_packetQueue.Dispose();
	}
}