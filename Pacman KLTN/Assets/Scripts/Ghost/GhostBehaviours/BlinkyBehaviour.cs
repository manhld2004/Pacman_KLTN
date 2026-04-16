using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlinkyBehavior : GhostBehavior
{
    public override Vector2Int GetTarget()
    {
        return PlayStateManager.Instance.Pacman.LogicPos;
    }
}

