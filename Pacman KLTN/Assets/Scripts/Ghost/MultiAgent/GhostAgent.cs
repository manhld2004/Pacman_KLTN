using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public enum SearchScoringMode
{
    Baseline,
    NormalizedAgeDistance,
    Enhanced
}

[System.Serializable]
public class SearchScoreConfig
{
    [Header("Weights")]
    public float distanceWeight = 1.2f;
    public float ageWeight = 1.5f;
    public float frontierWeight = 1.0f;
    public float teammateWeight = 0.8f;

    [Header("Normalization")]
    public float maxUsefulDistance = 40f;
    public float maxUsefulAge = 100f;
    public int maxFrontier = 20;
    public int maxTeammatePenalty = 6;

    [Header("Frontier")]
    public int staleThreshold = 30;
    public int frontierRadius = 2;

    [Header("Team Separation")]
    public int minSeparation = 6;

    [Header("Partition")]
    public float partitionWeight = 0.5f;
}

[System.Serializable]
public class GhostAgent
{
    public Region region;
    public SearchScoringMode searchScoringMode = SearchScoringMode.NormalizedAgeDistance;
    public SearchScoreConfig searchScoreConfig = new SearchScoreConfig();

    // =========================
    // SEARCH
    // =========================
    public bool InRegion(Vector2Int pos)
    {
        if (region.ownedCells != null && region.ownedCells.Count > 0)
            return region.ownedCells.Contains(pos);

        return pos.x >= region.minX && pos.x <= region.maxX;
    }

    public Vector2Int FindBestTarget(
        Vector2Int logicPos,
        SharedWorldState worldState,
        IGridQuery grid)
    {
        int[,] distanceMap = BuildDistanceMap(logicPos, grid);
        Vector2Int best = logicPos;
        float bestScore = float.MaxValue;
        SearchScoreConfig cfg = GetSearchScoreConfig();

        foreach (Vector2Int pos in EnumerateCandidateCells(grid))
        {
            if (worldState.reservedTargets.ContainsKey(pos))
                continue;

            float score = ComputeScore(logicPos, pos, worldState, grid, distanceMap);
            score += cfg.partitionWeight * ComputePartitionPenalty(pos);

            if (score < bestScore)
            {
                bestScore = score;
                best = pos;
            }
        }

        return best;
    }

