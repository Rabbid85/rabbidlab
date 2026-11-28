using System;
using System.Runtime.InteropServices;

namespace RabbidLab.Client;

/// <summary>
/// The deploy GPO's install tasks ("Install the Rabbid Lab client ...") left in Task Scheduler. Windows Server 2016
/// keeps such a task after it has run. Once the client is installed and running they have done what they were for, so
/// the service takes them away. One that is still running (the install that started this service) is left for the
/// next time.
/// </summary>
internal static class InstallTasks
{
    private const string Prefix = "Install the Rabbid Lab client";
    private const int Running = 4; // TASK_STATE_RUNNING

    /// <summary>How many were removed. Never throws: nothing the client does depends on it.</summary>
    public static int Clear()
    {
        var gone = 0;
        try
        {
            if (Type.GetTypeFromProgID("Schedule.Service") is not { } type)
            {
                return 0;
            }
            dynamic scheduler = Activator.CreateInstance(type);
            try
            {
                scheduler.Connect();
                dynamic root = scheduler.GetFolder("\\");
                foreach (dynamic task in root.GetTasks(1)) // 1: hidden ones too
                {
                    string name = task.Name;
                    // Only the GPO's own, by the name it gives them, in the folder it puts them in.
                    if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && (int) task.State != Running)
                    {
                        root.DeleteTask(name, 0);
                        gone++;
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(scheduler);
            }
        }
        catch (Exception)
        {
            // Left for the next time the service starts.
        }
        return gone;
    }
}
