﻿using nexRemote.Agent.Interfaces;
using nexRemote.Shared.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace nexRemote.Agent.Services
{
    public class UpdaterWin : IUpdater
    {
        private readonly SemaphoreSlim _checkForUpdatesLock = new(1, 1);
        private readonly ConfigService _configService;
        private readonly IUpdateDownloader _updateDownloader;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SemaphoreSlim _installLatestVersionLock = new(1, 1);
        private readonly System.Timers.Timer _updateTimer = new(TimeSpan.FromHours(6).TotalMilliseconds);
        private DateTimeOffset _lastUpdateFailure;


        public UpdaterWin(ConfigService configService, IUpdateDownloader updateDownloader, IHttpClientFactory httpClientFactory)
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
                    Logger.Write("Skipping update check due to previous failure.  Updating will be tried again after 24 hours have passed.");
                    return;
                }


                var connectionInfo = _configService.GetConnectionInfo();
                var serverUrl = _configService.GetConnectionInfo().Host;

                var platform = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                var fileUrl = serverUrl + $"/Content/nexRemote-Win10-{platform}.zip";

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

                var installerPath = Path.Combine(Path.GetTempPath(), "nexRemote_Installer.exe");
                var platform = Environment.Is64BitOperatingSystem ? "x64" : "x86";

                await _updateDownloader.DownloadFile(
                     $"{serverUrl}/Content/nexRemote_Installer.exe",
                     installerPath);

                await _updateDownloader.DownloadFile(
                   $"{serverUrl}/api/AgentUpdate/DownloadPackage/win-{platform}/{downloadId}",
                   zipPath);

                using var httpClient = _httpClientFactory.CreateClient();
                using var response = httpClient.GetAsync($"{serverUrl}/api/AgentUpdate/ClearDownload/{downloadId}");

                foreach (var proc in Process.GetProcessesByName("nexRemote_Installer"))
                {
                    proc.Kill();
                }

                Logger.Write("Launching installer to perform update.");

                Process.Start(installerPath, $"-install -quiet -path {zipPath} -serverurl {serverUrl} -organizationid {connectionInfo.OrganizationID}");
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