    private IEnumerable<Vector2Int> EnumerateCandidateCells(IGridQuery grid)
    {
        if (region.ownedCells != null && region.ownedCells.Count > 0)
        {
            HashSet<Vector2Int> yielded = new HashSet<Vector2Int>();

            foreach (Vector2Int cell in region.ownedCells)
            {
                if (grid.IsWalkable(cell))
                {
                    yielded.Add(cell);
                    yield return cell;
                }
            }

            if (region.extendedCells != null && region.extendedCells.Count > 0)
            {
                foreach (Vector2Int cell in region.extendedCells)
                {
                    if (!grid.IsWalkable(cell))
                        continue;

                    if (yielded.Contains(cell))
                        continue;

                    yielded.Add(cell);
                    yield return cell;
                }
            }

            yield break;
        }

        for (int x = region.minX; x <= region.maxX; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);

                if (!grid.IsWalkable(pos))
                    continue;

                yield return pos;
            }
        }
    }

    private float ComputePartitionPenalty(Vector2Int cell)
    {
        if (region.ownedCells == null || region.ownedCells.Count == 0)
            return 0f;

        if (region.ownedCells.Contains(cell))
            return 0f;

        if (region.extendedCells != null && region.extendedCells.Contains(cell))
            return 0.25f;

        return 1f;
    }

    public float ComputeScore(
        Vector2Int from,
        Vector2Int to,
        SharedWorldState worldState,
        IGridQuery grid,
        int[,] distanceMap)
    {
        switch (searchScoringMode)
        {
            case SearchScoringMode.NormalizedAgeDistance:
                return ComputeNormalizedAgeDistanceScore(from, to, worldState, distanceMap);
            case SearchScoringMode.Enhanced:
                return ComputeEnhancedScore(from, to, worldState, grid, distanceMap);
            case SearchScoringMode.Baseline:
            default:
                return ComputeBaselineScore(from, to, worldState);
        }
    }

    private float ComputeBaselineScore(
        Vector2Int from,
        Vector2Int to,
        SharedWorldState worldState)
    {
        int visit = worldState.visitTimes[to.x, to.y];
        int age = worldState.CurrentStep - visit;
        float dist = Vector2Int.Distance(from, to);

        return dist - age * 2f;
    }

    private float ComputeNormalizedAgeDistanceScore(
        Vector2Int from,
        Vector2Int to,
        SharedWorldState worldState,
        int[,] distanceMap)
    {
        SearchScoreConfig cfg = GetSearchScoreConfig();

        int visit = worldState.visitTimes[to.x, to.y];
        int age = worldState.CurrentStep - visit;
        int distance = GetPathDistance(from, to, distanceMap);

        if (distance < 0)
            return float.MaxValue;

        float normalizedDistance = NormalizeToUnit(distance, cfg.maxUsefulDistance);
        float normalizedAge = NormalizeToUnit(age, cfg.maxUsefulAge);

        return cfg.distanceWeight * normalizedDistance - cfg.ageWeight * normalizedAge;
    }

    private float ComputeEnhancedScore(
        Vector2Int from,
        Vector2Int to,
        SharedWorldState worldState,
        IGridQuery grid,
        int[,] distanceMap)
    {
        SearchScoreConfig cfg = GetSearchScoreConfig();

        int visit = worldState.visitTimes[to.x, to.y];
        int age = worldState.CurrentStep - visit;
        int distance = GetPathDistance(from, to, distanceMap);

        if (distance < 0)
            return float.MaxValue;

        float normalizedDistance = NormalizeToUnit(distance, cfg.maxUsefulDistance);
        float normalizedAge = NormalizeToUnit(age, cfg.maxUsefulAge);
        int frontierScore = ComputeFrontierScore(to, worldState, grid, cfg);
        float normalizedFrontier = NormalizeToUnit(frontierScore, cfg.maxFrontier);

        return cfg.distanceWeight * normalizedDistance
            - cfg.ageWeight * normalizedAge
            - cfg.frontierWeight * normalizedFrontier;
    }

    private int ComputeFrontierScore(
        Vector2Int cell,
        SharedWorldState worldState,
        IGridQuery grid,
        SearchScoreConfig cfg)
    {
        if (grid == null)
            return 0;

        int score = 0;

        for (int dx = -cfg.frontierRadius; dx <= cfg.frontierRadius; dx++)
        {
            for (int dy = -cfg.frontierRadius; dy <= cfg.frontierRadius; dy++)
            {
                Vector2Int p = new Vector2Int(cell.x + dx, cell.y + dy);

                if (!grid.IsWalkable(p))
                    continue;

                int age = worldState.CurrentStep - worldState.visitTimes[p.x, p.y];

                if (age >= cfg.staleThreshold)
                    score++;
            }
        }

        return score;
    }

    public int[,] BuildDistanceMap(Vector2Int origin, IGridQuery grid)
    {
        int[,] distanceMap = new int[grid.Width, grid.Height];

        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                distanceMap[x, y] = -1;
            }
        }

        if (!grid.IsWalkable(origin))
            return distanceMap;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(origin);
        distanceMap[origin.x, origin.y] = 0;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int nextDistance = distanceMap[current.x, current.y] + 1;

            foreach (Vector2Int neighbor in grid.GetNeighbors(current))
            {
                if (distanceMap[neighbor.x, neighbor.y] != -1)
                    continue;

                distanceMap[neighbor.x, neighbor.y] = nextDistance;
                queue.Enqueue(neighbor);
            }
        }

        return distanceMap;
    }

    private SearchScoreConfig GetSearchScoreConfig()
    {
        if (searchScoreConfig == null)
            searchScoreConfig = new SearchScoreConfig();

        return searchScoreConfig;
    }

    private int GetPathDistance(Vector2Int from, Vector2Int to, int[,] distanceMap)
    {
        if (distanceMap == null)
            return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);

        if (to.x < 0 || to.y < 0 || to.x >= distanceMap.GetLength(0) || to.y >= distanceMap.GetLength(1))
            return -1;

        return distanceMap[to.x, to.y];
    }

    private float NormalizeToUnit(float value, float maxValue)
    {
        if (maxValue <= 0f)
            return 0f;

        return Mathf.Clamp01(value / maxValue);
    }

    public void UpdateSharedState(SharedWorldState worldState, Vector2Int logicPos)
    {
        worldState.MarkVisited(logicPos);
    }

    // VISION
    public List<Vector2Int> GetVisibleCells(
        Vector2Int origin,
        IGridQuery grid,
        int visionRadius)
    {
        List<Vector2Int> visible = new List<Vector2Int>();

        int minX = Mathf.Max(0, origin.x - visionRadius);
        int maxX = Mathf.Min(grid.Width - 1, origin.x + visionRadius);
        int minY = Mathf.Max(0, origin.y - visionRadius);
        int maxY = Mathf.Min(grid.Height - 1, origin.y + visionRadius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int target = new Vector2Int(x, y);

                int dx = target.x - origin.x;
                int dy = target.y - origin.y;

                if (dx * dx + dy * dy > visionRadius * visionRadius)
                    continue;

                if (HasLineVision(origin, target, grid))
                    visible.Add(target);
            }
        }

        return visible;
    }

    public void UpdateVision(
        SharedWorldState worldState,
        Vector2Int origin,
        IGridQuery grid,
        int visionRadius)
    {
        List<Vector2Int> visibleCells = GetVisibleCells(origin, grid, visionRadius);

        foreach (var target in visibleCells)
        {
            worldState.MarkVisited(target);
        }
    }

    public bool UpdateVisionAndDetectPacman(
        SharedWorldState worldState,
        Vector2Int origin,
        Vector2Int pacmanPos,
        IGridQuery grid,
        int visionRadius)
    {
        bool detected = false;

        List<Vector2Int> visibleCells = GetVisibleCells(origin, grid, visionRadius);

        foreach (var target in visibleCells)
        {
            worldState.MarkVisited(target);

            if (target == pacmanPos)
                detected = true;
        }

        if (detected)
            worldState.UpdatePacmanLastKnown(pacmanPos);

        return detected;
    }

    public Vector2Int FindCaptureTarget(
        Vector2Int logicPos,
        SharedWorldState worldState,
        IGridQuery grid,
        int ghostId,
        int ghostCount)
    {
        Vector2Int pivot = worldState.pacmanLastKnownPosition ?? logicPos;

        if (worldState.pacmanLastKnownPosition.HasValue &&
            IsPrimaryChaser(ghostId, worldState, pivot) &&
            grid.IsWalkable(pivot))
        {
            return pivot;
        }

        List<Vector2Int> anchors = BuildCaptureAnchors(pivot, ghostCount);

        if (anchors.Count == 0)
            return pivot;

        int preferredIndex = Mathf.Abs(ghostId) % anchors.Count;

        for (int offset = 0; offset < anchors.Count; offset++)
        {
            Vector2Int candidate = anchors[(preferredIndex + offset) % anchors.Count];

            if (!grid.IsWalkable(candidate))
                continue;

            if (worldState.reservedTargets.TryGetValue(candidate, out int reservedBy) && reservedBy != ghostId)
                continue;

            return candidate;
        }

        return FindNearestCaptureTile(logicPos, pivot, grid);
    }

    private bool IsPrimaryChaser(int ghostId, SharedWorldState worldState, Vector2Int pivot)
    {
        float bestDist = float.MaxValue;
        int bestGhostId = ghostId;

        foreach (var pair in worldState.ghostPositions)
        {
            float dist = Vector2Int.Distance(pair.Value, pivot);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestGhostId = pair.Key;
            }
        }

        return ghostId == bestGhostId;
    }

    private List<Vector2Int> BuildCaptureAnchors(Vector2Int pivot, int ghostCount)
    {
        List<Vector2Int> anchors = new List<Vector2Int>();

        Vector2Int[] offsets =
        {
            Vector2Int.zero,
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.up + Vector2Int.right,
            Vector2Int.right + Vector2Int.down,
            Vector2Int.down + Vector2Int.left,
            Vector2Int.left + Vector2Int.up,
            Vector2Int.up * 2,
            Vector2Int.right * 2,
            Vector2Int.down * 2,
            Vector2Int.left * 2
        };

        int limit = ghostCount <= 0 ? offsets.Length : Mathf.Min(offsets.Length, ghostCount * 3);

        for (int i = 0; i < limit; i++)
            anchors.Add(pivot + offsets[i]);

        if (anchors.Count == 0)
            anchors.Add(pivot);

        return anchors;
    }

    private Vector2Int FindNearestCaptureTile(
        Vector2Int from,
        Vector2Int pivot,
        IGridQuery grid)
    {
        Vector2Int best = pivot;
        float bestScore = float.MaxValue;

        for (int radius = 0; radius <= 2; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int candidate = new Vector2Int(pivot.x + dx, pivot.y + dy);

                    if (!grid.IsWalkable(candidate))
                        continue;

                    float score = Vector2Int.Distance(from, candidate) + Vector2Int.Distance(candidate, pivot) * 0.5f;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
        }

        return best;
    }

    public bool HasLineVision(
        Vector2Int from,
        Vector2Int to,
        IGridQuery grid)
    {
        List<Vector2Int> cells = GetSupercoverCells(from, to);

        if (cells == null || cells.Count == 0)
            return false;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];

            if (cell != from && cell != to && grid.IsHardBlocked(cell))
                return false;
        }

        for (int i = 0; i < cells.Count - 1; i++)
        {
            Vector2Int a = cells[i];
            Vector2Int b = cells[i + 1];

            if (IsTransitionBlocked(a, b, grid))
                return false;
        }

        return true;
    }

    private bool IsTransitionBlocked(
        Vector2Int a,
        Vector2Int b,
        IGridQuery grid)
    {
        Vector2Int delta = b - a;

        // bước thẳng
        if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1)
        {
            return !grid.CanMove(a, delta);
        }

        // bước chéo
        if (Mathf.Abs(delta.x) == 1 && Mathf.Abs(delta.y) == 1)
        {
            Vector2Int stepX = new Vector2Int(delta.x, 0);
            Vector2Int stepY = new Vector2Int(0, delta.y);

            bool path1 =
                grid.CanMove(a, stepX) &&
                grid.CanMove(a + stepX, stepY);

            bool path2 =
                grid.CanMove(a, stepY) &&
                grid.CanMove(a + stepY, stepX);

            return !path1 && !path2;
        }

        return true;
    }

    private List<Vector2Int> GetSupercoverCells(Vector2Int from, Vector2Int to)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        int x0 = from.x;
        int y0 = from.y;
        int x1 = to.x;
        int y1 = to.y;

        int dx = x1 - x0;
        int dy = y1 - y0;

        int nx = Mathf.Abs(dx);
        int ny = Mathf.Abs(dy);

        int signX = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        int signY = dy == 0 ? 0 : (dy > 0 ? 1 : -1);

        int x = x0;
        int y = y0;

        result.Add(new Vector2Int(x, y));

        int ix = 0;
        int iy = 0;

        while (ix < nx || iy < ny)
        {
            float tx = nx == 0 ? float.PositiveInfinity : (0.5f + ix) / nx;
            float ty = ny == 0 ? float.PositiveInfinity : (0.5f + iy) / ny;

            if (tx < ty)
            {
                x += signX;
                ix++;
            }
            else if (ty < tx)
            {
                y += signY;
                iy++;
            }
            else
            {
                x += signX;
                y += signY;
                ix++;
                iy++;
            }

            Vector2Int p = new Vector2Int(x, y);
            if (!result.Contains(p))
                result.Add(p);
        }

        return result;
    }

    public Vector2Int GetTargetByMode(
        GhostMode mode,
        Vector2Int logicPos,
        GhostBehavior behavior,
        Vector2Int scatterCorner,
        IGridQuery grid)
    {
        switch (mode)
        {
            case GhostMode.Chase:
                return behavior != null ? behavior.GetTarget() : logicPos;

            case GhostMode.Scatter:
                return scatterCorner;

            case GhostMode.Frightened:
                return grid.GetRandomWalkableTile();

            case GhostMode.Support:
                return logicPos;

            default:
                return logicPos;
        }
    }
}