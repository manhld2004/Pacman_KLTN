using UnityEngine;

public abstract class GhostBehavior : MonoBehaviour
{
    protected GhostAI ghost;

    protected virtual void Awake()
    {
        ghost = GetComponent<GhostAI>();
    }

    public abstract Vector2Int GetTarget();
}
