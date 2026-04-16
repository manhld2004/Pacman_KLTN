using System.Collections.Generic;
using UnityEngine;

public class SharedWorldState
{
    public Vector2Int? pacmanLastKnownPosition;
    // id -> position
    public Dictionary<int, Vector2Int> ghostPositions = new Dictionary<int, Vector2Int>();

    public int[,] visitTimes;

    // target tile -> ghost id
    public Dictionary<Vector2Int, int> reservedTargets = new Dictionary<Vector2Int, int>();

    private int width;
    private int height;
    private int step = 0;

    public int Width => width;
    public int Height => height;
    public int CurrentStep => step;

    // =========================
    // INIT
    // =========================
    public void Initialize(int width, int height, int unvisitedInitValue = -4000)
    {
        this.width = width;
        this.height = height;

        visitTimes = new int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                visitTimes[x, y] = unvisitedInitValue;
            }
        }

        pacmanLastKnownPosition = null;
        ghostPositions.Clear();
        reservedTargets.Clear();
        step = 0;
    }

    // =========================
    // RESET
    // =========================
    public void ResetRuntimeState(int unvisitedInitValue = -4000)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                visitTimes[x, y] = unvisitedInitValue;
            }
        }

        pacmanLastKnownPosition = null;
        ghostPositions.Clear();
        reservedTargets.Clear();
        step = 0;
    }

    // =========================
    // UPDATE STATE
    // =========================
    public void UpdatePacmanLastKnown(Vector2Int pos)
    {
        pacmanLastKnownPosition = pos;
    }

    public void ClearPacmanKnowledge()
    {
        pacmanLastKnownPosition = null;
    }

    public void UpdateGhost(int id, Vector2Int pos)
    {
        ghostPositions[id] = pos;
    }

    public bool TryGetGhostPosition(int id, out Vector2Int pos)
    {
        return ghostPositions.TryGetValue(id, out pos);
    }

    public void MarkVisited(Vector2Int pos)
    {
        if (!IsInside(pos)) return;
        visitTimes[pos.x, pos.y] = step;
    }

    // =========================
    // STEP CONTROL
    // =========================
    public void UpdateStepTick()
    {
        step++;
    }

    public void SetStep(int value)
    {
        step = value;
    }

    // =========================
    // HELPERS
    // =========================
    public bool IsInside(Vector2Int pos)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;
    }

    public int GetVisitTime(Vector2Int pos)
    {
        if (!IsInside(pos)) return int.MinValue;
        return visitTimes[pos.x, pos.y];
    }

    public int GetCellAge(Vector2Int pos)
    {
        if (!IsInside(pos)) return int.MaxValue;
        return step - visitTimes[pos.x, pos.y];
    }

    public void ClearReservations()
    {
        reservedTargets.Clear();
    }
}