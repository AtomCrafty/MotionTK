using System;

namespace MotionTK {
	public abstract class Playback {
		internal abstract void SourceReloaded();
		internal abstract void StateChanged(PlayState oldState, PlayState newState);
	}
}
