using System;
using System.Threading.Tasks;

namespace nexRemote.Agent.Interfaces
{
    public interface IUpdater
    {
        Task BeginChecking();
        Task CheckForUpdates();
        Task InstallLatestVersion();
    }
}