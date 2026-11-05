namespace FC_Tweaks;

internal enum RosterSortMode
{
    Name,
    Location,
    Rank,
}

internal readonly record struct RosterMember(
    ulong ContentId, ulong State, string Name, string Location, bool IsGoblet, int RankOrder)
{
    public bool IsOnline => (State & 0x810000000000UL) != 0;
}

internal static class RosterSort
{
    // Each destination receives this source index. Offline slots are never moved.
    public static int[] CreateOrder(IReadOnlyList<RosterMember> members, RosterSortMode mode)
    {
        var online = Enumerable.Range(0, members.Count).Where(i => members[i].IsOnline).ToArray();
        var sorted = mode switch
        {
            RosterSortMode.Name => online.OrderBy(i => members[i].Name, StringComparer.CurrentCultureIgnoreCase),
            RosterSortMode.Location => online.OrderBy(i => !members[i].IsGoblet)
                .ThenBy(i => members[i].Location, StringComparer.CurrentCultureIgnoreCase),
            RosterSortMode.Rank => online.OrderBy(i => members[i].RankOrder),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var order = Enumerable.Range(0, members.Count).ToArray();
        var destination = 0;
        foreach (var source in sorted)
            order[online[destination++]] = source;
        return order;
    }

    public static IEnumerable<(int First, int Second)> GetSwaps(IReadOnlyList<int> order)
    {
        var sources = Enumerable.Range(0, order.Count).ToArray();
        var positions = Enumerable.Range(0, order.Count).ToArray();
        for (var destination = 0; destination < order.Count; destination++)
        {
            var sourcePosition = positions[order[destination]];
            if (sourcePosition == destination)
                continue;

            var displacedSource = sources[destination];
            sources[sourcePosition] = displacedSource;
            positions[displacedSource] = sourcePosition;
            sources[destination] = order[destination];
            positions[order[destination]] = destination;
            yield return (destination, sourcePosition);
        }
    }
}
