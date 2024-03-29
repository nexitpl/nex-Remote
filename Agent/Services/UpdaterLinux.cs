﻿using nexRemote.Agent.Interfaces;
using nexRemote.Agent.Services;
using nexRemote.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nexRemote.Agent.Services
{

    public class UpdaterLinux : IUpdater
    {
        private readonly SemaphoreSlim _checkForUpdatesLock = new(1, 1);
        private readonly ConfigService _configService;
        private readonly IUpdateDownloader _updateDownloader;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SemaphoreSlim _installLatestVersionLock = new(1, 1);
        private readonly System.Timers.Timer _updateTimer = new(TimeSpan.FromHours(6).TotalMilliseconds);
        private DateTimeOffset _lastUpdateFailure;

        public UpdaterLinux(ConfigService configService, UpdateDownloader updateDownloader, IHttpClientFactory httpClientFactory)
        {
            _configService = configService;
            _updateDownloader = updateDownloader;
            _httpClientFactory = httpClientFactory;
        }


        public async Task BeginChecking()
        {
            if (EnvironmentHelper.IsDebug)
            {
                return;
            }

            await CheckForUpdates();
            _updateTimer.Elapsed += UpdateTimer_Elapsed;
            _updateTimer.Start();
        }

        public async Task CheckForUpdates()
        {
            if (!await _checkForUpdatesLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                if (EnvironmentHelper.IsDebug)
                {
                    return;
                }

                if (_lastUpdateFailure.AddDays(1) > DateTimeOffset.Now)
                {
                    Logger.Write("Skipping update check due to previous failure.  Restart the service to try again, or manually install the update.");
                    return;
                }


                var connectionInfo = _configService.GetConnectionInfo();
                var serverUrl = _configService.GetConnectionInfo().Host;

                var fileUrl = serverUrl + $"/Content/nexRemote-Linux.zip";

                using var httpClient = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Head, fileUrl);

                if (File.Exists("etag.txt"))
                {
                    var lastEtag = await File.ReadAllTextAsync("etag.txt");
                    if (!string.IsNullOrWhiteSpace(lastEtag) &&
                       EntityTagHeaderValue.TryParse(lastEtag.Trim(), out var etag))
                    {
                        request.Headers.IfNoneMatch.Add(etag);
                    }
                }

                using var response = await httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    Logger.Write("Service Updater: Version is current.");
                    return;
                }

                Logger.Write("Service Updater: Update found.");

                await InstallLatestVersion();

            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse).StatusCode == HttpStatusCode.NotModified)
            {
                Logger.Write("Service Updater: Version is current.");
                return;
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
            finally
            {
                _checkForUpdatesLock.Release();
            }
        }

        public async Task InstallLatestVersion()
        {
            try
            {
                await _installLatestVersionLock.WaitAsync();

                var connectionInfo = _configService.GetConnectionInfo();
                var serverUrl = connectionInfo.Host;

                Logger.Write("Service Updater: Downloading install package.");

                var downloadId = Guid.NewGuid().ToString();
                var zipPath = Path.Combine(Path.GetTempPath(), "nexRemoteUpdate.zip");

                var installerPath = Path.Combine(Path.GetTempPath(), "nexRemoteUpdate.sh");

                string platform;

                if (RuntimeInformation.OSDescription.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase))
                {
                    platform = "UbuntuInstaller-x64";
                }
                else if (RuntimeInformation.OSDescription.Contains("Manjaro", StringComparison.OrdinalIgnoreCase))
                {
                    platform = "ManjaroInstaller-x64";
                }
                else
                {
                    throw new PlatformNotSupportedException();
                }

                await _updateDownloader.DownloadFile(
                       $"{serverUrl}/API/ClientDownloads/{connectionInfo.OrganizationID}/{platform}",
                       installerPath);

                await _updateDownloader.DownloadFile(
                   $"{serverUrl}/API/AgentUpdate/DownloadPackage/linux/{downloadId}",
                   zipPath);

                using var httpClient = _httpClientFactory.CreateClient();
                using var response = httpClient.GetAsync($"{serverUrl}/api/AgentUpdate/ClearDownload/{downloadId}");

                Logger.Write("Launching installer to perform update.");

                Process.Start("sudo", $"chmod +x {installerPath}").WaitForExit();

                Process.Start("sudo", $"{installerPath} --path {zipPath}");
            }
            catch (WebException ex) when (ex.Status == WebExceptionStatus.Timeout)
            {
                Logger.Write("Timed out while waiting to download update.", Shared.Enums.EventType.Warning);
                _lastUpdateFailure = DateTimeOffset.Now;
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
                _lastUpdateFailure = DateTimeOffset.Now;
            }
            finally
            {
                _installLatestVersionLock.Release();
            }
        }

        private async void UpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            await CheckForUpdates();
        }

    }
}
