namespace CommandPrompts.Core.Models;

public enum SessionRunState : byte
{
    Idle = 0,
    Running = 1,
    Terminating = 2
}
