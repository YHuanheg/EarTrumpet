using EarTrumpet.DataModel.Audio;
using System;

namespace EarTrumpet.DataModel
{
    /// <summary>
    /// A remembered volume was put back on a device that just reconnected.
    /// </summary>
    public class VolumeRestoredEventArgs : EventArgs
    {
        public VolumeRestoredEventArgs(IAudioDevice device, int volume, bool isMuted)
        {
            Device = device;
            Volume = volume;
            IsMuted = isMuted;
        }

        public IAudioDevice Device { get; }

        /// <summary>The level the device actually ended up at, in percent - not what we asked
        /// for: some endpoints snap to the nearest level they support.</summary>
        public int Volume { get; }

        public bool IsMuted { get; }
    }
}
