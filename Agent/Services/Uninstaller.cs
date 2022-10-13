using Microsoft.Win32;
using nexRemote.Shared.Utilities;
using System;
using System.Diagnostics;
using System.IO;

namespace nexRemote.Agent.Services
{
    public class Uninstaller
    {
        public void UninstallAgent()
        {
            if (EnvironmentHelper.IsWindows)
            {
                Process.Start("cmd.exe", "/c sc delete nexRemote_Service");

                var view = Environment.Is64BitOperatingSystem ?
                    "/reg:64" :
                    "/reg:32";

                Process.Start("cmd.exe", @$"/c REG DELETE HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\nexRemote /f {view}");

                var currentDir = Path.GetDirectoryName(typeof(Uninstaller).Assembly.Location);
                Process.Start("cmd.exe", $"/c timeout 5 & rd /s /q \"{currentDir}\"");
            }
            else if (EnvironmentHelper.IsLinux)
            {
                Process.Start("sudo", "systemctl stop nexRemote-agent").WaitForExit();
                Directory.Delete("/usr/local/bin/nexRemote", true);
                File.Delete("/etc/systemd/system/nexRemote-agent.service");
                Process.Start("sudo", "systemctl daemon-reload").WaitForExit();
            }
            Environment.Exit(0);
        }
    }
}
