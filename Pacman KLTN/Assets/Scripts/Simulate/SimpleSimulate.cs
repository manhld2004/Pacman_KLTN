using UnityEngine;
using UnityEngine.Tilemaps;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class SimpleSimulate : MonoBehaviour
{
    [Header("Simulation")]
    public bool runOnStart = false;
    public int episodeCount = 10;
    public int randomSeed = 0;
    public int maxStepsPerEpisode = 1000;

    [Header("Tilemap Source")]
    public Tilemap groundTilemapSource;
    public Tilemap wallTilemapSource;
    public string groundTilemapName = "Ground";
    public string wallTilemapName = "Walls";
    public bool usePrefabFallbackWhenSceneTilemapMissing = false;
    public GameObject tilemapPrefab;

    [Header("Export")]
    public bool exportCsv = true;
    public string csvFileName = "simulation_results.csv";

    [Header("Agents")]
    public int ghostCount = 3;
    public int visionRange = 4;
    public bool useXPartition = true;
    public int partitionOverlap = 1;

    [Header("Capture Recovery")]
    public int captureLostSightSteps = 4;

    [Header("Search Scoring")]
    public float distanceWeight = 1f;
    public float ageWeight = 2f;

    [Header("Pacman")]
    public PacmanMoveMode pacmanMoveMode = PacmanMoveMode.RandomWalk;

    [Header("Scene Preset (Required)")]
    public GameObject pacmanObject;
    public GameObject[] ghostObjects;

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
        int captureLostSightCounter = 0;

        EpisodeMetrics metrics = new EpisodeMetrics
        {
            seed = seed
        };

        // Ä‘Ã¡nh dáº¥u tile ban Ä‘áº§u cá»§a ghost
        foreach (var ghost in state.ghosts)
        {
            state.worldState.UpdateGhost(ghost.id, ghost.pos);
            MarkVisited(state, ghost.pos);
        }

        for (int step = 1; step <= maxStepsPerEpisode; step++)
        {
            state.currentStep = step;
            state.worldState.UpdateStepTick();
            state.worldState.reservedTargets.Clear();
            
            // CHECK CAPTURE BEFORE MOVEMENT (to ensure gap between detect and capture)
            if (CheckCapture(state))
            {
                metrics.captured = true;
                metrics.captureStep = step;
                if (logEachEpisode && seed % 10 == 0)
                    Debug.Log($"[Step {step}] CAPTURED! Pacman={state.pacmanPos}, Gap={metrics.gap}");
                break;
            }

            // 1. Pacman move - STORE OLD POSITION FOR LOGGING
            Vector2Int pacmanOldPos = state.pacmanPos;
            StepPacman(state, rng);
            Vector2Int pacmanNewPos = state.pacmanPos;
            
            // DEBUG: Log first 5 steps
            if (step <= 5 && logEachEpisode && seed % 10 == 0)
            {
                Debug.Log($"[Step {step}] Pacman: {pacmanOldPos}->{pacmanNewPos}, " +
                    $"Ghost0: {state.ghosts[0].pos}");
            }

            // 2. Ghost move
            bool anyGhostDetected = false;

            foreach (var ghost in state.ghosts)
            {
                state.worldState.UpdateGhost(ghost.id, ghost.pos);
            }

            foreach (var ghost in state.ghosts)
            {
                bool detected = ghost.Step(state, distanceWeight, ageWeight, useXPartition, visionRange);

                // Track metrics
                state.totalGhostMoves++;
                state.ghostDistanceTraveled[ghost.id] += ghost.distanceThisStep;
                if (ghost.revisitThisStep)
                    state.totalRevisitMoves++;

                if (detected && metrics.detectStep < 0)
                {
                    metrics.detectStep = step;
                    if (metrics.capturePhaseStartStep < 0)
                        metrics.capturePhaseStartStep = step;
                    
                }

                if (detected && state.phase == GhostTeamPhase.Search)
                {
                    state.phase = GhostTeamPhase.Capture;
                    state.worldState.UpdatePacmanLastKnown(state.pacmanPos);
                }

                if (detected)
                    anyGhostDetected = true;
            }

            if (state.phase == GhostTeamPhase.Capture)
            {
                if (anyGhostDetected)
                {
                    captureLostSightCounter = 0;
                }
                else
                {
                    captureLostSightCounter++;

                    int failureTolerance = Mathf.Max(2, captureLostSightSteps);
                    if (captureLostSightCounter >= failureTolerance)
                    {
                        state.phase = GhostTeamPhase.Search;
                        state.worldState.ClearPacmanKnowledge();
                        captureLostSightCounter = 0;
                    }
                }
            }

            float coverage = CalculateCoverageRatio(state);
            metrics.finalCoverageRatio = coverage;

            if (metrics.firstFullCoverageStep < 0 && coverage >= 0.999f)
                metrics.firstFullCoverageStep = step;
        }

        FinalizeMetrics(state, metrics);
        return metrics;
    }

    SimState BuildState(System.Random rng)
    {
        SimState state = BuildStateFromTilemap();
        if (state == null)
            throw new InvalidOperationException("Simulation requires gameplay tilemap data, but no valid tilemap source was found.");

        state.phase = GhostTeamPhase.Search;

        // world state
        if (state.worldState == null)
            state.worldState = new SharedWorldState();

        state.worldState.Initialize(state.width, state.height);

        InitializeFromScene(state, rng);
        state.visitCount = new int[state.width, state.height];
        
        // Initialize Phase 1 metrics tracking
        state.ghostDistanceTraveled = new float[ghostCount];
        for (int i = 0; i < ghostCount; i++)
            state.ghostDistanceTraveled[i] = 0f;
        state.totalGhostMoves = 0;
        state.totalRevisitMoves = 0;
        state.targetConflictCount = 0;

        return state;
    }

    SimState BuildStateFromTilemap()
    {
        Tilemap groundTilemap;
        Tilemap wallTilemap;
        GameObject spawnedRoot = null;

        if (!ResolveTilemapSources(out groundTilemap, out wallTilemap, out spawnedRoot))
        {
            Debug.LogError("Simulation tilemap source is missing. Please assign scene tilemaps (Ground/Wall) like gameplay.");
            return null;
        }

        groundTilemap.CompressBounds();
        BoundsInt bounds = groundTilemap.cellBounds;
        Vector3Int origin = bounds.min;

        SimState state = new SimState(bounds.size.x, bounds.size.y);
        state.tilemapOrigin = origin;  // Store origin for later coordinate conversion

        for (int x = 0; x < state.width; x++)
        {
            for (int y = 0; y < state.height; y++)
            {
                Vector3Int cell = new Vector3Int(origin.x + x, origin.y + y, 0);
                bool hasGround = groundTilemap.HasTile(cell);

                WallTile wallTile = null;
                if (wallTilemap != null && wallTilemap.HasTile(cell))
                    wallTile = wallTilemap.GetTile(cell) as WallTile;

                bool hardBlock = !hasGround || (wallTile != null && wallTile.isBlock);

                state.walkable[x, y] = !hardBlock;
                state.wallCells[x, y] = new SimWallCell
                {
                    isBlock = wallTile != null && wallTile.isBlock,
                    bTop = wallTile != null && wallTile.bTop,
                    bBottom = wallTile != null && wallTile.bBottom,
                    bLeft = wallTile != null && wallTile.bLeft,
                    bRight = wallTile != null && wallTile.bRight
                };
            }
        }

        if (spawnedRoot != null)
            Destroy(spawnedRoot);

        return state;
    }

    bool ResolveTilemapSources(
        out Tilemap groundTilemap,
        out Tilemap wallTilemap,
        out GameObject spawnedRoot)
    {
        groundTilemap = groundTilemapSource;
        wallTilemap = wallTilemapSource;
        spawnedRoot = null;

        if (groundTilemap != null)
            return true;

        Tilemap[] sceneTilemaps = FindObjectsOfType<Tilemap>(true);
        if (sceneTilemaps != null && sceneTilemaps.Length > 0)
        {
            groundTilemap = FindTilemapByName(sceneTilemaps, groundTilemapName);
            wallTilemap = FindTilemapByName(sceneTilemaps, wallTilemapName);

            if (groundTilemap == null)
                groundTilemap = sceneTilemaps[0];

            if (groundTilemap != null)
                return true;
        }

        if (!usePrefabFallbackWhenSceneTilemapMissing || tilemapPrefab == null)
            return false;

        spawnedRoot = Instantiate(tilemapPrefab);
        Tilemap[] tilemaps = spawnedRoot.GetComponentsInChildren<Tilemap>(true);

        if (tilemaps == null || tilemaps.Length == 0)
            return false;

        groundTilemap = FindTilemapByName(tilemaps, groundTilemapName);
        wallTilemap = FindTilemapByName(tilemaps, wallTilemapName);

        if (groundTilemap == null)
            groundTilemap = tilemaps[0];

        return groundTilemap != null;
    }

    Tilemap FindTilemapByName(Tilemap[] tilemaps, string nameHint)
    {
        if (tilemaps == null || tilemaps.Length == 0)
            return null;

        if (string.IsNullOrWhiteSpace(nameHint))
            return null;

        for (int i = 0; i < tilemaps.Length; i++)
        {
            Tilemap tilemap = tilemaps[i];
            if (tilemap != null &&
                tilemap.gameObject.name.Equals(nameHint, StringComparison.OrdinalIgnoreCase))
            {
                return tilemap;
            }
        }

        return null;
    }

    void StepPacman(SimState state, System.Random rng)
    {
        if (pacmanMoveMode == PacmanMoveMode.RandomWalk)
        {
            state.pacmanPos = RandomMove(state, state.pacmanPos, rng);
            return;
        }

        // Greedy escape - find neighbor that maximizes distance to nearest ghost
        List<Vector2Int> neighbors = state.grid.GetNeighbors(state.pacmanPos);
        if (neighbors.Count == 0) return;

        float distanceToNearestGhostNow = GetMinDistanceToAnyGhost(state, state.pacmanPos);
        
        float bestScore = float.MinValue;
        Vector2Int best = state.pacmanPos;

        foreach (var n in neighbors)
        {
            float minDist = GetMinDistanceToAnyGhost(state, n);
            
            // Only move if it increases distance to nearest ghost
            // If all moves decrease distance, stay in place
            if (minDist >= bestScore)
            {
                bestScore = minDist;
                best = n;
            }
        }
        
        
        if (distanceToNearestGhostNow > bestScore)
        {
            return;
        }

        state.pacmanPos = best;
    }
    
    float GetMinDistanceToAnyGhost(SimState state, Vector2Int pos)
    {
        float minDist = float.MaxValue;
        foreach (var ghost in state.ghosts)
        {
            float d = Manhattan(pos, ghost.pos);
            if (d < minDist) minDist = d;
        }
        return minDist;
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
        
        // ===== PHASE 1 METRICS CALCULATION =====
        
        // 1. Revisit Ratio (efficiency of search)
        metrics.revisitRatio = state.totalGhostMoves > 0 
            ? (float)state.totalRevisitMoves / state.totalGhostMoves 
            : 0f;
        
        // 2. Idleness Metrics (cell coverage quality)
        metrics.avgIdleness = metrics.avgCellAge;
        metrics.maxIdleness = maxAge;
        
        // 3. Workload Balance (ghost coordination)
        if (state.ghostDistanceTraveled != null && state.ghostDistanceTraveled.Length > 0)
        {
            float avgDistance = 0f;
            for (int i = 0; i < state.ghostDistanceTraveled.Length; i++)
                avgDistance += state.ghostDistanceTraveled[i];
            avgDistance /= state.ghostDistanceTraveled.Length;
            
            float variance = 0f;
            for (int i = 0; i < state.ghostDistanceTraveled.Length; i++)
            {
                float diff = state.ghostDistanceTraveled[i] - avgDistance;
                variance += diff * diff;
            }
            variance /= state.ghostDistanceTraveled.Length;
            metrics.workloadBalance = Mathf.Sqrt(variance);
        }
        
        // 4. Target Conflict Count (multi-agent metric)
        metrics.targetConflictCount = state.targetConflictCount;
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

    void InitializeFromScene(SimState state, System.Random rng)
    {
        // Get tilemap for coordinate conversion
        Tilemap groundTilemap = groundTilemapSource;
        if (groundTilemap == null && tilemapPrefab != null)
        {
            Tilemap[] tilemaps = tilemapPrefab.GetComponentsInChildren<Tilemap>(true);
            groundTilemap = FindTilemapByName(tilemaps, groundTilemapName);
            if (groundTilemap == null && tilemaps.Length > 0)
                groundTilemap = tilemaps[0];
        }

        if (groundTilemap == null)
            throw new InvalidOperationException("Cannot find groundTilemap for scene preset conversion. Required for coordinate transformation.");

        // Get Pacman position from scene (mandatory)
        if (pacmanObject == null)
            throw new InvalidOperationException("Pacman object not assigned. Scene preset requires pacmanObject to be set.");

        Vector2Int pacmanLogicPos = WorldToLogic(pacmanObject.transform.position, groundTilemap, state);
        if (!state.IsWalkable(pacmanLogicPos))
            throw new InvalidOperationException($"Pacman position {pacmanLogicPos} (world: {pacmanObject.transform.position}) is not walkable. Please place Pacman on a valid walkable tile.");

        state.pacmanPos = pacmanLogicPos;
        Debug.Log($"[SCENE] Pacman loaded at logic pos: {pacmanLogicPos} (world: {pacmanObject.transform.position})");

        // Get Ghost positions from scene (mandatory)
        if (ghostObjects == null || ghostObjects.Length == 0)
            throw new InvalidOperationException($"Ghost objects array is empty. Scene preset requires {ghostCount} ghosts to be assigned.");

        List<Region> ghostRegions = BuildRegions(state.width, ghostCount, partitionOverlap);
        int ghostIndex = 0;

        foreach (GameObject ghostObj in ghostObjects)
        {
            if (ghostIndex >= ghostCount)
                break;

            if (ghostObj == null)
                throw new InvalidOperationException($"Ghost object at index {ghostIndex} is null. All ghost slots must be filled.");

            Vector2Int ghostLogicPos = WorldToLogic(ghostObj.transform.position, groundTilemap, state);
            if (!state.IsWalkable(ghostLogicPos))
                throw new InvalidOperationException($"Ghost {ghostIndex} position {ghostLogicPos} (world: {ghostObj.transform.position}) is not walkable. Please place all ghosts on valid walkable tiles.");

            GhostAgent logic = new GhostAgent();
            logic.region = ghostRegions[Mathf.Min(ghostIndex, ghostRegions.Count - 1)];

            SimGhost ghost = new SimGhost
            {
                id = ghostIndex,
                pos = ghostLogicPos,
                currentTarget = Vector2Int.one * -999,
                logic = logic
            };

            state.ghosts.Add(ghost);
            Debug.Log($"[SCENE] Ghost {ghostIndex} loaded at logic pos: {ghostLogicPos} (world: {ghostObj.transform.position})");
            ghostIndex++;
        }

        if (ghostIndex < ghostCount)
            throw new InvalidOperationException($"Not enough ghost objects assigned. Expected {ghostCount}, but got {ghostIndex}.");

        Debug.Log($"[SCENE PRESET] Episode initialized - Pacman at {state.pacmanPos}, {state.ghosts.Count} ghosts at scene positions.");
    }

    Vector2Int WorldToLogic(Vector3 worldPos, Tilemap groundTilemap, SimState state)
    {
        // Convert world position to tilemap cell (using Unity's conversion)
        Vector3Int cell = groundTilemap.WorldToCell(worldPos);

        // Use the stored origin (same as GridManager uses)
        Vector3Int origin = state.tilemapOrigin;

        // Convert cell to logic position (exactly like GridManager.CellToLogic)
        Vector2Int logicPos = new Vector2Int(
            cell.x - origin.x,
            cell.y - origin.y
        );

        return logicPos;
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
        public SimWallCell[,] wallCells;
        public SimGridQuery grid;

        public SharedWorldState worldState;
        public Vector2Int pacmanPos;
        public List<SimGhost> ghosts = new List<SimGhost>();

        public GhostTeamPhase phase = GhostTeamPhase.Search;

        public int[,] visitCount;
        public int currentStep;
        
        // Tilemap origin for coordinate conversion (same as GridManager)
        public Vector3Int tilemapOrigin = Vector3Int.zero;
        
        // Phase 1 Metrics Tracking
        public int totalGhostMoves = 0;
        public int totalRevisitMoves = 0;
        public float[] ghostDistanceTraveled;
        public int targetConflictCount = 0;

        public SimState(int width, int height)
        {
            this.width = width;
            this.height = height;
            this.walkable = new bool[width, height];
            this.wallCells = new SimWallCell[width, height];
            this.grid = new SimGridQuery(this.walkable, this.wallCells);
            this.worldState = new SharedWorldState();
        }

        public bool IsWalkable(Vector2Int pos)
        {
            if (pos.x < 0 || pos.y < 0 || pos.x >= width || pos.y >= height)
                return false;
            return walkable[pos.x, pos.y];
        }
    }

    [Serializable]
    public class SimGhost
    {
        public int id;
        public Vector2Int pos;
        public Vector2Int currentTarget;
        public GhostAgent logic;
        public float distanceThisStep = 0f;  // Track distance for this step
        public bool revisitThisStep = false; // Track if revisit this step

        public bool Step(SimState state, float distanceWeight, float ageWeight, bool useRegion, int visionRange)
        {
            distanceThisStep = 0f;
            revisitThisStep = false;
            
            ClearOldTarget(state.worldState);

            state.worldState.UpdateGhost(id, pos);

            currentTarget = state.phase == GhostTeamPhase.Capture
                ? logic.FindCaptureTarget(pos, state.worldState, state.grid, id, state.ghosts.Count)
                : FindBestTargetLocal(state, distanceWeight, ageWeight, useRegion);

            ReserveTarget(state.worldState);

            List<Vector2Int> path = SimPathfinder.FindPath(pos, currentTarget, state.grid);

            if (path != null && path.Count > 1)
            {
                Vector2Int oldPos = pos;
                pos = path[1];
                
                // Track distance traveled
                distanceThisStep = Vector2Int.Distance(oldPos, pos);
                
                // Track if this cell was visited before (for revisit ratio)
                if (state.visitCount[pos.x, pos.y] > 0)
                    revisitThisStep = true;
                
                state.worldState.UpdateGhost(id, pos);
                logic.UpdateSharedState(state.worldState, pos);
            }

            List<Vector2Int> visibleCells = logic.GetVisibleCells(pos, state.grid, visionRange);
            MarkVisibleCells(state, visibleCells);

            bool detected = visibleCells.Contains(state.pacmanPos);

            if (detected)
                state.worldState.UpdatePacmanLastKnown(state.pacmanPos);

            return detected;
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

        void MarkVisibleCells(SimState state, List<Vector2Int> visibleCells)
        {
            foreach (var cell in visibleCells)
            {
                if (!state.worldState.IsInside(cell))
                    continue;

                state.worldState.MarkVisited(cell);
                state.visitCount[cell.x, cell.y] += 1;
            }
        }
    }

    [Serializable]
    public class EpisodeMetrics
    {
        public int seed;
        public int totalSteps = 0;
        public bool captured = false;
        public int detectStep = -1;
        public int capturePhaseStartStep = -1;
        public int captureStep = -1;
        public int firstFullCoverageStep = -1;
        public float finalCoverageRatio = 0f;
        public float meanRevisitInterval = -1f;
        public int maxCellAge = 0;
        public float avgCellAge = 0f;
        public float revisitRatio = 0f;
        public float avgIdleness = 0f;
        public int maxIdleness = 0;
        public float workloadBalance = 0f;
        public int targetConflictCount = 0;

        public int gap => (captureStep >= 0 && detectStep >= 0) ? captureStep - detectStep : -1;

        public override string ToString()
        {
            return $"[Seed {seed}] Captured:{captured} Detect:{detectStep} Capture:{captureStep} Gap:{gap} " +
                   $"Coverage:{finalCoverageRatio:P1} Revisit:{revisitRatio:P1} Balance:{workloadBalance:F1} Conflicts:{targetConflictCount}";
        }
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
            sb.AppendLine("seed,captured,detectStep,capturePhaseStartStep,captureStep,firstFullCoverageStep,finalCoverageRatio,maxCellAge,avgCellAge,meanRevisitInterval,totalSteps,revisitRatio,avgIdleness,maxIdleness,workloadBalance,targetConflictCount");

            foreach (var r in results)
            {
                sb.AppendLine(
                    $"{r.seed},{r.captured},{r.detectStep},{r.capturePhaseStartStep},{r.captureStep},{r.firstFullCoverageStep}," +
                    $"{r.finalCoverageRatio:F4},{r.maxCellAge},{r.avgCellAge:F4},{r.meanRevisitInterval:F4},{r.totalSteps}," +
                    $"{r.revisitRatio:F4},{r.avgIdleness:F4},{r.maxIdleness},{r.workloadBalance:F4},{r.targetConflictCount}"
                );
            }

            return sb.ToString();
        }
    }
}

