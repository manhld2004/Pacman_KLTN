using UnityEngine;
using System.Collections.Generic;
using System;

public class GhostMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 4.5f;

    // =======================
    // LOGIC STATE
    // =======================

    private Vector2Int logicPos;
    private Vector2Int currentDir;

    private Vector3 moveTargetWorld;
    private bool isMoving;

    // PATH
    private List<Vector2Int> path;
    private int pathIndex;

    public Vector2Int LogicPos => logicPos;
    public bool IsIdle => !isMoving && path == null;

    [SerializeField]
    private Transform ghostTexture;
    [SerializeField]
    private Animation movingAnimation;

    public Action OnPathFinished;
    public Action OnMoveToCell;

    // =======================
    // INIT
    // =======================

    void Start()
    {
        logicPos = GridManager.Instance.WorldToLogic(transform.position);
        transform.position = GridManager.Instance.GridToWorld(logicPos);

        isMoving = false;
    }

    void Update()
    {
        if (!isMoving)
        {
            FollowPath();
        }

        MoveTowardsTarget();
    }

    // =======================
    // PATH INTERFACE (AI CALLS)
    // =======================

    public void SetPath(List<Vector2Int> newPath, int startIndex = 1, Action onFinished = null, Action onMoveToCell = null)
    {
        if (newPath == null || newPath.Count <= startIndex)
        {
            path = null;
            OnPathFinished = null;
            OnMoveToCell = null;
            return;
        }

        // DEBUG: Capture phase path setting
        if (newPath.Count > 1)
        {
            Debug.Log($"[CAPTURE] SetPath called: start={newPath[0]}, next={newPath[startIndex]}, target={newPath[newPath.Count-1]}, pathLength={newPath.Count}");
            
            // Check if any step in path is diagonal
            for (int i = 0; i < newPath.Count - 1; i++)
            {
                Vector2Int from = newPath[i];
                Vector2Int to = newPath[i + 1];
                Vector2Int step = to - from;
                if (step.sqrMagnitude != 1)
                {
                    Debug.LogError($"[DIAGONAL BUG SOURCE] Path step {i}->{i+1} is diagonal: {from} -> {to} (step={step}, sqrMagnitude={step.sqrMagnitude})");
                }
            }
        }

        path = newPath;
        pathIndex = startIndex;
        OnPathFinished = onFinished;
        OnMoveToCell = onMoveToCell;

    }

    public void InterruptPath()
    {
        path = null;
        pathIndex = 0;
        isMoving = false;
        OnPathFinished = null;
        OnMoveToCell = null;
    }

    // =======================
    // CORE LOGIC
    // =======================

    void FollowPath()
    {
        if (path == null || pathIndex >= path.Count)
        {
            Debug.Log("Path finished or invalid");
            OnPathFinished?.Invoke();
            return;
        }

        Debug.Log("Following path to " + path[pathIndex] + " (index " + pathIndex + ")");

        Vector2Int nextPos = path[pathIndex];
        Vector2Int dir = nextPos - logicPos;

        // đảm bảo là hướng hợp lệ (4 hướng, sqrMagnitude = 1)
        if (dir.sqrMagnitude != 1)
        {
            Debug.LogError($"[DIAGONAL BUG] Path contains invalid step! logicPos={logicPos}, nextPos={nextPos}, dir={dir}, sqrMagnitude={dir.sqrMagnitude}");
            Debug.LogError($"[DIAGONAL BUG] Full path: {string.Join(" -> ", path.ConvertAll(p => p.ToString()))}");
            Debug.LogError($"[DIAGONAL BUG] Current pathIndex: {pathIndex}");
            path = null;
            return;
        }

        // nếu không thể đi → hủy path
        if (!GridManager.Instance.CanMove(logicPos, dir))
        {
            Debug.LogWarning($"Cannot move {logicPos} in direction {dir}. Hủy path.");
            path = null;
            return;
        }

        currentDir = dir;
        logicPos = logicPos + dir;

        moveTargetWorld = GridManager.Instance.GridToWorld(logicPos);
        isMoving = true;

        pathIndex++;
    }

    /// <summary>
    /// Normalize direction để chỉ có 4 hướng chính: up, down, left, right
    /// </summary>
    Vector2Int NormalizeDirection(Vector2Int dir)
    {
        if (dir == Vector2Int.zero)
            return Vector2Int.zero;

        // Nếu không phải chính xác 4 hướng, chọn hướng có magnitude lớn nhất
        if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            return new Vector2Int(dir.x > 0 ? 1 : -1, 0); // Quyên tới trái/phải
        else
            return new Vector2Int(0, dir.y > 0 ? 1 : -1); // Quyên tới trên/dưới
    }

    void MoveTowardsTarget()
    {
        if (!isMoving)
        {
            movingAnimation.Stop();
            return;
        }
            

        transform.position = Vector3.MoveTowards(
            transform.position,
            moveTargetWorld,
            moveSpeed * Time.deltaTime
        );

        if (Vector3.Distance(transform.position, moveTargetWorld) < 0.001f)
        {
            transform.position = moveTargetWorld;
            OnMoveToCell?.Invoke();
            isMoving = false;
        }

        if (movingAnimation.isPlaying == false)
            movingAnimation.Play();

        // Update animation direction - chỉ 4 hướng, không cho phép chéo
        if (currentDir == Vector2Int.right)
            ghostTexture.localScale = ghostTexture.localScale.WithX(-1 * Mathf.Abs(ghostTexture.localScale.x)); // Mặt phải
        else if (currentDir == Vector2Int.left)
            ghostTexture.localScale = ghostTexture.localScale.WithX(Mathf.Abs(ghostTexture.localScale.x)); // Mặt trái
        else if (currentDir == Vector2Int.up || currentDir == Vector2Int.down)
        {
            // Up/down: giữ facing direction
        }
        else
        {
            Debug.LogError($"[DIAGONAL BUG] Invalid currentDir: {currentDir} (sqrMagnitude={currentDir.sqrMagnitude})");
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        PacManMovement pacman = other.GetComponent<PacManMovement>();

        if (pacman == null)
            return;

        GameManager.Instance.LoseGame();
    }

#if UNITY_EDITOR
    // =======================
    // DEBUG
    // =======================

    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.1f);
    }
#endif
}