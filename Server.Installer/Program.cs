using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using nexRemote.Shared.Enums;
using nexRemote.Shared.Services;
using nexRemote.Shared.Utilities;
using Server.Installer.Models;
using Server.Installer.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Server.Installer
{

    public class Program
    {
        public static IServiceProvider Services { get; set; }

        public static async Task Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(AppConstants.nexRemoteAscii);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("(https://nex-it.pl)");
            Console.WriteLine();
            Console.WriteLine();


            if (!ParseCliParams(args, out var cliParams))
            {
                ShowHelpText();
                return;
            }

            if (EnvironmentHelper.Platform != Platform.Windows &&
                EnvironmentHelper.Platform != Platform.Linux)
            {
                ConsoleHelper.WriteError("nexRemote server można zainstalować tylko w systemie Linux lub Windows.");
                return;
            }

            BuildServices();

            var elevationDetector = Services.GetRequiredService<IElevationDetector>();
            var serverInstaller = Services.GetRequiredService<IServerInstaller>();
            var githubApi = Services.GetRequiredService<IGitHubApi>();

            if (!elevationDetector.IsElevated())
            {
                ConsoleHelper.WriteError("Proces instalatora musi być uruchomiony jako root. W systemie Linux uruchom sudo.  " +
                    "W systemie Windows uruchom z wiersza poleceń, który został otwarty za pomocą \"Uruchom jako administrator\".");
                return;
            }

            ConsoleHelper.WriteLine("Usługi Informatyczne nex-IT Jakub Potoczny.  Ten instalator poprowadzi Cię " +
                "poprzez wdrożenie serwera nexRemote na tym komputerze.");


            while (cliParams.UsePrebuiltPackage is null)
            {
                var usePrebuiltPackage = ConsoleHelper.ReadLine("Pobierz gotowy pakiet (yes/no)?",
                    subprompt: "Jeśli nie, dostosowany pakiet serwera zostanie utworzony za pomocą akcji GitHub.");

                if (ConsoleHelper.TryParseBoolLike(usePrebuiltPackage, out var result))
                {
                    cliParams.UsePrebuiltPackage = result;
                }
            }

            while (string.IsNullOrWhiteSpace(cliParams.InstallDirectory))
            {
                cliParams.InstallDirectory = ConsoleHelper.ReadLine("Do którego katalogu należy rozpakować pliki serwera (np. /var/www/nexRemote/)?").Trim();
            }

            while (cliParams.ServerUrl is null)
            {
                var url = ConsoleHelper.ReadLine("Jaki jest publiczny adres URL Twojego serwera (np. https://remote.nex-it.pl)?").Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var serverUrl))
                {
                    cliParams.ServerUrl = serverUrl;
                }
            }

            while (cliParams.WebServer is null)
            {
                var webServerType = ConsoleHelper.GetSelection("Który serwer WWW będzie używany?",
                    "Caddy na Ubuntu",
                    "Nginx na Ubuntu",
                    "Caddy na CentOS",
                    "Nginx na CentOS",
                    "IIS na Windows Server 2016+");

                if (Enum.TryParse<WebServerType>(webServerType, out var result))
                {
                    cliParams.WebServer = result;
                }
            }


            if (cliParams.UsePrebuiltPackage == false)
            {
                while (string.IsNullOrWhiteSpace(cliParams.GitHubUsername))
                {
                    cliParams.GitHubUsername = ConsoleHelper.ReadLine("Jaka jest Twoja nazwa użytkownika GitHub?").Trim();
                }

                while (string.IsNullOrWhiteSpace(cliParams.GitHubPat))
                {
                    cliParams.GitHubPat = ConsoleHelper.ReadLine("Jakiego osobistego tokena dostępu GitHub należy użyć?").Trim();
                }

                while (cliParams.CreateNew is null)
                {
                    var createNew = ConsoleHelper.ReadLine("Utworzyć nową kompilację (yes/no)?", 
                        subprompt: "Jeśli nie, zostanie użyty najnowszy istniejący artefakt kompilacji w serwisie GitHub.");

                    if (ConsoleHelper.TryParseBoolLike(createNew, out var result))
                    {
                        cliParams.CreateNew = result;
                    }
                }

                if (cliParams.CreateNew == true)
                {
                    if (cliParams.Reference?.Contains("latest", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        cliParams.Reference = await githubApi.GetLatestReleaseTag();
                    }

                    while (string.IsNullOrWhiteSpace(cliParams.Reference))
                    {
                        var selection = ConsoleHelper.GetSelection("Którą wersję chciałbyś zbudować?",
                            "Najnowsze oficjalne wydanie",
                            "Podgląd zmian (np. master branch)",
                            "Specific release");

                        if (int.TryParse(selection, out var result))
                        {
                            switch (result)
                            {
                                case 0:
                                    cliParams.Reference = await githubApi.GetLatestReleaseTag();
                                    break;
                                case 1:
                                    cliParams.Reference = "master";
                                    break;
                                case 2:
                                    ConsoleHelper.WriteLine("Wprowadź gałąź GitHub lub nazwę tagu, z którego chcesz kompilować " +
                                        "(np. \"master\" lub \"v2021.04.25.0953\").");
                                    cliParams.Reference = ConsoleHelper.ReadLine("Input Reference").Trim();
                                    break;
                                default:
                                    break;
                            }
                        }

                     
                    }

                }

                ConsoleHelper.WriteLine($"Wykonywanie instalacji serwera.  Użytkownik GitHub: {cliParams.GitHubUsername}.  " +
                    $"Serwer URL: {cliParams.ServerUrl}.  Katalog instalacyjny: {cliParams.InstallDirectory}.  " +
                    $"Serwer WWW: {cliParams.WebServer}.  Utwórz nową kompilację: {cliParams.CreateNew}.  " +
                    $"Odniesienie do Gita: {cliParams.Reference}");
            }
            else
            {
                ConsoleHelper.WriteLine($"Serwer URL: {cliParams.ServerUrl}.  " +
                    $"Katalog instalacyjny: {cliParams.InstallDirectory}. Serwer WWW: {cliParams.WebServer}.");
            }

            await serverInstaller.PerformInstall(cliParams);

            ConsoleHelper.WriteLine("Instalacja zakończona.");
        }

        private static void BuildServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<IGitHubApi, GitHubApi>();
            services.AddSingleton<IServerInstaller, ServerInstaller>();

            if (EnvironmentHelper.IsWindows)
            {
                services.AddSingleton<IElevationDetector, ElevationDetectorWin>();
            }
            else if (EnvironmentHelper.IsLinux)
            {
                services.AddSingleton<IElevationDetector, ElevationDetectorLinux>();
            }
            else
            {
                throw new NotSupportedException("System operacyjny nie jest obsługiwany.");
            }

            Services = services.BuildServiceProvider();
        }

        private static bool ParseCliParams(string[] args, out CliParams cliParams)
        {
            cliParams = new CliParams();

            for (var i = 0; i < args.Length; i += 2)
            {
                try
                {
                    var key = args[i].Trim();

                    if (i == args.Length - 1)
                    {
                        ConsoleHelper.WriteError("W argumencie brakuje wartości.");
                        return false;
                    }

                    var value = args[i + 1].Trim();

                    switch (key)
                    {
                        case "--help":
                        case "-h":
                        case "/h":
                            ShowHelpText();
                            Environment.Exit(0);
                            break;
                        case "--use-prebuilt":
                        case "-b":
                            {
                                if (bool.TryParse(value, out var result))
                                {
                                    cliParams.UsePrebuiltPackage = result;
                                    continue;
                                }
                                ConsoleHelper.WriteError("--use-prebuilt parameter is invalid.  Must be true/yes or false/no.");
                                return false;
                            }
                        case "--web-server":
                        case "-w":
                            {
                                if (int.TryParse(value, out var webServerResult))
                                {
                                    cliParams.WebServer = (WebServerType)webServerResult;
                                    continue;
                                }
                                ConsoleHelper.WriteError($"--web-server parameter is invalid.  Must be a " +
                                    $"number (0 - {Enum.GetValues<WebServerType>().Length}).");
                                return false;
                            }
                        case "--server-url":
                        case "-s":
                            {
                                if (Uri.TryCreate(value, UriKind.Absolute, out var result))
                                {
                                    cliParams.ServerUrl = result;
                                    continue;
                                }
                                ConsoleHelper.WriteError("--server-url parameter is invalid.  Must be a valid URL (e.g. https://remote.nex-it.pl).");
                                return false;
                            }
                        case "--install-directory":
                        case "-i":
                            cliParams.InstallDirectory = value;
                            continue;
                        case "--github-username":
                        case "-u":
                            cliParams.GitHubUsername = value;
                            continue;
                        case "--github-pat":
                        case "-p":
                            cliParams.GitHubPat = value;
                            continue;
                        case "--reference":
                        case "-r":
                            cliParams.Reference = value;
                            continue;
                        case "--create-new":
                        case "-c":
                            {
                                if (bool.TryParse(value, out var result))
                                {
                                    cliParams.CreateNew = result;
                                    continue;
                                }
                                ConsoleHelper.WriteError("--create-new parameter is invalid.  Must be true/yes or false/no.");
                                return false;
                            }
                        default:
                            return false;
                    }
                }
                catch (Exception ex)
                {
                    ConsoleHelper.WriteError($"Error while parsing command line arguments: {ex.Message}");
                    return false;
                }

            }
            return true;
        }

        private static void ShowHelpText()
        {
            ConsoleHelper.WriteLine("nexRemote Server Installer", 0, ConsoleColor.Cyan);
            ConsoleHelper.WriteLine("Builds a customized nexRemote server using GitHub actions " +
                "and installs the server on the local machine.", 1);

            ConsoleHelper.WriteLine("Usage:");

            ConsoleHelper.WriteLine("\tNo Parameters - Run the installer interactively.", 2);

            ConsoleHelper.WriteLine("\t--use-prebuilt, -b    (true/false or yes/no)  Whether to use the pre-built server package from the " +
                "latest public release, or to create a customized package through GitHub Actions.  The pre-built package " +
                "will not contain your server's URL in the desktop clients, and end users will need to type it in manually.", 1);

            ConsoleHelper.WriteLine("\t--github-username, -u    Your GitHub username, where the forked nexRemote repo exists.", 1);
            
            ConsoleHelper.WriteLine("\t--github-pat, -p    The GitHub Personal Access Token to use for authentication.  " +
                "Create one at ttps://github.com/settings/tokens.", 1);

            ConsoleHelper.WriteLine("\t--server-url, -s    The public URL where your nexRemote server will be accessed (e.g. https://remote.nex-it.pl).", 1);

            ConsoleHelper.WriteLine("\t--install-directory, -i    The directory path where the server files will be installed (e.g. /var/www/nexRemote/).", 1);
            
            ConsoleHelper.WriteLine("Enter the GitHub branch or tag name from which to build.  For example, you can enter " +
                  " \"master\" to build the latest changes from the default branch.  Or you can enter a release tag like \"v2021.04.13.1604\".", 1);
            
            ConsoleHelper.WriteLine("\t--reference, -r    Use \"latest\" to build from the latest full release.  Otherwise, you can enter the " +
                "name of a branch or tag from which to build.  For example, you can enter \"master\" to build the most recent preview changes " +
                "from the default branch.  Or you can enter a specific release tag like \"v2021.04.13.1604\".", 1);
            
            ConsoleHelper.WriteLine("\t--create-new, -c    (true/false or yes/no)  Whether to run a new build.  If false, the latest existing build artifact will be used.", 1);
            
            ConsoleHelper.WriteLine("\t--web-server, -w    Number.  The web server that will be used as a reverse proxy to forward " +
                "requests to the nexRemote server.  Select the appropriate option for your operating system and web server.  " +
                "0 = Caddy on Ubuntu.  1 = Nginx on Ubuntu.  2 = Caddy on CentOS.  3 = Nginx on CentOS.  4 = IIS on Windows Server 2016+.", 1);

            ConsoleHelper.WriteLine("Example (build latest release):");
            ConsoleHelper.WriteLine("sudo ./nexRemote_Server_Installer -b false -u nexitpl -p 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 -s https://remote.nex-it.pl -i /var/www/nexRemote/ -r latest -c true -w 0", 1);

            ConsoleHelper.WriteLine("Example (use pre-built package):");
            ConsoleHelper.WriteLine("sudo ./nexRemote_Server_Installer -b true -s https://remote.nex-it.pl -i /var/www/nexRemote/ -w 0");
        }
    }
}
