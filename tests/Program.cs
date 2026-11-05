using FC_Tweaks;

const ulong online = 1UL << 47;
const ulong anotherWorld = 1UL << 40;
var tests = 0;

void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual, string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    tests++;
}

RosterMember[] members =
[
    new(1, online, "Zoe", "The Goblet", true, 3),
    new(2, 0, "Offline One", "18d", false, 0),
    new(3, anotherWorld, "Alice", "Siren", false, 1),
    new(4, online, "bob", "The Goblet", true, 0),
    new(5, 0, "Offline Two", "18d", false, 0),
    new(6, online, "alice", "Alpha", false, 2),
];

Equal([2, 1, 5, 3, 4, 0], RosterSort.CreateOrder(members, RosterSortMode.Name), "Online name order, stable ties");
Equal([0, 1, 3, 5, 4, 2], RosterSort.CreateOrder(members, RosterSortMode.Location), "Goblet first, stable ties, world name");
Equal([3, 1, 2, 5, 4, 0], RosterSort.CreateOrder(members, RosterSortMode.Rank), "Rank hierarchy, not name");

foreach (var mode in Enum.GetValues<RosterSortMode>())
{
    Equal(Array.Empty<int>(), RosterSort.CreateOrder([], mode), $"{mode}: empty");
    Equal([0], RosterSort.CreateOrder([members[0]], mode), $"{mode}: singleton");
    Equal([0, 1], RosterSort.CreateOrder([members[1], members[4]], mode), $"{mode}: all offline");
    Equal(Array.Empty<(int, int)>(), RosterSort.GetSwaps([0, 1]), $"{mode}: identity needs no swaps");
}

if (!members[2].IsOnline || new RosterMember(7, 1UL << 17, "AFK without online flag", "", false, 0).IsOnline)
    throw new InvalidOperationException("Online classification must use Online | AnotherWorld, not displayed location or unrelated flags.");
tests++;

foreach (var mode in Enum.GetValues<RosterSortMode>())
{
    for (var refresh = 0; refresh < 5; refresh++)
    {
        var reloaded = members.ToArray();
        var order = RosterSort.CreateOrder(reloaded, mode);
        foreach (var (first, second) in RosterSort.GetSwaps(order))
            (reloaded[first], reloaded[second]) = (reloaded[second], reloaded[first]);
        Equal(RosterSort.CreateOrder(members, mode).Select(i => members[i]), reloaded,
            $"{mode}: native refresh restores the selected ordering");
        Equal(Array.Empty<(int, int)>(), RosterSort.GetSwaps(RosterSort.CreateOrder(reloaded, mode)),
            $"{mode}: repeated checks make no native swaps when already sorted");
    }
}

var random = new Random(41);
for (var sample = 0; sample < 100; sample++)
{
    var roster = Enumerable.Range(0, sample * 2 + 1).Select(i =>
        new RosterMember((ulong)i + 1, random.Next(3) == 0 ? 0 : (i % 2 == 0 ? online : anotherWorld),
            $"Name {random.Next(10)}", $"Location {random.Next(10)}", random.Next(5) == 0, random.Next(16))).ToArray();
    foreach (var mode in Enum.GetValues<RosterSortMode>())
    {
        var order = RosterSort.CreateOrder(roster, mode);
        Equal(Enumerable.Range(0, roster.Length), order.Order(), "Permutation preserves every member exactly once");
        var rows = roster.ToArray();
        var uiIds = roster.Select(m => m.ContentId).ToArray();
        var memoIds = uiIds.ToArray();
        foreach (var (first, second) in RosterSort.GetSwaps(order))
        {
            if (!roster[first].IsOnline || !roster[second].IsOnline)
                throw new InvalidOperationException("A swap moved an offline slot.");
            (rows[first], rows[second]) = (rows[second], rows[first]);
            (uiIds[first], uiIds[second]) = (uiIds[second], uiIds[first]);
            (memoIds[first], memoIds[second]) = (memoIds[second], memoIds[first]);
        }
        Equal(order.Select(i => roster[i]), rows, "Swap sequence realizes full permutation");
        Equal(rows.Select(m => m.ContentId), uiIds, "Displayed and clicked member identities match");
        Equal(uiIds, memoIds, "FC memo identity follows row");
        Equal(Enumerable.Range(0, roster.Length).Where(i => !roster[i].IsOnline),
            Enumerable.Range(0, roster.Length).Where(i => !rows[i].IsOnline && rows[i] == roster[i]),
            "Offline rows stay in their exact original slots");
        Equal(Enumerable.Range(0, roster.Length), RosterSort.CreateOrder(rows, mode), "Sorting is idempotent");
    }
}

Console.WriteLine($"Passed {tests} ordering and identity checks.");
