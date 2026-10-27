namespace CYERPAutoQuery;

/// <summary>A process that owns an ERP window when ERP has to be force-restarted.</summary>
internal sealed record ErpWindowOwner(int Pid, int? SessionId, bool OwnsMainWindow);

/// <summary>
/// Which processes a forced ERP restart may end (user, 2026-10-26). The ERP program names and
/// folders vary (MainMenu in C_dsbin; the 作業 run in SMCOPIS01S or SMLeaderWorkCenter in S_dsbin)
/// and the ERP server runs services of its own with similar names (SMDispatcher, SMCounterS, AUC),
/// so a process is picked only by owning an ERP main or 作業 window — never by name, folder or
/// process tree — and only in this Windows session (never another remote-desktop user's ERP),
/// never this program. 作業 owners come first and the main window's owner last.
/// </summary>
internal static class ErpRestartTargets
{
    public static IReadOnlyList<int> Select(IEnumerable<ErpWindowOwner> owners, int sessionId, int ownPid) =>
        owners.Where(o => o.SessionId == sessionId && o.Pid != ownPid && o.Pid > 0)
            .GroupBy(o => o.Pid)
            .Select(g => (Pid: g.Key, Main: g.Any(o => o.OwnsMainWindow)))
            .OrderBy(p => p.Main).ThenBy(p => p.Pid)
            .Select(p => p.Pid)
            .ToList();
}
