﻿using nexRemote.Desktop.Core.Interfaces;
using System;

namespace nexRemote.Desktop.XPlat.Services
{
    public class AudioCapturerLinux : IAudioCapturer
    {
#pragma warning disable
        public event EventHandler<byte[]> AudioSampleReady;
#pragma warning restore

        public void ToggleAudio(bool toggleOn)
        {
            // Not implemented.
        }
    }
}
