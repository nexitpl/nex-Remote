using IWshRuntimeLibrary;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using nexRemote.Agent.Installer.Win.Utilities;
using nexRemote.Shared.Models;
using System;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using FileIO = System.IO.File;

namespace nexRemote.Agent.Installer.Win.Services
{
    public class InstallerService
    {
        public event EventHandler<string> ProgressMessageChanged;
        public event EventHandler<int> ProgressValueChanged;

        private string InstallPath => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Program Files", "nexRemote");
        private string Platform => Environment.Is64BitOperatingSystem ? "x64" : "x86";
        private JavaScriptSerializer Serializer { get; } = new JavaScriptSerializer();
        public async Task<bool> Install(string serverUrl,
            string organizationId,
            string deviceGroup,
            string deviceAlias,
            string deviceUuid,
            bool createSupportShortcut)
        {
            try
            {
                Logger.Write("Instalacja rozpoczęta.");
                if (!CheckIsAdministrator())
                {
                    return false;
                }

                StopService();

                await StopProcesses();

                BackupDirectory();

                var connectionInfo = GetConnectionInfo(organizationId, serverUrl, deviceUuid);

                ClearInstallDirectory();

                await DownloadnexRemoteAgent(serverUrl);

                FileIO.WriteAllText(Path.Combine(InstallPath, "ConnectionInfo.json"), Serializer.Serialize(connectionInfo));

                FileIO.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(InstallPath, "nexRemote_Installer.exe"));

                await CreateDeviceOnServer(connectionInfo.DeviceID, serverUrl, deviceGroup, deviceAlias, organizationId);

                AddFirewallRule();

                InstallService();

                CreateUninstallKey();

                CreateSupportShortcut(serverUrl, connectionInfo.DeviceID, createSupportShortcut);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
                RestoreBackup();
                return false;
            }

        }

        public async Task<bool> Uninstall()
        {
            try
            {
                if (!CheckIsAdministrator())
                {
                    return false;
                }

                StopService();

                ProcessEx.StartHidden("cmd.exe", "/c sc delete nexRemote_Service").WaitForExit();

                await StopProcesses();

                ProgressMessageChanged?.Invoke(this, "Usuwanie plików.");
                ClearInstallDirectory();
                ProcessEx.StartHidden("cmd.exe", $"/c timeout 5 & rd /s /q \"{InstallPath}\"");

                ProcessEx.StartHidden("netsh", "advfirewall firewall delete rule name=\"nexRemote Desktop Unattended\"").WaitForExit();

                GetRegistryBaseKey().DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\nexRemote", false);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
                return false;
            }
        }

        private void AddFirewallRule()
        {
            var desktopExePath = Path.Combine(InstallPath, "Desktop", "nexRemote_Desktop.exe");
            ProcessEx.StartHidden("netsh", "advfirewall firewall delete rule name=\"nexRemote Desktop Unattended\"").WaitForExit();
            ProcessEx.StartHidden("netsh", $"advfirewall firewall add rule name=\"nexRemote Desktop Unattended\" program=\"{desktopExePath}\" protocol=any dir=in enable=yes action=allow description=\"Agent, który umożliwia udostępnianie ekranu i zdalne sterowanie dla nexRemote.\"").WaitForExit();
        }

        private void BackupDirectory()
        {
            if (Directory.Exists(InstallPath))
            {
                Logger.Write("Tworzenie kopii zapasowej bieżącej instalacji.");
                ProgressMessageChanged?.Invoke(this, "Tworzenie kopii zapasowej bieżącej instalacji.");
                var backupPath = Path.Combine(Path.GetTempPath(), "nexRemote_Backup.zip");
                if (FileIO.Exists(backupPath))
                {
                    FileIO.Delete(backupPath);
                }
                ZipFile.CreateFromDirectory(InstallPath, backupPath, CompressionLevel.Fastest, false);
            }
        }

