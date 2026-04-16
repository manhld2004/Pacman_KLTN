using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Tilemaps;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance;

    [Header("Tilemaps")]
    public Tilemap groundTilemap;
    public Tilemap wallTilemap;

    // Bounds thật sau khi nén
    private BoundsInt groundBounds;

    // Gốc logic (dưới-trái)
    private Vector3Int origin;

    // Kích thước logic map
    public Vector2Int GridSize { get; private set; }

    void Awake()
    {
        Instance = this;

        // 1. Nén bounds
        groundTilemap.CompressBounds();
        groundBounds = groundTilemap.cellBounds;

        // 2. Gốc logic = min bounds
        origin = groundBounds.min;

        // 3. Size logic
        GridSize = new Vector2Int(
            groundBounds.size.x,
            groundBounds.size.y
        );
    }

    void Start()
    {
        GhostManager.Instance.worldState.Initialize(GridSize.x, GridSize.y);
        GhostManager.Instance.InitRegions();
    }

    // LOGIC 

    public bool IsWalkable(Vector2Int logicPos)
    {
        // Ngoài map logic
        if (logicPos.x < 0 || logicPos.y < 0 ||
            logicPos.x >= GridSize.x || logicPos.y >= GridSize.y)
            return false;

        Vector3Int cell = LogicToCell(logicPos);

        // Không có ground → không đi được
        if (!groundTilemap.HasTile(cell))
            return false;

        // Có wall → chặn
        if (wallTilemap != null && wallTilemap.HasTile(cell) && GetWallTile(cell).isBlock)
            return false;

        return true;
    }

    public bool CanMove(Vector2Int from, Vector2Int dir)
    {
        Vector2Int to = from + dir;

        // ngoài map
        if (to.x < 0 || to.y < 0 ||
            to.x >= GridSize.x || to.y >= GridSize.y)
            return false;

        Vector3Int fromCell = LogicToCell(from);
        Vector3Int toCell   = LogicToCell(to);

        // không có ground ở ô đích
        if (!groundTilemap.HasTile(toCell))
            return false;

        WallTile fromWall = GetWallTile(fromCell);
        WallTile toWall   = GetWallTile(toCell);

        // block cứng
        if ((fromWall && fromWall.isBlock) ||
            (toWall && toWall.isBlock))
            return false;

        // kiểm tra edge
        if (dir == Vector2Int.up)
        {
            if ((fromWall && fromWall.bTop) ||
                (toWall && toWall.bBottom))
                return false;
        }
        else if (dir == Vector2Int.down)
        {
            if ((fromWall && fromWall.bBottom) ||
                (toWall && toWall.bTop))
                return false;
        }
        else if (dir == Vector2Int.left)
        {
            if ((fromWall && fromWall.bLeft) ||
                (toWall && toWall.bRight))
                return false;
        }
        else if (dir == Vector2Int.right)
        {
            if ((fromWall && fromWall.bRight) ||
                (toWall && toWall.bLeft))
                return false;
        }

        return true;
    }

    public List<Vector2Int> GetNeighbors(Vector2Int pos)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        foreach (var dir in Directions)
        {
            if (CanMove(pos, dir))
            {
                result.Add(pos + dir);
            }
        }

        return result;
    }

    #region GridLogic Helpers

    WallTile GetWallTile(Vector3Int cell)
    {
        if (!wallTilemap.HasTile(cell))
            return null;

        return wallTilemap.GetTile(cell) as WallTile;
    }


    public Vector3Int LogicToCell(Vector2Int logicPos)
    {
        return new Vector3Int(
            logicPos.x + origin.x,
            logicPos.y + origin.y,
            0
        );
    }

    public Vector2Int CellToLogic(Vector3Int cellPos)
    {
        return new Vector2Int(
            cellPos.x - origin.x,
            cellPos.y - origin.y
        );
    }

    public Vector2 LogicToWorld(Vector2Int logicPos)
    {
        Vector3 world = groundTilemap.CellToWorld(
            LogicToCell(logicPos)
        );
        return world + groundTilemap.cellSize * 0.5f;
    }

    public Vector2Int WorldToLogic(Vector2 worldPos)
    {
        Vector3Int cell = groundTilemap.WorldToCell(worldPos);
        return CellToLogic(cell);
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        Vector3Int cell = new Vector3Int(
            gridPos.x + origin.x,
            gridPos.y + origin.y,
            0
        );

        Vector3 world = groundTilemap.GetCellCenterWorld(cell);
        return world;
    }

    public Vector2Int GetRandomWalkableTile()
    {
        // tránh loop vô hạn
        for (int i = 0; i < 100; i++)
        {
            int x = Random.Range(0, groundBounds.size.x);
            int y = Random.Range(0, groundBounds.size.y);

            Vector2Int pos = new Vector2Int(x, y);

            if (IsWalkable(pos))
                return pos;
        }

        // fallback (hiếm khi xảy ra)
        return PlayStateManager.Instance.Pacman.LogicPos;
    }


    public Vector2Int GetRandomWalkableTile(Vector2Int avoidPos)
    {
        for (int i = 0; i < 100; i++)
        {
            Vector2Int pos = GetRandomWalkableTile();
            if (pos != avoidPos)
                return pos;
        }
        return avoidPos;
    }

    public static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

    #endregion

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (groundTilemap == null) return;

        Gizmos.color = Color.cyan;

        for (int x = 0; x < GridSize.x; x++)
        {
            for (int y = 0; y < GridSize.y; y++)
            {
                Vector2 world = LogicToWorld(new Vector2Int(x, y));
                Gizmos.DrawWireCube(world, Vector3.one * 0.9f);
            }
        }
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying == false) return;

        var worldState = GhostManager.Instance.worldState;

        if (worldState == null || worldState.visitTimes == null)
            return;

        Vector2Int size = GridSize;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                int visit = worldState.visitTimes[x, y];
                int age = worldState.CurrentStep - visit;

                // clamp age
                float t = Mathf.Clamp01(age / 4000f);

                // màu từ xanh → đỏ
                Color color = Color.Lerp(Color.green, Color.red, t);

                Gizmos.color = color;

                Vector3 pos = GridToWorld(new Vector2Int(x, y));

                Gizmos.DrawCube(pos, Vector3.one * 0.6f);
                
                //UnityEditor.Handles.Label(pos, age.ToString());
            }
        }   
    }
    

#endif
}

public static class DirectExtension
{
    public static bool IsRight(this Vector2Int dir)
    {
        return dir == Vector2Int.right;
    }

    public static bool IsLeft(this Vector2Int dir)
    {
        return dir == Vector2Int.left;
    }

    public static bool IsUp(this Vector2Int dir)
    {
        return dir == Vector2Int.up;
    }

    public static bool IsDown(this Vector2Int dir)
    {
        return dir == Vector2Int.down;
    }

    public static Vector3 WithY(this Vector3 v, float y)
    {
        return new Vector3(v.x, y, v.z);
    }

    public static Vector3 WithX(this Vector3 v, float x)
    {
        return new Vector3(x, v.y, v.z);
    }
}
