using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class GhostSimulationRunner : MonoBehaviour
{
    [Header("Run")]
    public bool runOnStart = true;
    public int episodeCount = 20;
    public int maxStepsPerEpisode = 3000;
    public int randomSeed = 12345;

    [Header("CSV Export")]
    public bool exportCsv = false;
    public string csvFileName = "ghost_simulation_results.csv";

    [Header("Map")]
    public int width = 30;
    public int height = 20;
    [Range(0f, 0.35f)] public float wallRate = 0.10f;
    public bool generateRandomMap = true;

    [Header("Agents")]
    public int ghostCount = 3;
    public int visionRange = 4;
    public bool useXPartition = true;
    public int partitionOverlap = 1;

    [Header("Search Scoring")]
    public float distanceWeight = 1f;
    public float ageWeight = 2f;

    [Header("Pacman")]
    public PacmanMoveMode pacmanMoveMode = PacmanMoveMode.RandomWalk;

    [Header("Log")]
    public bool logEachEpisode = true;
    public bool logSummary = true;

    public enum PacmanMoveMode
    {
        RandomWalk,
        GreedyEscape
    }

    void Start()
    {
        if (runOnStart)
            RunBatch();
    }

    [ContextMenu("Run Batch Simulation")]
    public void RunBatch()
    {
        BatchMetrics batch = new BatchMetrics();

        for (int i = 0; i < episodeCount; i++)
        {
            int seed = randomSeed + i;
            EpisodeMetrics result = RunEpisode(seed);
            batch.Add(result);

            if (logEachEpisode)
            {
                Debug.Log(
                    $"[Episode {i}] " +
                    $"Captured={result.captured}, " +
                    $"DetectStep={result.detectStep}, " +
                    $"CaptureStep={result.captureStep}, " +
                    $"FullCoverageStep={result.firstFullCoverageStep}, " +
                    $"Coverage={result.finalCoverageRatio:F3}, " +
                    $"MaxCellAge={result.maxCellAge}, " +
                    $"AvgCellAge={result.avgCellAge:F2}, " +
                    $"MeanRevisit={result.meanRevisitInterval:F2}"
                );
            }
        }

        if (logSummary)
            Debug.Log(batch.BuildSummary());

        if (exportCsv)
        {
            string path = Path.Combine(Application.dataPath, csvFileName);
            File.WriteAllText(path, batch.ToCsv());
            Debug.Log("CSV exported: " + path);
        }
    }

    EpisodeMetrics RunEpisode(int seed)
    {
        System.Random rng = new System.Random(seed);
        SimState state = BuildState(rng);

        EpisodeMetrics metrics = new EpisodeMetrics
        {
            seed = seed
        };

        // đánh dấu tile ban đầu của ghost
        foreach (var ghost in state.ghosts)
            MarkVisited(state, ghost.pos);

        for (int step = 1; step <= maxStepsPerEpisode; step++)
        {
            state.currentStep = step;
            state.worldState.SetStep(step);
            state.worldState.reservedTargets.Clear();

            // 1. Pacman move
            StepPacman(state, rng);

            // 2. Ghost move
            foreach (var ghost in state.ghosts)
            {
                bool detected = Manhattan(ghost.pos, state.pacmanPos) <= visionRange;
                if (detected && metrics.detectStep < 0)
                    metrics.detectStep = step;

                ghost.Step(state, distanceWeight, ageWeight, useXPartition);

                MarkVisited(state, ghost.pos);
            }

            // 3. Coverage metric
            float coverage = CalculateCoverageRatio(state);
            metrics.finalCoverageRatio = coverage;

            if (metrics.firstFullCoverageStep < 0 && coverage >= 0.999f)
                metrics.firstFullCoverageStep = step;

            // 4. Capture
            if (CheckCapture(state))
            {
                metrics.captured = true;
                metrics.captureStep = step;
                break;
            }
        }

        FinalizeMetrics(state, metrics);
        return metrics;
    }

    SimState BuildState(System.Random rng)
    {
        SimState state = new SimState(width, height);

        if (generateRandomMap)
            GenerateRandomMap(state, rng);
        else
            GenerateEmptyMap(state);

        EnsureConnectivity(state, rng);

        // world state
        state.worldState.Initialize(width, height);

        // pacman
        state.pacmanPos = GetRandomWalkable(state, rng);

        // visit arrays
        state.visitCount = new int[width, height];

        // zones
        List<Region> regions = BuildRegions(width, ghostCount, partitionOverlap);

        // ghosts
        for (int i = 0; i < ghostCount; i++)
        {
            GhostAgent logic = new GhostAgent();
            logic.region = regions[Mathf.Min(i, regions.Count - 1)];

            SimGhost ghost = new SimGhost
            {
                id = i,
                pos = GetRandomWalkableInRegion(state, logic.region, rng),
                currentTarget = Vector2Int.one * -999,
                logic = logic
            };

            state.ghosts.Add(ghost);
        }

        return state;
    }

    void GenerateEmptyMap(SimState state)
    {
        for (int x = 0; x < state.width; x++)
        for (int y = 0; y < state.height; y++)
            state.walkable[x, y] = true;
    }

    void GenerateRandomMap(SimState state, System.Random rng)
    {
        for (int x = 0; x < state.width; x++)
        {
            for (int y = 0; y < state.height; y++)
            {
                bool border = (x == 0 || y == 0 || x == state.width - 1 || y == state.height - 1);

                if (border)
                {
                    state.walkable[x, y] = false;
                }
                else
                {
                    state.walkable[x, y] = rng.NextDouble() > wallRate;
                }
            }
        }
    }

    void EnsureConnectivity(SimState state, System.Random rng)
    {
        Vector2Int start = GetRandomWalkable(state, rng);
        HashSet<Vector2Int> reachable = FloodFill(state, start);

        for (int x = 0; x < state.width; x++)
        {
            for (int y = 0; y < state.height; y++)
            {
                Vector2Int p = new Vector2Int(x, y);
                if (state.walkable[x, y] && !reachable.Contains(p))
                    state.walkable[x, y] = false;
            }
        }
    }

    HashSet<Vector2Int> FloodFill(SimState state, Vector2Int start)
    {
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        q.Enqueue(start);
        visited.Add(start);

        while (q.Count > 0)
        {
            Vector2Int cur = q.Dequeue();

            foreach (var n in state.grid.GetNeighbors(cur))
            {
                if (!visited.Contains(n))
                {
                    visited.Add(n);
                    q.Enqueue(n);
                }
            }
        }

        return visited;
    }

    void StepPacman(SimState state, System.Random rng)
    {
        if (pacmanMoveMode == PacmanMoveMode.RandomWalk)
        {
            state.pacmanPos = RandomMove(state, state.pacmanPos, rng);
            return;
        }

        // Greedy escape
        List<Vector2Int> neighbors = state.grid.GetNeighbors(state.pacmanPos);
        if (neighbors.Count == 0) return;

        float bestScore = float.MinValue;
        Vector2Int best = state.pacmanPos;

        foreach (var n in neighbors)
        {
            float minDist = float.MaxValue;
            foreach (var ghost in state.ghosts)
            {
                float d = Manhattan(n, ghost.pos);
                if (d < minDist) minDist = d;
            }

            if (minDist > bestScore)
            {
                bestScore = minDist;
                best = n;
            }
        }

        state.pacmanPos = best;
    }

    Vector2Int RandomMove(SimState state, Vector2Int pos, System.Random rng)
    {
        List<Vector2Int> neighbors = state.grid.GetNeighbors(pos);
        if (neighbors.Count == 0) return pos;
        return neighbors[rng.Next(neighbors.Count)];
    }

    void MarkVisited(SimState state, Vector2Int pos)
    {
        state.worldState.visitTimes[pos.x, pos.y] = state.currentStep;
        state.visitCount[pos.x, pos.y] += 1;
    }

    float CalculateCoverageRatio(SimState state)
    {
        int walkableCount = 0;
        int visitedCount = 0;

        for (int x = 0; x < state.width; x++)
        {
            for (int y = 0; y < state.height; y++)
            {
                if (!state.walkable[x, y]) continue;

                walkableCount++;
                if (state.visitCount[x, y] > 0)
                    visitedCount++;
            }
        }

        if (walkableCount == 0) return 0f;
        return (float)visitedCount / walkableCount;
    }

    bool CheckCapture(SimState state)
    {
        foreach (var ghost in state.ghosts)
        {
            if (ghost.pos == state.pacmanPos)
                return true;
        }
        return false;
    }

    void FinalizeMetrics(SimState state, EpisodeMetrics metrics)
    {
        int walkableCount = 0;
        int maxAge = 0;
        float ageSum = 0f;

        float revisitSum = 0f;
        int revisitCount = 0;

        for (int x = 0; x < state.width; x++)
        {
            for (int y = 0; y < state.height; y++)
            {
                if (!state.walkable[x, y]) continue;

                walkableCount++;

                int age = state.currentStep - state.worldState.visitTimes[x, y];
                ageSum += age;
                if (age > maxAge) maxAge = age;

                if (state.visitCount[x, y] > 1)
                {
                    revisitSum += (float)state.currentStep / state.visitCount[x, y];
                    revisitCount++;
                }
            }
        }

        metrics.maxCellAge = maxAge;
        metrics.avgCellAge = walkableCount > 0 ? ageSum / walkableCount : 0f;
        metrics.meanRevisitInterval = revisitCount > 0 ? revisitSum / revisitCount : -1f;
        metrics.totalSteps = state.currentStep;
    }

    List<Region> BuildRegions(int mapWidth, int count, int overlap)
    {
        List<Region> result = new List<Region>();

        int regionWidth = Mathf.Max(1, mapWidth / count);

        for (int i = 0; i < count; i++)
        {
            int minX = i * regionWidth;
            int maxX = (i == count - 1) ? mapWidth - 1 : ((i + 1) * regionWidth - 1);

            minX = Mathf.Max(0, minX - overlap);
            maxX = Mathf.Min(mapWidth - 1, maxX + overlap);

            result.Add(new Region
            {
                minX = minX,
                maxX = maxX
            });
        }

        return result;
    }

    Vector2Int GetRandomWalkable(SimState state, System.Random rng)
    {
        for (int i = 0; i < 1000; i++)
        {
            Vector2Int p = new Vector2Int(rng.Next(0, state.width), rng.Next(0, state.height));
            if (state.grid.IsWalkable(p))
                return p;
        }

        return Vector2Int.one;
    }

    Vector2Int GetRandomWalkableInRegion(SimState state, Region region, System.Random rng)
    {
        for (int i = 0; i < 1000; i++)
        {
            int x = rng.Next(region.minX, region.maxX + 1);
            int y = rng.Next(0, state.height);
            Vector2Int p = new Vector2Int(x, y);

            if (state.grid.IsWalkable(p))
                return p;
        }

        return GetRandomWalkable(state, rng);
    }

    int Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    // =========================================================
    // DATA TYPES
    // =========================================================

    [Serializable]
    public class SimState
    {
        public int width;
        public int height;
        public bool[,] walkable;
        public SimGridQuery grid;

        public SharedWorldState worldState;
        public Vector2Int pacmanPos;
        public List<SimGhost> ghosts = new List<SimGhost>();

        public int[,] visitCount;
        public int currentStep;

        public SimState(int width, int height)
        {
            this.width = width;
            this.height = height;
            this.walkable = new bool[width, height];
            this.grid = new SimGridQuery(this.walkable);
        }
    }

    [Serializable]
    public class SimGhost
    {
        public int id;
        public Vector2Int pos;
        public Vector2Int currentTarget;
        public GhostAgent logic;

        public void Step(SimState state, float distanceWeight, float ageWeight, bool useRegion)
        {
            ClearOldTarget(state.worldState);

            currentTarget = FindBestTargetLocal(state, distanceWeight, ageWeight, useRegion);

            ReserveTarget(state.worldState);

            List<Vector2Int> path = SimPathfinder.FindPath(pos, currentTarget, state.grid);

            if (path != null && path.Count > 1)
            {
                pos = path[1];
                logic.UpdateSharedState(state.worldState, pos);
            }
        }

        Vector2Int FindBestTargetLocal(SimState state, float distanceWeight, float ageWeight, bool useRegion)
        {
            Vector2Int best = pos;
            float bestScore = float.MaxValue;

            for (int x = 0; x < state.width; x++)
            {
                for (int y = 0; y < state.height; y++)
                {
                    Vector2Int p = new Vector2Int(x, y);

                    if (!state.grid.IsWalkable(p))
                        continue;

                    if (useRegion && !logic.InRegion(p))
                        continue;

                    if (state.worldState.reservedTargets.ContainsKey(p))
                        continue;

                    int visit = state.worldState.visitTimes[p.x, p.y];
                    int age = state.worldState.CurrentStep - visit;
                    float dist = Vector2Int.Distance(pos, p);

                    float score = dist * distanceWeight - age * ageWeight;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = p;
                    }
                }
            }

            return best;
        }

        void ClearOldTarget(SharedWorldState worldState)
        {
            if (worldState.reservedTargets.ContainsKey(currentTarget) &&
                worldState.reservedTargets[currentTarget] == id)
            {
                worldState.reservedTargets.Remove(currentTarget);
            }
        }

        void ReserveTarget(SharedWorldState worldState)
        {
            if (!worldState.reservedTargets.ContainsKey(currentTarget))
                worldState.reservedTargets[currentTarget] = id;
        }
    }

    [Serializable]
    public class EpisodeMetrics
    {
        public int seed;
        public bool captured = false;
        public int detectStep = -1;
        public int captureStep = -1;
        public int firstFullCoverageStep = -1;
        public float finalCoverageRatio = 0f;
        public int maxCellAge = 0;
        public float avgCellAge = 0f;
        public float meanRevisitInterval = -1f;
        public int totalSteps = 0;
    }

    public class BatchMetrics
    {
        public List<EpisodeMetrics> results = new List<EpisodeMetrics>();

        public void Add(EpisodeMetrics e)
        {
            results.Add(e);
        }

        public string BuildSummary()
        {
            if (results.Count == 0) return "No results.";

            int capturedCount = 0;
            float detectSum = 0f;
            float captureSum = 0f;
            float coverageSum = 0f;
            float maxAgeSum = 0f;
            float avgCellAgeSum = 0f;
            float revisitSum = 0f;

            int detectCount = 0;
            int captureCount = 0;
            int revisitCount = 0;

            foreach (var r in results)
            {
                if (r.captured) capturedCount++;

                coverageSum += r.finalCoverageRatio;
                maxAgeSum += r.maxCellAge;
                avgCellAgeSum += r.avgCellAge;

                if (r.detectStep >= 0)
                {
                    detectSum += r.detectStep;
                    detectCount++;
                }

                if (r.captureStep >= 0)
                {
                    captureSum += r.captureStep;
                    captureCount++;
                }

                if (r.meanRevisitInterval >= 0)
                {
                    revisitSum += r.meanRevisitInterval;
                    revisitCount++;
                }
            }

            coverageSum /= results.Count;
            maxAgeSum /= results.Count;
            avgCellAgeSum /= results.Count;
            if (detectCount > 0) detectSum /= detectCount;
            if (captureCount > 0) captureSum /= captureCount;
            if (revisitCount > 0) revisitSum /= revisitCount;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("===== GHOST SIMULATION SUMMARY =====");
            sb.AppendLine($"Episodes: {results.Count}");
            sb.AppendLine($"Capture Rate: {(float)capturedCount / results.Count:P2}");
            sb.AppendLine($"Avg Detect Step: {(detectCount > 0 ? detectSum.ToString("F2") : "N/A")}");
            sb.AppendLine($"Avg Capture Step: {(captureCount > 0 ? captureSum.ToString("F2") : "N/A")}");
            sb.AppendLine($"Avg Coverage Ratio: {coverageSum:F3}");
            sb.AppendLine($"Avg Max Cell Age: {maxAgeSum:F2}");
            sb.AppendLine($"Avg Cell Age: {avgCellAgeSum:F2}");
            sb.AppendLine($"Avg Mean Revisit Interval: {(revisitCount > 0 ? revisitSum.ToString("F2") : "N/A")}");
            return sb.ToString();
        }

        public string ToCsv()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("seed,captured,detectStep,captureStep,firstFullCoverageStep,finalCoverageRatio,maxCellAge,avgCellAge,meanRevisitInterval,totalSteps");

            foreach (var r in results)
            {
                sb.AppendLine(
                    $"{r.seed},{r.captured},{r.detectStep},{r.captureStep},{r.firstFullCoverageStep}," +
                    $"{r.finalCoverageRatio:F4},{r.maxCellAge},{r.avgCellAge:F4},{r.meanRevisitInterval:F4},{r.totalSteps}"
                );
            }

            return sb.ToString();
        }
    }
}

