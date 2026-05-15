using System.Collections.Generic;
using UnityEngine;

public enum GhostMode
{
    Chase,
    Scatter,
    Frightened,
    Support
}

public enum GhostTeamPhase
{
    Search,
    Capture
}

[RequireComponent(typeof(GhostMovement))]
public class GhostAI : MonoBehaviour
{
    [SerializeField]
    private int id;

    [Header("State")]
    public GhostMode mode = GhostMode.Chase;

    [Header("References")]
    public GhostBehavior behavior;
    public GhostPathVisualizer visualizer;

    [Header("Scatter")]
    public Vector2Int scatterCorner;

    [Header("Search Region")]
    public Region region;

    [Header("Multi-Agent Phase")]
    public GhostTeamPhase teamPhase = GhostTeamPhase.Search;

    [Header("Search Scoring")]
    public SearchScoringMode searchScoringMode = SearchScoringMode.Baseline;
    public SearchScoreConfig searchScoreConfig = new SearchScoreConfig();

    [Header("Pathfinding")]
    public float repathInterval = 0.5f;

    [Header("Capture Recovery")]
    [SerializeField] private int lostSightReplansBeforeSearch = 4;

    public int ID => id;
    public Vector2Int GridPosition => movement.LogicPos;
    public Vector2Int LogicPos => movement.LogicPos;
    public GhostAgent Agent => agent;

    private GhostMovement movement;
    private IGridQuery gridQuery;
    private GhostAgent agent;

    [SerializeField] private int visionRadius = 5;

    private List<Vector2Int> currentPath;
    private float repathTimer;
    private Vector2Int currentTarget;
    private float nextCaptureRepathTime;
    private int captureLostSightCounter;

    void Update()
    {
        if (teamPhase == GhostTeamPhase.Capture)
        {
            if (movement.IsIdle && Time.time >= nextCaptureRepathTime)
            {
                ReplanCurrentPhase();
                return;
            }
        }
    }

    void Awake()
    {
        movement = GetComponent<GhostMovement>();

        if (behavior == null)
            behavior = GetComponent<GhostBehavior>();

        gridQuery = new UnityGridQuery();

        agent = new GhostAgent
        {
            region = region,
            searchScoringMode = searchScoringMode,
            searchScoreConfig = searchScoreConfig
        };
    }

    void Start()
    {
        EnterSearchPhase();
    }

    // =========================
    // COOPERATIVE SEARCH
    // =========================
    public void SearchMode()
    {
        if (teamPhase != GhostTeamPhase.Search)
            return;

        SharedWorldState worldState = GhostManager.Instance.worldState;

        agent.searchScoringMode = searchScoringMode;
        agent.searchScoreConfig = searchScoreConfig;

        ClearOldTarget(currentTarget, worldState);

        currentTarget = agent.FindBestTarget(
            GridPosition,
            worldState,
            gridQuery
        );

        Debug.Log($"[GHOST {ID} SEARCH] Chosen target {currentTarget}");
        if (worldState.reservedTargets.ContainsKey(currentTarget))
            Debug.Log($"[GHOST {ID} SEARCH] Target {currentTarget} already reserved by {worldState.reservedTargets[currentTarget]}");

        ReserveTarget(currentTarget, worldState);

        var path = AStarPathfinder.FindPath(GridPosition, currentTarget);

        if (path == null)
        {
            Debug.Log($"[GHOST {ID} SEARCH] No path found from {GridPosition} to {currentTarget}");
        }

        if (path != null && path.Count > 1)
        {
            movement.SetPath(
                path,
                1,
                () => ReplanCurrentPhase(),
                () =>
                {
                    worldState.UpdateGhost(ID, movement.LogicPos);

                    bool detected = agent.UpdateVisionAndDetectPacman(
                        worldState,
                        movement.LogicPos,
                        PlayStateManager.Instance.Pacman.LogicPos,
                        gridQuery,
                        visionRadius
                    );

                    if (detected)
                    {
                        Debug.Log($"Ghost {ID} detected Pacman at {PlayStateManager.Instance.Pacman.LogicPos}");
                        GhostManager.Instance.EnterCapturePhase(PlayStateManager.Instance.Pacman.LogicPos);
                    }
                }
            );

            if (visualizer != null)
                visualizer.DrawPath(path);

            Debug.Log($"[GHOST {ID} SEARCH] Path set length={path.Count}");
        }
        else
        {
            Debug.Log($"[GHOST {ID} SEARCH] Did not set path (null or too short). Will replan later.");
        }
    }

