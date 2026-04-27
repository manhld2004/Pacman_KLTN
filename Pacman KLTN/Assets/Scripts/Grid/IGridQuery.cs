using System.Collections.Generic;
using UnityEngine;

public interface IGridQuery
{
    int Width { get; }
    int Height { get; }

    bool IsWalkable(Vector2Int pos);
    bool CanMove(Vector2Int from, Vector2Int dir);
    List<Vector2Int> GetNeighbors(Vector2Int pos);
    Vector2Int GetRandomWalkableTile();

    bool IsHardBlocked(Vector2Int pos);
}