        private bool CheckIsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var result = principal.IsInRole(WindowsBuiltInRole.Administrator);
            if (!result)
            {
                MessageBoxEx.Show("Wymagane są podwyższone uprawnienia.  Uruchom ponownie instalatora, używając opcji 'Uruchom jako administrator'.", "Wymagane uprawnienia administratora", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return result;
        }

        private void ClearInstallDirectory()
        {
            if (Directory.Exists(InstallPath))
            {
                foreach (var entry in Directory.GetFileSystemEntries(InstallPath))
                {
                    try
                    {
                        if (FileIO.Exists(entry))
                        {
                            FileIO.Delete(entry);
                        }
                        else if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(ex);
                    }
                }
            }
        }

        private async Task CreateDeviceOnServer(string deviceUuid,
            string serverUrl,
            string deviceGroup,
            string deviceAlias,
            string organizationId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(deviceGroup) ||
                    !string.IsNullOrWhiteSpace(deviceAlias))
                {
                    var setupOptions = new DeviceSetupOptions()
                    {
                        DeviceID = deviceUuid,
                        DeviceGroupName = deviceGroup,
                        DeviceAlias = deviceAlias,
                        OrganizationID = organizationId
                    };

                    var wr = WebRequest.CreateHttp(serverUrl.TrimEnd('/') + "/api/devices");
                    wr.Method = "POST";
                    wr.ContentType = "application/json";
                    using (var rs = await wr.GetRequestStreamAsync())
                    using (var sw = new StreamWriter(rs))
                    {
                        await sw.WriteAsync(Serializer.Serialize(setupOptions));
                    }
                    using (var response = await wr.GetResponseAsync() as HttpWebResponse)
                    { 
                        Logger.Write($"Utwórz odpowiedź urządzenia: {response.StatusCode}");
                    }
                }
            }
            catch (WebException ex) when ((ex.Response is HttpWebResponse response) && response.StatusCode == HttpStatusCode.BadRequest)
            {
                Logger.Write("Nieprawidłowe żądanie podczas tworzenia urządzenia. ID urządzenia może już zostać utworzony.");
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }

        }

        private void CreateSupportShortcut(string serverUrl, string deviceUuid, bool createSupportShortcut)
        {
            var shell = new WshShell();
            var shortcutLocation = Path.Combine(InstallPath, "nexRemote Uzyskaj Wsparcie.lnk");
            var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutLocation);
            shortcut.Description = "nexRemote Wsparcie";
            shortcut.IconLocation = Path.Combine(InstallPath, "nexRemote_Agent.exe");
            shortcut.TargetPath = serverUrl.TrimEnd('/') + $"/GetSupport?deviceID={deviceUuid}";
            shortcut.Save();

            if (createSupportShortcut)
            {
                var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
                var publicDesktop = Path.Combine(systemRoot, "Users", "Public", "Desktop", "nexRemote Uzyskaj Wsparcie.lnk");
                FileIO.Copy(shortcutLocation, publicDesktop, true);
            }
        }
        private void CreateUninstallKey()
        {
            var version = FileVersionInfo.GetVersionInfo(Path.Combine(InstallPath, "nexRemote_Agent.exe"));
            var baseKey = GetRegistryBaseKey();

            var nexRemoteKey = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\nexRemote", true);
            nexRemoteKey.SetValue("DisplayIcon", Path.Combine(InstallPath, "nexRemote_Agent.exe"));
            nexRemoteKey.SetValue("DisplayName", "nexRemote");
            nexRemoteKey.SetValue("DisplayVersion", version.FileVersion);
            nexRemoteKey.SetValue("InstallDate", DateTime.Now.ToShortDateString());
            nexRemoteKey.SetValue("Publisher", "nex-IT Jakub Potoczny");
            nexRemoteKey.SetValue("VersionMajor", version.FileMajorPart.ToString(), RegistryValueKind.DWord);
            nexRemoteKey.SetValue("VersionMinor", version.FileMinorPart.ToString(), RegistryValueKind.DWord);
            nexRemoteKey.SetValue("UninstallString", Path.Combine(InstallPath, "nexRemote_Installer.exe -uninstall -quiet"));
            nexRemoteKey.SetValue("QuietUninstallString", Path.Combine(InstallPath, "nexRemote_Installer.exe -uninstall -quiet"));
        }

        private async Task DownloadnexRemoteAgent(string serverUrl)
        {
            var targetFile = Path.Combine(Path.GetTempPath(), $"nexRemote-Agent.zip");

            if (CommandLineParser.CommandLineArgs.TryGetValue("path", out var result) &&
                FileIO.Exists(result))
            {
                targetFile = result;
            }
            else
            {
                ProgressMessageChanged.Invoke(this, "Pobieranie nexRemote.");
                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (sender, args) =>
                    {
                        ProgressValueChanged?.Invoke(this, args.ProgressPercentage);
                    };

                    await client.DownloadFileTaskAsync($"{serverUrl}/Content/nexRemote-Win10-{Platform}.zip", targetFile);
                }
            }

            ProgressMessageChanged.Invoke(this, "Rozpakowywanie nexRemote agent.");
            ProgressValueChanged?.Invoke(this, 0);

            var tempDir = Path.Combine(Path.GetTempPath(), "nexRemote_Update");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            Directory.CreateDirectory(InstallPath);
            while (!Directory.Exists(InstallPath))
            {
                await Task.Delay(10);
            }

            var wr = WebRequest.CreateHttp($"{serverUrl}/Content/nexRemote-Win10-{Platform}.zip");
            wr.Method = "Head";
            using (var response = (HttpWebResponse)await wr.GetResponseAsync())
            {
                FileIO.WriteAllText(Path.Combine(InstallPath, "etag.txt"), response.Headers["ETag"]);
            }

            ZipFile.ExtractToDirectory(targetFile, tempDir);
            var fileSystemEntries = Directory.GetFileSystemEntries(tempDir);
            for (var i = 0; i < fileSystemEntries.Length; i++)
            {
                try
                {
                    ProgressValueChanged?.Invoke(this, (int)((double)i / (double)fileSystemEntries.Length * 100d));
                    var entry = fileSystemEntries[i];
                    if (FileIO.Exists(entry))
                    {
                        FileIO.Copy(entry, Path.Combine(InstallPath, Path.GetFileName(entry)), true);
                    }
                    else if (Directory.Exists(entry))
                    {
                        FileSystem.CopyDirectory(entry, Path.Combine(InstallPath, new DirectoryInfo(entry).Name), true);
                    }
                    await Task.Delay(1);
                }
                catch (Exception ex)
                {
                    Logger.Write(ex);
                }
            }
            ProgressValueChanged?.Invoke(this, 0);
        }

        private ConnectionInfo GetConnectionInfo(string organizationId, string serverUrl, string deviceUuid)
        {
            ConnectionInfo connectionInfo;
            var connectionInfoPath = Path.Combine(InstallPath, "ConnectionInfo.json");
            if (FileIO.Exists(connectionInfoPath))
            {
                connectionInfo = Serializer.Deserialize<ConnectionInfo>(FileIO.ReadAllText(connectionInfoPath));
                connectionInfo.ServerVerificationToken = null;
            }
            else
            {
                connectionInfo = new ConnectionInfo()
                {
                    DeviceID = Guid.NewGuid().ToString()
                };
            }

            if (!string.IsNullOrWhiteSpace(deviceUuid))
            {
                // Clear the server verification token if we're installing this as a new device.
                if (connectionInfo.DeviceID != deviceUuid)
                {
                    connectionInfo.ServerVerificationToken = null;
                }
                connectionInfo.DeviceID = deviceUuid;
            }
            connectionInfo.OrganizationID = organizationId;
            connectionInfo.Host = serverUrl;
            return connectionInfo;
        }

        private RegistryKey GetRegistryBaseKey()
        {
            if (Environment.Is64BitOperatingSystem)
            {
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            }
            else
            {
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            }
        }

        private void InstallService()
        {
            Logger.Write("Instalacja usług.");
            ProgressMessageChanged?.Invoke(this, "Instalacja usług nexRemote.");
            var serv = ServiceController.GetServices().FirstOrDefault(ser => ser.ServiceName == "nexRemote_Service");
            if (serv == null)
            {
                var command = new string[] { "/assemblypath=" + Path.Combine(InstallPath, "nexRemote_Agent.exe") };
                var context = new InstallContext("", command);
                var serviceInstaller = new ServiceInstaller()
                {
                    Context = context,
                    DisplayName = "nexRemote_Service",
                    Description = "Usługa działająca w tle, która utrzymuje połączenie z serwerem nexRemote.  Usługa służy do zdalnego wsparcia systemu nexRemote - nex-IT Jakub Potoczny.",
                    ServiceName = "nexRemote_Service",
                    StartType = ServiceStartMode.Automatic,
                    DelayedAutoStart = true,
                    Parent = new ServiceProcessInstaller()
                };

                var state = new System.Collections.Specialized.ListDictionary();
                serviceInstaller.Install(state);
                Logger.Write("Usługa Zainstalowana.");
                serv = ServiceController.GetServices().FirstOrDefault(ser => ser.ServiceName == "nexRemote_Service");

                ProcessEx.StartHidden("cmd.exe", "/c sc.exe failure \"nexRemote_Service\" reset= 5 actions= restart/5000");
            }
            if (serv.Status != ServiceControllerStatus.Running)
            {
                serv.Start();
            }
            Logger.Write("Usługa uruchomiona.");
        }

        private void RestoreBackup()
        {
            try
            {
                var backupPath = Path.Combine(Path.GetTempPath(), "nexRemote_Backup.zip");
                if (FileIO.Exists(backupPath))
                {
                    Logger.Write("Odtwarzanie kopii.");
                    ClearInstallDirectory();
                    ZipFile.ExtractToDirectory(backupPath, InstallPath);
                    var serv = ServiceController.GetServices().FirstOrDefault(ser => ser.ServiceName == "nexRemote_Service");
                    if (serv?.Status != ServiceControllerStatus.Running)
                    {
                        serv?.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
        }

        private async Task StopProcesses()
        {
            ProgressMessageChanged?.Invoke(this, "Zatrzymywanie procesów nexRemote.");
            var procs = Process.GetProcessesByName("nexRemote_Agent").Concat(Process.GetProcessesByName("nexRemote_Desktop"));

            foreach (var proc in procs)
            {
                proc.Kill();
            }

            await Task.Delay(500);
        }
        private void StopService()
        {
            try
            {
                var nexRemoteService = ServiceController.GetServices().FirstOrDefault(x => x.ServiceName == "nexRemote_Service");
                if (nexRemoteService != null)
                {
                    Logger.Write("Zatrzymywanie istniejących usług nexRemote.");
                    ProgressMessageChanged?.Invoke(this, "Zatrzymywanie bieżacych usług nexRemote.");
                    nexRemoteService.Stop();
                    nexRemoteService.WaitForStatus(ServiceControllerStatus.Stopped);
                }
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
        }
    }
}
