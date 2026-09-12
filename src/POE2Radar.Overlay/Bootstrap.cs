using POE2Radar.Core;
using POE2Radar.Core.Game;
using POE2Radar.Core.Health;

namespace POE2Radar.Overlay;

/// <summary>
/// Scans for the PoE2 GameState global-pointer slot via the "Game States" AOB pattern, accepting the slot
/// whose chain resolves at least to a real in-zone AreaInstance (the patch-stable low fields). Stateless +
/// re-runnable — the lazy SlotResolver in RadarApp calls it on a cadence until it returns non-zero.
/// </summary>
internal static class Bootstrap
{
    /// <summary>Scan and validate candidates. Returns the best usable slot, the raw AOB hit count,
    /// and the deepest chain stage reached by any candidate.</summary>
    public static nint ScanForSlot(ProcessHandle process, MemoryReader reader, out int candidateCount, out ResolveStage bestStage)
    {
        candidateCount = 0;
        nint bestSlot = 0;
        bestStage = ResolveStage.None;
        var probe = new Poe2Live(reader, 0);

        foreach (var pattern in AobPatterns.GameStateRefs)
        {
            foreach (var slot in AobScanner.ScanForResolvedAddresses(process, reader, pattern).Distinct())
            {
                candidateCount++;
                probe.Rebind(slot);
                var stage = probe.Probe(out _, out _, out _, out _, out _);
                if (stage > bestStage) { bestStage = stage; bestSlot = slot; }
                if (stage == ResolveStage.Full) return slot;   // best possible — stop early
            }
        }
        return bestStage >= ResolveStage.InZone ? bestSlot : 0;
    }
}
