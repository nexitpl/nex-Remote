﻿using nexRemote.Desktop.Core;
using nexRemote.Desktop.Core.Services;
using nexRemote.Shared.Enums;
using nexRemote.Shared.Models;
using nexRemote.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;

namespace nexRemote.Desktop.Win.Services
{
    public interface IClickOnceService
    {
        string GetActivationUri();
        Task TrySetBrandingFromActivationUri();
    }
    public class ClickOnceService : IClickOnceService
    {
        private static string _activationUri;
        private readonly IDeviceInitService _deviceInitService;
        private readonly Conductor _conductor;

        public ClickOnceService(Conductor conductor, IDeviceInitService deviceInitService)
        {
            _conductor = conductor;
            _deviceInitService = deviceInitService;
        }

        public string GetActivationUri()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_activationUri))
                {
                    return _activationUri;
                }
                var appRoot = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Parent;
                if (!Directory.Exists(Path.Combine(appRoot.FullName, "manifests")))
                {
                    Logger.Write($"Nie znaleziono folderu manifestów w folderze głównym: {appRoot}", EventType.Debug);
                    return _activationUri;
                }

                var manifestFiles = appRoot.GetFiles("manifests\\*tion_*.manifest", SearchOption.AllDirectories);
                var manifestFile = manifestFiles.FirstOrDefault();
                if (manifestFile is null)
                {
                    Logger.Write($"Nie znaleziono pliku manifestu.", EventType.Warning);
                    return _activationUri;
                }

                var manifest = new XmlDocument();
                manifest.Load(manifestFile.FullName);
                var node = manifest.GetElementsByTagName("deploymentProvider")[0];
                _activationUri = node.Attributes["codebase"].Value;

                Logger.Write($"Znaleziono URI aktywacji: {_activationUri}");
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }

            return _activationUri;
        }

        public async Task TrySetBrandingFromActivationUri()
        {
            var activationUri = GetActivationUri();

            if (Uri.TryCreate(activationUri, UriKind.Absolute, out var result))
            {
                var host = $"{result.Scheme}://{result.Authority}";

                if (!string.IsNullOrWhiteSpace(host))
                {
                    _conductor.UpdateHost(host);
                    using var httpClient = new HttpClient();
                    try
                    {
                        var url = $"{host.TrimEnd('/')}/api/branding";
                        var query = HttpUtility.ParseQueryString(result.Query);
                        if (query?.AllKeys?.Contains("organizationid") == true)
                        {
                            url += $"?organizationId={query["organizationid"]}";
                            _conductor.UpdateOrganizationId(query["organizationid"]);
                        }
                        var branding = await httpClient.GetFromJsonAsync<BrandingInfo>(url).ConfigureAwait(false);
                        if (branding != null)
                        {
                            _deviceInitService.SetBrandingInfo(branding);
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
