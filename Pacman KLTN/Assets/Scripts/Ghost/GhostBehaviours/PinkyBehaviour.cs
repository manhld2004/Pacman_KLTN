using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PinkyBehavior : GhostBehavior
{
    public int lookAhead = 4;

    public override Vector2Int GetTarget()
    {
        Vector2Int pacPos = PlayStateManager.Instance.Pacman.LogicPos;
        Vector2Int pacDir = PlayStateManager.Instance.Pacman.CurrentDir;

        return pacPos + pacDir * lookAhead;
    }
}

