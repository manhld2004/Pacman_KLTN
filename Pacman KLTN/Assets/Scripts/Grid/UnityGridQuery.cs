using System.Collections.Generic;
using UnityEngine;

public class UnityGridQuery : IGridQuery
{
    public int Width => GridManager.Instance.GridSize.x;
    public int Height => GridManager.Instance.GridSize.y;

    public bool IsWalkable(Vector2Int pos)
    {
        return GridManager.Instance.IsWalkable(pos);
    }

    public bool CanMove(Vector2Int from, Vector2Int dir)
    {
        return GridManager.Instance.CanMove(from, dir);
    }

    public List<Vector2Int> GetNeighbors(Vector2Int pos)
    {
        return GridManager.Instance.GetNeighbors(pos);
    }

    public Vector2Int GetRandomWalkableTile()
    {
        return GridManager.Instance.GetRandomWalkableTile();
    }

    public bool IsHardBlocked(Vector2Int pos)
    {
        return GridManager.Instance.IsHardBlocked(pos);
    }
}