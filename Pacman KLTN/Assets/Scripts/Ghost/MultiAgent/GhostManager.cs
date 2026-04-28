using System.Collections.Generic;
using UnityEngine;

public class GhostManager : MonoBehaviour
{
    public static GhostManager Instance;
    public List<GhostAI> ghosts;

    public SharedWorldState worldState = new SharedWorldState();
    private Region[] regions;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        UpdateWorldState();
        //AssignRoles();
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

    // void AssignRoles()
    // {
    //     GhostAI closestGhost = null;

    //     float minDist = float.MaxValue;

    //     foreach (var ghost in ghosts)
    //     {
    //         float dist = Vector2Int.Distance(
    //             ghost.GridPosition,
    //             worldState.pacmanPosition
    //         );

    //         if (dist < minDist)
    //         {
    //             minDist = dist;
    //             closestGhost = ghost;
    //         }
    //     }

    //     foreach (var ghost in ghosts)
    //     {
    //         if (ghost == closestGhost)
    //             ghost.SetMode(GhostMode.Chase);
    //         else
    //             ghost.SetMode(GhostMode.Support);
    //     }
    // }

    public void InitRegions()
    {
        int ghostCount = ghosts.Count;
        int width = GridManager.Instance.GridSize.x;

        int regionWidth = width / ghostCount;

        regions = new Region[ghostCount];

        for(int i=0;i<ghostCount;i++)
        {
            regions[i] = new Region
            {
                minX = i * regionWidth,
                maxX = (i == ghostCount-1)
                    ? width - 1
                    : (i+1)*regionWidth - 1
            };

            ghosts[i].Agent.region = regions[i];
        }
    }
}

public struct Region
{
    public int minX;
    public int maxX;
}