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

        path = newPath;
        pathIndex = startIndex;
        OnPathFinished = onFinished;
        OnMoveToCell = onMoveToCell;

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

        Debug.Log("Following path to " + path[pathIndex]);

        Vector2Int nextPos = path[pathIndex];
        Vector2Int dir = nextPos - logicPos;

        // đảm bảo là hướng hợp lệ
        if (dir.sqrMagnitude != 1)
        {
            path = null;
            return;
        }

        // nếu không thể đi → hủy path
        if (!GridManager.Instance.CanMove(logicPos, dir))
        {
            path = null;
            return;
        }

        currentDir = dir;
        logicPos = nextPos;

        moveTargetWorld = GridManager.Instance.GridToWorld(logicPos);
        isMoving = true;

        pathIndex++;
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

        if (currentDir.IsRight()) ghostTexture.localScale = ghostTexture.localScale.WithX(-1 * Mathf.Abs(ghostTexture.localScale.x));
        else if (currentDir.IsLeft()) ghostTexture.localScale = ghostTexture.localScale.WithX(Mathf.Abs(ghostTexture.localScale.x));
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