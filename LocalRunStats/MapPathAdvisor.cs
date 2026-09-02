using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;

namespace LocalRunStats;

// Computes the best remaining path through the current act's map, weighted
// toward one goal at a time (most elites / most events / most rest sites /
// etc.) — requested as "ideal map path... best path for most upgrades or
// most elites or most question marks."
//
// The map is a DAG (MapPoint.Children only ever point to the next row up —
// confirmed via reflection: ActMap.GetAllMapPoints/GetPointsInRow are row-
// ordered, and MapPoint has no back-edges into Children), so "best path
// from here to the boss" is the classic longest-path-in-a-DAG problem: walk
// every node reachable from the current position in DESCENDING row order
// (boss-ward first) so every node's children are already scored by the time
// it's processed, then bestScore[node] = weight(node) + max(bestScore[child]
// for child in node.Children, default 0). Reconstructing forward from the
// current node via the recorded best-child pointer gives the actual path.
public static class MapPathAdvisor
{
    public enum Goal
    {
        Elites,
        Events,
        RestSites,
        Shops,
        Treasures,
    }

    // Combat is a tie-breaker, not the primary objective, for every goal —
    // maximizing the chosen goal always wins first regardless of fight
    // count. Two scales, not one: PrimaryScale always dominates (the goal
    // count itself), then EliteAvoidScale — an Elite fight is worse to
    // route through than a regular one, so it needs to outweigh "one fewer
    // regular fight" the same way the primary goal outweighs everything.
    // Regular Monster rooms are the lowest-priority tie-break, worth
    // exactly 1 each, and apply to EVERY goal including Elites itself —
    // more elites is still the primary objective there, but among paths
    // tied on elite count, fewer regular fights along the way wins. Only
    // the Elite-avoidance tier is skipped for the Elites goal (avoiding
    // elites there would fight the goal's own primary objective); regular-
    // fight avoidance never is. A path's room-type counts can only ever
    // differ by roughly the number of floors in an act (well under 1,000),
    // so these factors leave a wide safety margin at every tier.
    private const int PrimaryScale = 1_000_000;
    private const int EliteAvoidScale = 1_000;

    private static int Weight(MapPoint point, Goal goal)
    {
        var primary = goal switch
        {
            Goal.Elites => point.PointType == MapPointType.Elite ? 1 : 0,
            Goal.Events => point.PointType == MapPointType.Unknown ? 1 : 0,
            // "Most upgrades" — a Rest Site OFFERS a choice between healing
            // and upgrading, it doesn't guarantee one, but it's the only
            // room type that can ever give you an upgrade outside of
            // relics/potions, so this is the closest available proxy
            // without reading INTO what the player will actually choose at
            // each rest site.
            Goal.RestSites => point.PointType == MapPointType.RestSite ? 1 : 0,
            Goal.Shops => point.PointType == MapPointType.Shop ? 1 : 0,
            Goal.Treasures => point.PointType == MapPointType.Treasure ? 1 : 0,
            _ => 0,
        };

        // Bug found live: this originally only penalized MapPointType.Monster,
        // never Elite — meaning a path through an Elite scored the SAME as
        // one through nothing at all, so it could look "better" than a path
        // through a regular fight whenever total fight counts tied.
        // Reported live: "why is it choosing the [path through a regular
        // fight]... they have the same number of combats but one is an
        // elite and one is a regular combat" [expecting the elite to be
        // avoided, not treated as free]. Elite now costs strictly more than
        // a regular Monster room.
        //
        // monsterPenalty is now UNCONDITIONAL — regular fights are never
        // desired for ANY goal, Elites included: "even on the elite toggle
        // path it will prioritize less enemies, but will still go for path
        // of most elites." Elites themselves still only get penalized for
        // every OTHER goal (avoiding them for the Elites goal would fight
        // the goal's own primary objective).
        var elitePenalty = goal != Goal.Elites && point.PointType == MapPointType.Elite ? 1 : 0;
        var monsterPenalty = point.PointType == MapPointType.Monster ? 1 : 0;
        return primary * PrimaryScale - elitePenalty * EliteAvoidScale - monsterPenalty;
    }

    // Returns the recommended path from `from` to the boss (inclusive of
    // both ends), or an empty list if `from` is null or has no path to the
    // boss (shouldn't happen on a well-formed map, but this is read-only
    // analysis of live game state we don't control).
    public static List<MapPoint> ComputeBestPath(MapPoint from, Goal goal)
    {
        var result = new List<MapPoint>();
        if (from == null) return result;

        // Collect every node reachable forward from `from` (BFS over
        // Children), grouped by row, so we can process rows in descending
        // order below regardless of how the map happens to branch.
        var reachable = new Dictionary<MapPoint, int>(); // point -> row
        var frontier = new Queue<MapPoint>();
        frontier.Enqueue(from);
        reachable[from] = from.coord.row;
        while (frontier.Count > 0)
        {
            var point = frontier.Dequeue();
            foreach (var child in point.Children)
            {
                if (reachable.ContainsKey(child)) continue;
                reachable[child] = child.coord.row;
                frontier.Enqueue(child);
            }
        }

        var rowsDescending = reachable.Values.Distinct().OrderByDescending(r => r).ToList();
        var bestScore = new Dictionary<MapPoint, int>();
        var bestChild = new Dictionary<MapPoint, MapPoint>();

        foreach (var row in rowsDescending)
        {
            foreach (var point in reachable.Keys.Where(p => reachable[p] == row))
            {
                var best = 0;
                MapPoint bestNext = null;
                foreach (var child in point.Children)
                {
                    if (!bestScore.TryGetValue(child, out var childScore)) continue;
                    if (bestNext == null || childScore > best)
                    {
                        best = childScore;
                        bestNext = child;
                    }
                }
                bestScore[point] = Weight(point, goal) + best;
                if (bestNext != null) bestChild[point] = bestNext;
            }
        }

        var current = from;
        result.Add(current);
        while (bestChild.TryGetValue(current, out var next))
        {
            result.Add(next);
            current = next;
        }
        return result;
    }
}
