using Hartonomous.AgentClient.Models;
using System;

namespace Hartonomous.AgentClient.Events;

public class AgentInstanceEventArgs : EventArgs
{
    public AgentInstance Instance { get; }

    public AgentInstanceEventArgs(AgentInstance instance)
    {
        Instance = instance;
    }
}
