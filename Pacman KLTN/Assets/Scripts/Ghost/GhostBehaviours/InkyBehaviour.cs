using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InkyBehavior : GhostBehavior
{
    public int offset = 2;

    public override Vector2Int GetTarget()
    {
        Vector2Int pacPos = PlayStateManager.Instance.Pacman.LogicPos;
        Vector2Int pacDir = PlayStateManager.Instance.Pacman.CurrentDir;

        Vector2Int ahead = pacPos + pacDir * offset;
        Vector2Int blinkyPos = PlayStateManager.Instance.BlinkyGhost.LogicPos;

        Vector2Int vector = ahead - blinkyPos;
        return blinkyPos + vector * 2;
    }
}

