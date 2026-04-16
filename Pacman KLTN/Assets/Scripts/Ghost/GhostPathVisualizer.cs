using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class GhostPathVisualizer : MonoBehaviour
{
    public bool showPath = true;
    public float heightOffset = 0.01f;

    private LineRenderer line;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 0;
    }

    public void DrawPath(List<Vector2Int> path)
    {
        if (!showPath || path == null || path.Count == 0)
        {
            line.positionCount = 0;
            return;
        }

        line.positionCount = path.Count;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 worldPos =
                GridManager.Instance.GridToWorld(path[i]);

            worldPos.z -= heightOffset; // tránh z-fighting
            line.SetPosition(i, worldPos);
        }
    }

    public void Clear()
    {
        line.positionCount = 0;
    }

    public void SetVisible(bool visible)
    {
        showPath = visible;
        if (!visible)
            Clear();
    }
}