public class SimGridQuery : IGridQuery
{
    private bool[,] walkable;
    private SimWallCell[,] wallCells;
    private int width;
    private int height;

    public int Width => width;
    public int Height => height;

    public SimGridQuery(bool[,] walkable, SimWallCell[,] wallCells)
    {
        this.walkable = walkable;
        this.wallCells = wallCells;
        this.width = walkable.GetLength(0);
        this.height = walkable.GetLength(1);
    }

    public bool IsHardBlocked(Vector2Int pos)
    {
        if (pos.x < 0 || pos.y < 0 || pos.x >= width || pos.y >= height)
            return true;

        return !walkable[pos.x, pos.y];
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

        if (!IsWalkable(from) || !IsWalkable(to))
            return false;

        SimWallCell fromWall = wallCells[from.x, from.y];
        SimWallCell toWall = wallCells[to.x, to.y];

        if (fromWall.isBlock || toWall.isBlock)
            return false;

        if (dir == Vector2Int.up)
        {
            if (fromWall.bTop || toWall.bBottom)
                return false;
        }
        else if (dir == Vector2Int.down)
        {
            if (fromWall.bBottom || toWall.bTop)
                return false;
        }
        else if (dir == Vector2Int.left)
        {
            if (fromWall.bLeft || toWall.bRight)
                return false;
        }
        else if (dir == Vector2Int.right)
        {
            if (fromWall.bRight || toWall.bLeft)
                return false;
        }

        return true;
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
            if (CanMove(pos, dir))
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

public struct SimWallCell
{
    public bool isBlock;
    public bool bTop;
    public bool bBottom;
    public bool bLeft;
    public bool bRight;
}
