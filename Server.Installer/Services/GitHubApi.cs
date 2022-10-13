using nexRemote.Server.Installer.Models;
using nexRemote.Shared.Utilities;
using Server.Installer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.Installer.Services
{
    public interface IGitHubApi : IDisposable
    {
        Task<bool> DownloadArtifact(CliParams cliParams, string artifactDownloadUrl, string downloadToPath);

        Task<Artifact> GetLatestBuildArtifact(CliParams cliParams);

        Task<bool> TriggerDispatch(CliParams cliParams);
        Task<string> GetLatestReleaseTag();
    }
    public class GitHubApi : IGitHubApi
    {
        private readonly string _apiHost = "https://api.github.com";
        private readonly HttpClient _httpClient;
        public GitHubApi()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromHours(8);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "nexRemote Server Installer");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<bool> DownloadArtifact(CliParams cliParams, string artifactDownloadUrl, string downloadToPath)
        {
            try
            {
                ConsoleHelper.WriteLine("Pobieranie artefaktu kompilacji.");

                var message = GetHttpRequestMessage(HttpMethod.Get, artifactDownloadUrl, cliParams);

                var response = await _httpClient.SendAsync(message);

                ConsoleHelper.WriteLine($"Pobierz kod stanu odpowiedzi artefaktu: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError("Wywołanie interfejsu API GitHub w celu pobrania artefaktu kompilacji nie powiodło się.  Sprawdź swoje parametry wejściowe.");
                    Environment.Exit(1);
                }

                using var responseStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(downloadToPath, FileMode.Create);

                await responseStream.CopyToAsync(fileStream);
                return true;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Błąd podczas pobierania artefaktu.  Message: {ex.Message}");
                return false;
            }
        }

        public async Task<Artifact> GetLatestBuildArtifact(CliParams cliParams)
        {
            try
            {
                var message = GetHttpRequestMessage(HttpMethod.Get, 
                    $"{_apiHost}/repos/{cliParams.GitHubUsername}/nexRemote/actions/artifacts",
                    cliParams);

                var response = await _httpClient.SendAsync(message);

                ConsoleHelper.WriteLine($"Uzyskaj kod stanu odpowiedzi na artefakty: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError("Wywołanie interfejsu API GitHub w celu pobrania artefaktów kompilacji nie powiodło się.  Sprawdź swoje parametry wejściowe.");
                    Environment.Exit(1);
                }

                var payload = await response.Content.ReadFromJsonAsync<ArtifactsResponsePayload>();
                if (payload?.artifacts?.Any() != true)
                {
                    return null;
                }

                return payload.artifacts
                    .OrderByDescending(x => x.created_at)
                    .FirstOrDefault(x=>x.name.Equals("Server", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError("Błąd podczas próby pobrania artefaktów kompilacji." +
                    $"Error: {ex.Message}");
                Environment.Exit(1);
            }

            return null;
        }

        public async Task<string> GetLatestReleaseTag()
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<GitHubReleasesResponsePayload>("https://api.github.com/repos/nexitpl/nexRemote/releases/latest");
                return response.tag_name;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError("Błąd podczas próby pobrania informacji o wydaniu." +
                  $"Error: {ex.Message}");
                Environment.Exit(1);
            }
            return string.Empty;
        }

        public async Task<bool> TriggerDispatch(CliParams cliParams)
        {
            try
            {
                ConsoleHelper.WriteLine("Uruchom kompilację akcji GitHub.");


                var message = GetHttpRequestMessage(
                    HttpMethod.Post, 
                    $"{_apiHost}/repos/{cliParams.GitHubUsername}/nexRemote/actions/workflows/build.yml/dispatches",
                    cliParams);

                var rid = EnvironmentHelper.IsLinux ?
                    "linux-x64" :
                    "win-x64";

                var body = new
                {
                    @ref = cliParams.Reference,
                    inputs = new
                    {
                        serverUrl = cliParams.ServerUrl.ToString(),
                        rid = rid
                    }
                };
                message.Content = new StringContent(JsonSerializer.Serialize(body));

                var response = await _httpClient.SendAsync(message);

                ConsoleHelper.WriteLine($"Kod statusu odpowiedzi na wysyłkę: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Error: {ex.Message}");
            }
            return false;
        }

        private HttpRequestMessage GetHttpRequestMessage(HttpMethod method, string url, CliParams cliParams)
        {
            var message = new HttpRequestMessage(method, url);

            var base64Auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cliParams.GitHubUsername}:{cliParams.GitHubPat}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            return message;
        }
    }
}
