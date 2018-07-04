using System;

namespace MotionTK {
	public abstract class Packet : IDisposable {
		~Packet() => Dispose();
		public abstract void Dispose();
	}
}
