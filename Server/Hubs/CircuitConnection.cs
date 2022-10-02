using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using nexRemote.Server.Auth;
using nexRemote.Server.Models;
using nexRemote.Server.Services;
using nexRemote.Shared.Enums;
using nexRemote.Shared.Models;
using nexRemote.Shared.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nexRemote.Server.Hubs
{
    public interface ICircuitConnection
    {
        event EventHandler<CircuitEvent> MessageReceived;
        nexRemoteUser User { get; }

        Task DeleteRemoteLogs(string deviceId);

        Task ExecuteCommandOnAgent(ScriptingShell shell, string command, string[] deviceIDs);

        Task GetPowerShellCompletions(string inputText, int currentIndex, CompletionIntent intent, bool? forward);

        Task GetRemoteLogs(string deviceId);

        Task InvokeCircuitEvent(CircuitEventName eventName, params object[] args);
        Task ReinstallAgents(string[] deviceIDs);

        Task<bool> RemoteControl(string deviceID);

        Task RemoveDevices(string[] deviceIDs);

        Task RunScript(IEnumerable<string> deviceIds, Guid savedScriptId, int scriptRunId, ScriptInputType scriptInputType, bool runAsHostedService);

        Task SendChat(string message, string deviceId);
        Task<bool> TransferFileFromBrowserToAgent(string deviceId, string transferId, string[] fileIds);

        Task TriggerHeartbeat(string deviceId);

        Task UninstallAgents(string[] deviceIDs);
        Task UpdateTags(string deviceID, string tags);
        Task UploadFiles(List<string> fileIDs, string transferID, string[] deviceIDs);
    }

    public class CircuitConnection : CircuitHandler, ICircuitConnection
    {
        private readonly IHubContext<AgentHub> _agentHubContext;
        private readonly IApplicationConfig _appConfig;
        private readonly IClientAppState _appState;
        private readonly IAuthService _authService;
        private readonly ICircuitManager _circuitManager;
        private readonly IDataService _dataService;
        private readonly ConcurrentQueue<CircuitEvent> _eventQueue = new();
        private readonly IExpiringTokenService _expiringTokenService;
        private readonly ILogger<CircuitConnection> _logger;
        private readonly IToastService _toastService;
        public CircuitConnection(
            IAuthService authService,
            IDataService dataService,
            IClientAppState appState,
            IHubContext<AgentHub> agentHubContext,
            IApplicationConfig appConfig,
            ICircuitManager circuitManager,
            IToastService toastService,
            IExpiringTokenService expiringTokenService,
            ILogger<CircuitConnection> logger)
        {
            _dataService = dataService;
            _agentHubContext = agentHubContext;
            _appState = appState;
            _appConfig = appConfig;
            _authService = authService;
            _circuitManager = circuitManager;
            _toastService = toastService;
            _expiringTokenService = expiringTokenService;
            _logger = logger;
        }


        public event EventHandler<CircuitEvent> MessageReceived;

        public string ConnectionId { get; set; }
        public nexRemoteUser User { get; set; }


        public Task DeleteRemoteLogs(string deviceId)
        {
            var (canAccess, key) = CanAccessDevice(deviceId);
            if (!canAccess)
            {
                _toastService.ShowToast("Brak dostępu.", classString: "bg-warning");
                return Task.CompletedTask;
            }

            _logger.LogInformation("Wysłano polecenie usunięcia dzienników.  Urządzenie: {deviceId}.  Użytkownik: {username}",
                deviceId,
                User.UserName);

            return _agentHubContext.Clients.Client(key).SendAsync("DeleteLogs");
        }

        public Task ExecuteCommandOnAgent(ScriptingShell shell, string command, string[] deviceIDs)
        {
            deviceIDs = _dataService.FilterDeviceIDsByUserPermission(deviceIDs, User);
            var connections = GetActiveClientConnections(deviceIDs);

            _logger.LogInformation("Polecenie wykonane przez {username}.  Powłoka: {shell}.  Polecenie: {command}.  Urządzenia: {deviceIds}",
                  User.UserName,
                  shell,
                  command,
                  string.Join(", ", deviceIDs));

            var authTokenForUploadingResults = _expiringTokenService.GetToken(Time.Now.AddMinutes(5));

            foreach (var connection in connections)
            {
                _agentHubContext.Clients.Client(connection.Key).SendAsync("ExecuteCommand",
                    shell,
                    command,
                    authTokenForUploadingResults,
                    User.UserName,
                    ConnectionId);
            }

            return Task.CompletedTask;
        }

        public Task GetPowerShellCompletions(string inputText, int currentIndex, CompletionIntent intent, bool? forward)
        {
            var (canAccess, key) = CanAccessDevice(_appState.DevicesFrameSelectedDevices.FirstOrDefault());
            if (!canAccess)
            {
                return Task.CompletedTask;
            }

            return _agentHubContext.Clients.Client(key).SendAsync("GetPowerShellCompletions", inputText, currentIndex, intent, forward, ConnectionId);
        }

        public Task GetRemoteLogs(string deviceId)
        {
            var (canAccess, key) = CanAccessDevice(deviceId);
            if (!canAccess)
            {
                _toastService.ShowToast("Brak dostępu.", classString: "bg-warning");
                return Task.CompletedTask;
            }

            return _agentHubContext.Clients.Client(key).SendAsync("GetLogs", ConnectionId);
        }

        public Task InvokeCircuitEvent(CircuitEventName eventName, params object[] args)
        {
            _eventQueue.Enqueue(new CircuitEvent(eventName, args));
            return Task.Run(ProcessMessages);
        }

        public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(ConnectionId))
            {
                _circuitManager.TryRemoveConnection(ConnectionId, out _);
            }
            await base.OnCircuitClosedAsync(circuit, cancellationToken);
        }

        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (await _authService.IsAuthenticated())
            {
                User = await _authService.GetUser();
                ConnectionId = Guid.NewGuid().ToString();
                _circuitManager.TryAddConnection(ConnectionId, this);
            }
            await base.OnCircuitOpenedAsync(circuit, cancellationToken);
        }

        public Task ReinstallAgents(string[] deviceIDs)
        {
            deviceIDs = _dataService.FilterDeviceIDsByUserPermission(deviceIDs, User);
            var connections = GetActiveClientConnections(deviceIDs);
            foreach (var connection in connections)
            {
                _agentHubContext.Clients.Client(connection.Key).SendAsync("ReinstallAgent");
            }
            _dataService.RemoveDevices(deviceIDs);
            return Task.CompletedTask;
        }

        public async Task<bool> RemoteControl(string deviceID)
        {
            var targetDevice = AgentHub.ServiceConnections.FirstOrDefault(x => x.Value.ID == deviceID);

            if (targetDevice.Value is null)
            {
                MessageReceived?.Invoke(this, new CircuitEvent(CircuitEventName.DisplayMessage,
                    "Wybrane urządzenie nie jest online.",
                    "Urządzenie nie jest online.",
                    "bg-warning"));
                return false;
            }

            if (_dataService.DoesUserHaveAccessToDevice(deviceID, User))
            {
                var currentUsers = CasterHub.SessionInfoList.Count(x => x.Value.OrganizationID == User.OrganizationID);
                if (currentUsers >= _appConfig.RemoteControlSessionLimit)
                {
                    MessageReceived?.Invoke(this, new CircuitEvent(CircuitEventName.DisplayMessage,
                        "Istnieje już maksymalna liczba aktywnych sesji zdalnego sterowania dla Twojej organizacji.",
                        "Osiągnięto maksymalną liczbę jednoczesnych sesji.",
                        "bg-warning"));
                    return false;
                }
                await _agentHubContext.Clients.Client(targetDevice.Key).SendAsync("RemoteControl", ConnectionId, targetDevice.Key);
                return true;
            }
            else
            {
                _dataService.WriteEvent($"Próba zdalnego sterowania przez nieautoryzowanego użytkownika.  Device ID: {deviceID}.  Użytkownik: {User.UserName}.", EventType.Warning, targetDevice.Value.OrganizationID);
                return false;
            }
        }

        public Task RemoveDevices(string[] deviceIDs)
        {
            var filterDevices = _dataService.FilterDeviceIDsByUserPermission(deviceIDs, User);
            _dataService.RemoveDevices(filterDevices);
            return Task.CompletedTask;
        }

        public async Task RunScript(IEnumerable<string> deviceIds, Guid savedScriptId, int scriptRunId, ScriptInputType scriptInputType, bool runAsHostedService)
        {
            string username;
            if (runAsHostedService)
            {
                username = "nexRemote Server";
            }
            else
            {
                username = User.UserName;
                deviceIds = _dataService.FilterDeviceIDsByUserPermission(deviceIds.ToArray(), User);
            }

            var authToken = _expiringTokenService.GetToken(Time.Now.AddMinutes(AppConstants.ScriptRunExpirationMinutes));

            var connectionIds = AgentHub.ServiceConnections.Where(x => deviceIds.Contains(x.Value.ID)).Select(x => x.Key);

            if (connectionIds.Any())
            {
                await _agentHubContext.Clients.Clients(connectionIds).SendAsync("RunScript", savedScriptId, scriptRunId, username, scriptInputType, authToken);
            }

        }

        public Task SendChat(string message, string deviceId)
        {
            if (!_dataService.DoesUserHaveAccessToDevice(deviceId, User))
            {
                return Task.CompletedTask;
            }

            var connection = AgentHub.ServiceConnections.FirstOrDefault(x =>
                x.Value.OrganizationID == User.OrganizationID &&
                x.Value.ID == deviceId
            );

            if (connection.Value is null)
            {
                _toastService.ShowToast("Urządzenie nie zostało znalezione.");
                return Task.CompletedTask;
            }

            var organizationName = _dataService.GetOrganizationNameByUserName(User.UserName);

            return _agentHubContext.Clients.Client(connection.Key).SendAsync("Chat",
                User.UserOptions.DisplayName ?? User.UserName,
                message,
                organizationName,
                false,
                ConnectionId);
        }

        public async Task<bool> TransferFileFromBrowserToAgent(string deviceId, string transferId, string[] fileIds)
        {
            var serviceConnection = AgentHub.ServiceConnections.FirstOrDefault(x => x.Value.ID == deviceId);

            if (serviceConnection.Value is null)
            {
                return false;
            }

            if (!_dataService.DoesUserHaveAccessToDevice(deviceId, User))
            {
                _logger.LogWarning("User {username} does not have access to device ID {deviceId} and attempted file upload.",
                    User.UserName,
                    deviceId);

                return false;
            }

            var authToken = _expiringTokenService.GetToken(Time.Now.AddMinutes(5));

            await _agentHubContext.Clients.Client(serviceConnection.Key).SendAsync(
                "TransferFileFromBrowserToAgent",
                transferId,
                fileIds,
                ConnectionId,
                authToken);

            return true;
        }

        public async Task TriggerHeartbeat(string deviceId)
        {
            var (canAccess, connectionId) = CanAccessDevice(deviceId);

            if (!canAccess)
            {
                return;
            }

            await _agentHubContext.Clients.Client(connectionId).SendAsync("TriggerHeartbeat");
        }

        public Task UninstallAgents(string[] deviceIDs)
        {
            deviceIDs = _dataService.FilterDeviceIDsByUserPermission(deviceIDs, User);
            var connections = GetActiveClientConnections(deviceIDs);
            foreach (var connection in connections)
            {
                _agentHubContext.Clients.Client(connection.Key).SendAsync("UninstallAgent");
            }
            _dataService.RemoveDevices(deviceIDs);
            return Task.CompletedTask;
        }

        public Task UpdateTags(string deviceID, string tags)
        {
            if (_dataService.DoesUserHaveAccessToDevice(deviceID, User))
            {
                if (tags.Length > 200)
                {
                    MessageReceived?.Invoke(this, new CircuitEvent(CircuitEventName.DisplayMessage,
                        $"Tag musi mieć maksymalnie 200 znaków. Dostarczona długość to {tags.Length}.",
                        "Tag musi mieć mniej niż 200 znaków.",
                        "bg-warning"));
                }
                _dataService.UpdateTags(deviceID, tags);
                MessageReceived?.Invoke(this, new CircuitEvent(CircuitEventName.DisplayMessage,
                    "Urządzenie zostało zaktualizowane pomyślnie.",
                    "Zaktualizowano urządzenie.",
                    "bg-success"));
            }
            return Task.CompletedTask;
        }

        public Task UploadFiles(List<string> fileIDs, string transferID, string[] deviceIDs)
        {
            _dataService.WriteEvent(new EventLog()
            {
                EventType = EventType.Info,
                Message = $"Przesyłanie plików rozpoczęte przez {User.UserName}.  ID Transferu Plików: {string.Join(", ", fileIDs)}.",
                TimeStamp = Time.Now,
                OrganizationID = User.OrganizationID
            });
            deviceIDs = _dataService.FilterDeviceIDsByUserPermission(deviceIDs, User);
            var connections = GetActiveClientConnections(deviceIDs);
            foreach (var connection in connections)
            {
                _agentHubContext.Clients.Client(connection.Key).SendAsync("UploadFiles", transferID, fileIDs, ConnectionId);
            }
            return Task.CompletedTask;
        }

        private (bool canAccess, string connectionId) CanAccessDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return (false, null);
            }

            var kvp = AgentHub.ServiceConnections.FirstOrDefault(x => x.Value.ID == deviceId);

            if (kvp.Value is null)
            {
                return (false, null);
            }

            if (!_dataService.DoesUserHaveAccessToDevice(kvp.Value.ID, User))
            {
                return (false, null);
            }

            return (true, kvp.Key);
        }

        private IEnumerable<KeyValuePair<string, Device>> GetActiveClientConnections(IEnumerable<string> deviceIDs)
        {
            return AgentHub.ServiceConnections.Where(x =>
                x.Value.OrganizationID == User.OrganizationID &&
                deviceIDs.Contains(x.Value.ID)
            );
        }

        private void ProcessMessages()
        {
            lock (_eventQueue)
            {
                while (_eventQueue.TryDequeue(out var circuitEvent))
                {
                    try
                    {
                        MessageReceived?.Invoke(this, circuitEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Błąd podczas wywoływania zdarzenia obwodu.");
                    }
                }
            }
        }
    }
}
