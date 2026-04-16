using System.Collections.Generic;
using UnityEngine;

public enum GhostMode
{
    Chase,
    Scatter,
    Frightened,
    Support
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

    [Header("Pathfinding")]
    public float repathInterval = 0.5f;

    public int ID => id;
    public Vector2Int GridPosition => movement.LogicPos;
    public Vector2Int LogicPos => movement.LogicPos;
    public GhostAgent Agent => agent;

    private GhostMovement movement;
    private IGridQuery gridQuery;
    private GhostAgent agent;

    private List<Vector2Int> currentPath;
    private float repathTimer;
    private Vector2Int currentTarget;

    void Awake()
    {
        movement = GetComponent<GhostMovement>();

        if (behavior == null)
            behavior = GetComponent<GhostBehavior>();

        gridQuery = new UnityGridQuery();

        agent = new GhostAgent
        {
            region = region
        };
    }

    void Start()
    {
        SearchMode();
    }

    // =========================
    // COOPERATIVE SEARCH
    // =========================
    public void SearchMode()
    {
        SharedWorldState worldState = GhostManager.Instance.worldState;

        ClearOldTarget(currentTarget, worldState);

        currentTarget = agent.FindBestTarget(
            GridPosition,
            worldState,
            gridQuery
        );

        ReserveTarget(currentTarget, worldState);

        var path = AStarPathfinder.FindPath(GridPosition, currentTarget);

        if (path != null && path.Count > 1)
        {
            movement.SetPath(
                path,
                1,
                () => SearchMode(),
                () => agent.UpdateSharedState(worldState, movement.LogicPos)
            );

            if (visualizer != null)
                visualizer.DrawPath(path);
        }
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

    // =========================
    // CLASSIC MODE PATHING
    // =========================
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

    // =========================
    // STATE CONTROL
    // =========================
    public void SetMode(GhostMode newMode)
    {
        if (mode == newMode) return;

        mode = newMode;
        Debug.LogWarning($"Ghost {ID} changed mode to {newMode}");
    }
}