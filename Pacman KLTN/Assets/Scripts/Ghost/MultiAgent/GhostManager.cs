using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GhostManager : MonoBehaviour
{
    public static GhostManager Instance;
    public List<GhostAI> ghosts;

    [Header("Gameplay Partition")]
    public bool useBfsPartition = false;
    public bool useSoftBfsPartition = false;
    [Min(1)] public int softBoundaryRadius = 2;
    [Min(0)] public int partitionOverlap = 1;

    public SharedWorldState worldState = new SharedWorldState();
    private Region[] regions;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Rebuild partition after 1 frame to ensure all ghost positions are initialized
        StartCoroutine(RebuildPartitionAfterStartFrame());
    }

    void Update()
    {
        UpdateWorldState();
    }

    void UpdateWorldState()
    {
        worldState.UpdateStepTick();
        foreach (var ghost in ghosts)
        {
            worldState.UpdateGhost(ghost.ID, ghost.GridPosition);
        }
    }

    public void EnterCapturePhase(Vector2Int pacmanPos)
    {
        worldState.UpdatePacmanLastKnown(pacmanPos);

        foreach (var ghost in ghosts)
        {
            ghost.EnterCapturePhase(pacmanPos);
        }
    }

    public void EnterSearchPhase()
    {
        worldState.ClearPacmanKnowledge();

        foreach (var ghost in ghosts)
        {
            ghost.EnterSearchPhase();
        }
    }

    private IEnumerator RebuildPartitionAfterStartFrame()
    {
        yield return null;
        InitRegions();
        worldState.ClearReservations();
    }

    public void InitRegions()
    {
        int ghostCount = ghosts != null ? ghosts.Count : 0;
        if (ghostCount <= 0)
        {
            regions = new Region[0];
            return;
        }

        if (useBfsPartition)
        {
            regions = BuildBfsRegions();
        }
        else
        {
            regions = BuildXRegions(GridManager.Instance.GridSize.x, ghostCount, partitionOverlap);
        }

        for (int i = 0; i < ghostCount; i++)
        {
            ghosts[i].Agent.region = regions[i];
        }
    }

    private Region[] BuildXRegions(int mapWidth, int count, int overlap)
    {
        Region[] result = new Region[count];
        int regionWidth = Mathf.Max(1, mapWidth / count);

        for (int i = 0; i < count; i++)
        {
            int minX = i * regionWidth;
            int maxX = (i == count - 1) ? mapWidth - 1 : ((i + 1) * regionWidth - 1);

            minX = Mathf.Max(0, minX - overlap);
            maxX = Mathf.Min(mapWidth - 1, maxX + overlap);

            result[i] = new Region
            {
                minX = minX,
                maxX = maxX
            };
        }

        return result;
    }

    private Region[] BuildBfsRegions()
    {
        IGridQuery grid = new UnityGridQuery();
        int ghostTotal = ghosts.Count;
        Region[] fallback = BuildXRegions(grid.Width, ghostTotal, partitionOverlap);

        Debug.Log($"[BFS] Starting BFS partition: {ghostTotal} ghosts, grid {grid.Width}x{grid.Height}");

        int[,] ownerMap = new int[grid.Width, grid.Height];
        HashSet<Vector2Int>[] ownedCells = new HashSet<Vector2Int>[ghostTotal];
        List<Vector2Int> seeds = new List<Vector2Int>(ghostTotal);
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
                ownerMap[x, y] = -1;
        }

        // Collect seeds from ghost positions
        int walkaableSeeds = 0;
        for (int i = 0; i < ghostTotal; i++)
        {
            ownedCells[i] = new HashSet<Vector2Int>();

            Vector2 worldPos = ghosts[i].transform.position;
            Vector2Int seed = GridManager.Instance.WorldToLogic(ghosts[i].transform.position);
            seeds.Add(seed);

            bool walkable = grid.IsWalkable(seed);
            Debug.Log($"[BFS] Ghost {i}: world {worldPos} -> logic {seed}, walkable={walkable}");

            if (!walkable)
                continue;

            ownerMap[seed.x, seed.y] = i;
            ownedCells[i].Add(seed);
            queue.Enqueue(seed);
            walkaableSeeds++;
        }

        Debug.Log($"[BFS] Seeds: {walkaableSeeds}/{ghostTotal} walkable, queue size {queue.Count}");

        // Multi-source BFS expansion
        int bfsExpanded = 0;
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int currentOwner = ownerMap[current.x, current.y];

            foreach (Vector2Int neighbor in grid.GetNeighbors(current))
            {
                if (ownerMap[neighbor.x, neighbor.y] != -1)
                    continue;

                ownerMap[neighbor.x, neighbor.y] = currentOwner;
                ownedCells[currentOwner].Add(neighbor);
                queue.Enqueue(neighbor);
                bfsExpanded++;
            }
        }

        Debug.Log($"[BFS] BFS expanded {bfsExpanded} cells");

        // Assign unreachable cells to nearest seed
        int manhattanAssigned = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);

                if (!grid.IsWalkable(cell))
                    continue;

                if (ownerMap[x, y] != -1)
                    continue;

                int bestOwner = 0;
                int bestDist = int.MaxValue;

                for (int i = 0; i < seeds.Count; i++)
                {
                    Vector2Int seed = seeds[i];
                    int dist = Mathf.Abs(x - seed.x) + Mathf.Abs(y - seed.y);

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestOwner = i;
                    }
                }

                ownerMap[x, y] = bestOwner;
                ownedCells[bestOwner].Add(cell);
                manhattanAssigned++;
            }
        }

        Debug.Log($"[BFS] Manhattan fallback assigned {manhattanAssigned} cells");

        HashSet<Vector2Int>[] extendedCells = useSoftBfsPartition
            ? BuildSoftBoundaries(grid, ownedCells)
            : null;

        Region[] regionsResult = new Region[ghostTotal];
        System.Text.StringBuilder cellDist = new System.Text.StringBuilder();

        for (int i = 0; i < ghostTotal; i++)
        {
            if (ownedCells[i].Count == 0)
            {
                Debug.LogError($"[BFS] Ghost {i} has 0 cells! Falling back to X-partition");
                return fallback;
            }

            int minX = grid.Width - 1;
            int maxX = 0;

            foreach (Vector2Int cell in ownedCells[i])
            {
                if (cell.x < minX)
                    minX = cell.x;

                if (cell.x > maxX)
                    maxX = cell.x;
            }

            cellDist.Append($"G{i}:{ownedCells[i].Count} ");

            regionsResult[i] = new Region
            {
                minX = minX,
                maxX = maxX,
                ownedCells = ownedCells[i],
                extendedCells = extendedCells != null ? extendedCells[i] : null
            };
        }

        Debug.Log($"[BFS] SUCCESS - Cell distribution: {cellDist.ToString()}");
        return regionsResult;
    }

    private HashSet<Vector2Int>[] BuildSoftBoundaries(IGridQuery grid, HashSet<Vector2Int>[] ownedCells)
    {
        HashSet<Vector2Int>[] extendedCells = new HashSet<Vector2Int>[ownedCells.Length];

        for (int i = 0; i < ownedCells.Length; i++)
        {
            HashSet<Vector2Int> boundary = new HashSet<Vector2Int>();

            foreach (Vector2Int owned in ownedCells[i])
            {
                for (int dx = -softBoundaryRadius; dx <= softBoundaryRadius; dx++)
                {
                    for (int dy = -softBoundaryRadius; dy <= softBoundaryRadius; dy++)
                    {
                        if (Mathf.Abs(dx) + Mathf.Abs(dy) > softBoundaryRadius)
                            continue;

                        Vector2Int candidate = new Vector2Int(owned.x + dx, owned.y + dy);

                        if (!grid.IsWalkable(candidate))
                            continue;

                        if (ownedCells[i].Contains(candidate))
                            continue;

                        boundary.Add(candidate);
                    }
                }
            }

            extendedCells[i] = boundary;
        }

        return extendedCells;
    }
}

public struct Region
{
    public int minX;
    public int maxX;
    public HashSet<Vector2Int> ownedCells;
    public HashSet<Vector2Int> extendedCells;
}