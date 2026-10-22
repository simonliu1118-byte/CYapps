namespace CYERPAutoQuery;

/// <summary>
/// Identity of one ERP process explicitly launched by this running AutoQuery instance.
/// Diagnostic process-name similarity and same-session membership alone never confer ownership.
/// </summary>
internal sealed record ErpManagedProcessIdentity(int Pid, DateTime StartedUtc, int SessionId, string ExecutablePath)
{
    /// <summary>Do not claim a pre-existing single-instance ERP if ShellExecute forwarded a launch.</summary>
    public static bool IsFreshLaunch(DateTime startedUtc, DateTime launchRequestedUtc) =>
        startedUtc.ToUniversalTime() >= launchRequestedUtc.ToUniversalTime().AddSeconds(-2);

    public bool Matches(int pid, DateTime startedUtc, int sessionId, string executablePath, int callerSessionId)
    {
        return Pid == pid
            && SessionId == sessionId
            && SessionId == callerSessionId
            && StartedUtc == startedUtc.ToUniversalTime()
            && !string.IsNullOrWhiteSpace(ExecutablePath)
            && !string.IsNullOrWhiteSpace(executablePath)
            && string.Equals(ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase);
    }
}
