using nexRemote.Shared.Utilities;
using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace nexRemote.Agent.Services
{
    partial class WindowsService : ServiceBase
    {
        public WindowsService()
        {
            CanHandleSessionChangeEvent = true;
            InitializeComponent();
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            try
            {
                Logger.Write($"Zmieniono sesję.  Powód: {changeDescription.Reason}.  Sesja: {changeDescription.SessionId}");
                if (changeDescription.Reason == SessionChangeReason.ConsoleDisconnect ||
                   changeDescription.Reason == SessionChangeReason.RemoteDisconnect)
                {

                    foreach (var screenCaster in Process.GetProcessesByName("nexRemote_Desktop"))
                    {
                        if (screenCaster.SessionId == changeDescription.SessionId)
                        {
                            Logger.Write($"Zmieniono sesję.  Zakończ proces o ID {screenCaster.Id}.");
                            screenCaster.Kill();
                        }
                    }
                }
                base.OnSessionChange(changeDescription);
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
        }

        protected override void OnStart(string[] args)
        {

            try
            {
                // Set Secure Attention Sequence policy to allow app to simulate Ctrl + Alt + Del.
                var subkey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
                subkey.SetValue("SoftwareSASGeneration", "3", Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
        }

        protected override void OnStop()
        {
            try
            {
                // Remove Secure Attention Sequence policy to allow app to simulate Ctrl + Alt + Del.
                var subkey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
                if (subkey.GetValue("SoftwareSASGeneration") != null)
                {
                    subkey.DeleteValue("SoftwareSASGeneration");
                }
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
        }
    }
}
