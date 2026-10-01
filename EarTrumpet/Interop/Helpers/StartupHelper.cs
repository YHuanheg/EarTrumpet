using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;

namespace EarTrumpet.Interop.Helpers
{
    /// <summary>
    /// Where the "run at startup" state lives depends on how EarTrumpet was installed:
    /// packaged (MSIX / Microsoft Store) uses the StartupTask declared in the package
    /// manifest - the same setting Task Manager &gt; Startup apps shows - and unpackaged
    /// uses a value under HKCU\...\Run. Both are surfaced through this one type so the
    /// caller does not need to care which one is in play.
    /// </summary>
    public enum RunAtStartupState
    {
        /// <summary>Not registered, and the user is free to change it.</summary>
        Disabled,
        /// <summary>Registered, and the user is free to change it.</summary>
        Enabled,
        /// <summary>The user turned the entry off outside of EarTrumpet (Task Manager).</summary>
        DisabledByUser,
        /// <summary>Administrative policy prevents the entry from being enabled.</summary>
        DisabledByPolicy,
        /// <summary>Administrative policy forces the entry on.</summary>
        ForcedByPolicy,
        /// <summary>The state could not be read or written at all.</summary>
        Unavailable,
    }

    static class StartupHelper
    {
        // Must match <desktop:StartupTask TaskId="..."/> in EarTrumpet.Package/Package.appxmanifest.
        private const string StartupTaskId = "EarTrumpet";

        private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);

        // Cast to int instead of naming StartupTaskState members: the values below are the
        // documented order of the enum, and this keeps the build working with any SDK
        // reference that exposes the type without every member.
        private const int StartupTaskStateDisabled = 0;
        private const int StartupTaskStateDisabledByUser = 1;
        private const int StartupTaskStateEnabled = 2;
        private const int StartupTaskStateDisabledByPolicy = 3;
        private const int StartupTaskStateEnabledByPolicy = 4;

        /// <summary>
        /// Reads the current startup registration. Never throws and never runs the WinRT
        /// call on the calling (UI) thread.
        /// </summary>
        public static RunAtStartupState GetState()
        {
            try
            {
                if (!App.HasIdentity)
                {
                    return GetUnpackagedState();
                }
                return GetStartupTaskState();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"StartupHelper GetState Failed: {ex.Message}");
                return RunAtStartupState.Unavailable;
            }
        }

        /// <summary>
        /// Turns the startup registration on or off and reports the state Windows ended up
        /// in - which can differ from the request when a policy or the user owns the entry.
        /// </summary>
        public static RunAtStartupState SetEnabled(bool enabled)
        {
            try
            {
                if (!App.HasIdentity)
                {
                    var registration = RunKeyStartupRegistration.CreateDefault();
                    if (enabled)
                    {
                        registration.Register(RunKeyStartupRegistration.GetExecutablePath());
                    }
                    else
                    {
                        registration.Unregister();
                    }
                    Trace.WriteLine($"StartupHelper SetEnabled({enabled}) via Run key");
                }
                else
                {
                    var state = Task.Run(() =>
                    {
                        var startupTask = WaitFor(StartupTask.GetAsync(StartupTaskId));
                        if (startupTask == null)
                        {
                            return RunAtStartupState.Unavailable;
                        }

                        if (enabled)
                        {
                            // Windows can refuse: DisabledByUser and DisabledByPolicy are both
                            // returned here rather than applied.
                            return FromStartupTaskState((int)WaitFor(startupTask.RequestEnableAsync()));
                        }

                        startupTask.Disable();
                        return FromStartupTaskState((int)startupTask.State);
                    });

                    if (state.Wait(OperationTimeout))
                    {
                        Trace.WriteLine($"StartupHelper SetEnabled({enabled}) via StartupTask: {state.Result}");
                        return state.Result;
                    }

                    Trace.WriteLine("StartupHelper SetEnabled timed out");
                    return RunAtStartupState.Unavailable;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"StartupHelper SetEnabled({enabled}) Failed: {ex.Message}");
            }

            // Whatever happened, the truth is what Windows reports now.
            return GetState();
        }

        /// <summary>True when the user is allowed to flip the setting from EarTrumpet.</summary>
        public static bool CanUserChange(RunAtStartupState state)
        {
            return state == RunAtStartupState.Enabled || state == RunAtStartupState.Disabled;
        }

        private static RunAtStartupState GetUnpackagedState()
        {
            var isRegistered = RunKeyStartupRegistration.CreateDefault()
                .IsRegisteredFor(RunKeyStartupRegistration.GetExecutablePath());
            return isRegistered ? RunAtStartupState.Enabled : RunAtStartupState.Disabled;
        }

        private static RunAtStartupState GetStartupTaskState()
        {
            var state = Task.Run(() =>
            {
                var startupTask = WaitFor(StartupTask.GetAsync(StartupTaskId));
                return startupTask == null
                    ? RunAtStartupState.Unavailable
                    : FromStartupTaskState((int)startupTask.State);
            });

            if (state.Wait(OperationTimeout))
            {
                return state.Result;
            }

            Trace.WriteLine("StartupHelper GetStartupTaskState timed out");
            return RunAtStartupState.Unavailable;
        }

        private static RunAtStartupState FromStartupTaskState(int state)
        {
            switch (state)
            {
                case StartupTaskStateEnabled:
                    return RunAtStartupState.Enabled;
                case StartupTaskStateDisabled:
                    return RunAtStartupState.Disabled;
                case StartupTaskStateDisabledByUser:
                    return RunAtStartupState.DisabledByUser;
                case StartupTaskStateDisabledByPolicy:
                    return RunAtStartupState.DisabledByPolicy;
                case StartupTaskStateEnabledByPolicy:
                    return RunAtStartupState.ForcedByPolicy;
                default:
                    return RunAtStartupState.Unavailable;
            }
        }

        /// <summary>
        /// Blocks on a WinRT async operation by polling it. Deliberately never called on the
        /// UI thread (callers wrap this in Task.Run) so a slow completion cannot reenter it.
        /// </summary>
        private static T WaitFor<T>(IAsyncOperation<T> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            while (operation.Status == AsyncStatus.Started)
            {
                if (stopwatch.Elapsed > OperationTimeout)
                {
                    throw new TimeoutException("StartupTask operation timed out");
                }
                Thread.Sleep(15);
            }
            return operation.GetResults();
        }
    }
}
