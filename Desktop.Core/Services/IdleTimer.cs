﻿using nexRemote.Shared.Utilities;
using System;
using System.Collections.Concurrent;
using System.Timers;

namespace nexRemote.Desktop.Core.Services
{
    public class IdleTimer
    {
        public IdleTimer(Conductor conductor)
        {
            ViewerList = conductor.Viewers;
        }

        public ConcurrentDictionary<string, Services.Viewer> ViewerList { get; }

        public DateTimeOffset ViewersLastSeen { get; private set; } = DateTimeOffset.Now;

        private Timer Timer { get; set; }

        public void Start()
        {
            Timer?.Dispose();
            Timer = new Timer(100);
            Timer.Elapsed += Timer_Elapsed;
            Timer.Start();
        }

        public void Stop()
        {
            Timer?.Stop();
            Timer?.Dispose();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!ViewerList.IsEmpty)
            {
                ViewersLastSeen = DateTimeOffset.Now;
            }
            else if (DateTimeOffset.Now - ViewersLastSeen > TimeSpan.FromSeconds(30))
            {
                Logger.Write("Brak widzów połączonych po 30 sekundach.  Wyłączanie.");
                Environment.Exit(0);
            }
        }
    }
}
