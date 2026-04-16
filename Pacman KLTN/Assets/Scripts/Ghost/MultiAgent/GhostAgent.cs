using UnityEngine;

[System.Serializable]
public class GhostAgent
{
    public Region region;

    public void UpdateSharedState(SharedWorldState worldState, Vector2Int logicPos)
    {
        worldState.MarkVisited(logicPos);
    }

    public bool InRegion(Vector2Int pos)
    {
        return pos.x >= region.minX && pos.x <= region.maxX;
    }

    public Vector2Int FindBestTarget(
        Vector2Int logicPos,
        SharedWorldState worldState,
        IGridQuery grid)
    {
        Vector2Int best = logicPos;
        float bestScore = float.MaxValue;

        for (int x = region.minX; x <= region.maxX; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);

                if (!grid.IsWalkable(pos))
                    continue;

                if (worldState.reservedTargets.ContainsKey(pos))
                    continue;

                float score = ComputeScore(logicPos, pos, worldState);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = pos;
                }
            }
        }

        return best;
    }

    public float ComputeScore(
        Vector2Int from,
        Vector2Int to,
        SharedWorldState worldState)
    {
        int visit = worldState.visitTimes[to.x, to.y];
        int age = worldState.CurrentStep - visit;
        float dist = Vector2Int.Distance(from, to);

        // ông có thể chỉnh tham số sau
        return dist - age * 2f;
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