public class SimGridQuery : IGridQuery
{
    private bool[,] walkable;
    private int width;
    private int height;

    public int Width => width;
    public int Height => height;

    public SimGridQuery(bool[,] walkable)
    {
        this.walkable = walkable;
        this.width = walkable.GetLength(0);
        this.height = walkable.GetLength(1);
    }

    public bool IsWalkable(Vector2Int pos)
    {
        if (pos.x < 0 || pos.y < 0 || pos.x >= width || pos.y >= height)
            return false;

        return walkable[pos.x, pos.y];
    }

    public bool CanMove(Vector2Int from, Vector2Int dir)
    {
        Vector2Int to = from + dir;
        return IsWalkable(to);
    }

    public List<Vector2Int> GetNeighbors(Vector2Int pos)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        Vector2Int[] dirs =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (var dir in dirs)
        {
            Vector2Int next = pos + dir;
            if (IsWalkable(next))
                result.Add(next);
        }

        return result;
    }

    public Vector2Int GetRandomWalkableTile()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (walkable[x, y])
                    return new Vector2Int(x, y);
            }
        }

        return Vector2Int.zero;
    }
}

public static class SimPathfinder
{
    public static List<Vector2Int> FindPath(
        Vector2Int start,
        Vector2Int goal,
        IGridQuery grid)
    {
        if (!grid.IsWalkable(start) || !grid.IsWalkable(goal))
            return null;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(start);
        visited.Add(start);

        bool found = false;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            if (current == goal)
            {
                found = true;
                break;
            }

            foreach (var next in grid.GetNeighbors(current))
            {
                if (!visited.Contains(next))
                {
                    visited.Add(next);
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
        }

        if (!found)
            return null;

        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int p = goal;
        path.Add(p);

        while (p != start)
        {
            p = parent[p];
            path.Add(p);
        }

        path.Reverse();
        return path;
    }
}