    public void EnterSearchPhase()
    {
        teamPhase = GhostTeamPhase.Search;
        captureLostSightCounter = 0;
        SearchMode();
    }

    public void EnterCapturePhase(Vector2Int pacmanPos)
    {
        SharedWorldState worldState = GhostManager.Instance.worldState;

        teamPhase = GhostTeamPhase.Capture;
        nextCaptureRepathTime = Time.time;
        captureLostSightCounter = 0;
        worldState.UpdatePacmanLastKnown(pacmanPos);

        ClearOldTarget(currentTarget, worldState);
        movement.InterruptPath();
        ReplanCurrentPhase();
    }

    void CaptureMode()
    {
        if (teamPhase != GhostTeamPhase.Capture)
            return;

        SharedWorldState worldState = GhostManager.Instance.worldState;

        ClearOldTarget(currentTarget, worldState);

        currentTarget = agent.FindCaptureTarget(
            GridPosition,
            worldState,
            gridQuery,
            ID,
            GhostManager.Instance.ghosts.Count
        );

        Debug.Log($"[GHOST {ID} CAPTURE] Chosen capture target {currentTarget}");

        ReserveTarget(currentTarget, worldState);

        var path = AStarPathfinder.FindPath(GridPosition, currentTarget);

        if (path == null)
        {
            Debug.Log($"[GHOST {ID} CAPTURE] No path found from {GridPosition} to {currentTarget}");
        }

        if (path != null && path.Count > 1)
        {
            movement.SetPath(
                path,
                1,
                () => ReplanCurrentPhase(),
                () =>
                {
                    worldState.UpdateGhost(ID, movement.LogicPos);

                    bool detected = agent.UpdateVisionAndDetectPacman(
                        worldState,
                        movement.LogicPos,
                        PlayStateManager.Instance.Pacman.LogicPos,
                        gridQuery,
                        visionRadius
                    );

                    if (detected)
                    {
                        captureLostSightCounter = 0;
                        GhostManager.Instance.EnterCapturePhase(PlayStateManager.Instance.Pacman.LogicPos);
                    }
                    else
                    {
                        captureLostSightCounter++;

                        if (captureLostSightCounter >= lostSightReplansBeforeSearch)
                        {
                            GhostManager.Instance.EnterSearchPhase();
                        }
                    }
                }
            );

            if (visualizer != null)
                visualizer.DrawPath(path);

            Debug.Log($"[GHOST {ID} CAPTURE] Path set length={path.Count}");

            return;
        }

        captureLostSightCounter++;
        if (captureLostSightCounter >= lostSightReplansBeforeSearch)
        {
            GhostManager.Instance.EnterSearchPhase();
        }
    }

    void ReplanCurrentPhase()
    {
        nextCaptureRepathTime = Time.time + repathInterval;

        if (teamPhase == GhostTeamPhase.Capture)
            CaptureMode();
        else
            SearchMode();
    }

    public void ClearOldTarget(Vector2Int target, SharedWorldState worldState)
    {
        if (worldState.reservedTargets.ContainsKey(target) &&
            worldState.reservedTargets[target] == ID)
        {
            worldState.reservedTargets.Remove(target);
        }
    }

    public void ReserveTarget(Vector2Int target, SharedWorldState worldState)
    {
        if (!worldState.reservedTargets.ContainsKey(target))
        {
            worldState.reservedTargets[target] = ID;
        }
    }

    void RecalculatePath()
    {
        Vector2Int target = agent.GetTargetByMode(
            mode,
            LogicPos,
            behavior,
            scatterCorner,
            gridQuery
        );

        currentPath = AStarPathfinder.FindPath(LogicPos, target);

        if (currentPath == null || currentPath.Count < 2)
            return;

        movement.SetPath(currentPath, 1);

        if (visualizer != null)
            visualizer.DrawPath(currentPath);
    }

    public void SetMode(GhostMode newMode)
    {
        if (mode == newMode) return;

        mode = newMode;
        Debug.LogWarning($"Ghost {ID} changed mode to {newMode}");
    }
}