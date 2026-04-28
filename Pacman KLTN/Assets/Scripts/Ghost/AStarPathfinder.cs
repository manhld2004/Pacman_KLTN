using System.Collections.Generic;
using UnityEngine;

public static class AStarPathfinder
{
    struct PathKey
    {
        public Vector2Int start;
        public Vector2Int goal;

        public PathKey(Vector2Int s, Vector2Int g)
        {
            start = s;
            goal = g;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is PathKey)) return false;
            PathKey other = (PathKey)obj;
            return start == other.start && goal == other.goal;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (start.GetHashCode() * 397) ^ goal.GetHashCode();
            }
        }
    }

    static Dictionary<PathKey, List<Vector2Int>> cache =
        new Dictionary<PathKey, List<Vector2Int>>();

    static int maxCacheSize = 200;

    class Node
    {
        public Vector2Int pos;
        public int g;
        public int h;
        public int f => g + h;
        public Node parent;

        public Node(Vector2Int p, int g, int h, Node parent)
        {
            pos = p;
            this.g = g;
            this.h = h;
            this.parent = parent;
        }
    }

    static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    public static List<Vector2Int> FindPath(
        Vector2Int start,
        Vector2Int goal
    )
    {
        PathKey key = new PathKey(start, goal);

        // ===== CACHE HIT =====
        if (cache.TryGetValue(key, out var cachedPath))
        {
            return new List<Vector2Int>(cachedPath);
        }

        // ===== CACHE MISS =====
        List<Vector2Int> path = FindPathInternal(start, goal);

        if (path != null)
        {
            // DEBUG: Validate path doesn't have diagonals
            if (path.Count > 1)
            {
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2Int from = path[i];
                    Vector2Int to = path[i + 1];
                    Vector2Int step = to - from;
                    if (step.sqrMagnitude != 1)
                    {
                        Debug.LogError($"[A* BUG] Generated diagonal step {i}->{i+1}: {from} -> {to} (step={step})");
                    }
                }
            }
            AddToCache(key, path);
        }

        return path;
    }

    public static void ClearCache()
    {
        cache.Clear();
    }

    // =======================
    // A* ALGORITHM

    static List<Vector2Int> FindPathInternal(
        Vector2Int start,
        Vector2Int goal
    )
    {
        var open = new List<Node>();
        var closed = new HashSet<Vector2Int>();

        open.Add(new Node(start, 0, Heuristic(start, goal), null));

        while (open.Count > 0)
        {
            // chọn node có f nhỏ nhất
            Node current = open[0];
            for (int i = 1; i < open.Count; i++)
            {
                if (open[i].f < current.f)
                    current = open[i];
            }

            open.Remove(current);
            closed.Add(current.pos);

            if (current.pos == goal)
                return ReconstructPath(current);

            foreach (var dir in Directions)
            {
                if (!GridManager.Instance.CanMove(current.pos, dir))
                    continue;

                Vector2Int nextPos = current.pos + dir;

                if (closed.Contains(nextPos))
                    continue;

                int newG = current.g + 1;

                Node existing = open.Find(n => n.pos == nextPos);

                if (existing == null)
                {
                    open.Add(new Node(
                        nextPos,
                        newG,
                        Heuristic(nextPos, goal),
                        current
                    ));
                }
                else if (newG < existing.g)
                {
                    existing.g = newG;
                    existing.parent = current;
                }
            }
        }

        return null;
    }

    static int Heuristic(Vector2Int a, Vector2Int b)
    {
        // Manhattan distance (4-direction grid)
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    static List<Vector2Int> ReconstructPath(Node node)
    {
        var path = new List<Vector2Int>();

        while (node != null)
        {
            path.Add(node.pos);
            node = node.parent;
        }

        path.Reverse();
        return path;
    }

    static void AddToCache(PathKey key, List<Vector2Int> path)
    {
        if (cache.Count >= maxCacheSize)
        {
            cache.Clear();
        }

        cache[key] = new List<Vector2Int>(path);
    }
}
