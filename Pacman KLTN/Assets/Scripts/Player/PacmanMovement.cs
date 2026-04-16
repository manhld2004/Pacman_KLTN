using UnityEngine;

public class PacManMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;  

    private Vector2Int logicPos;       
    private Vector2Int currentDir;     // hướng đang đi
    private Vector2Int nextDir;        // hướng buffer

    private Vector2 moveTargetWorld;   // tâm tile tiếp theo
    private bool isMoving;

    [SerializeField]
    private float swipeThreshold = 50f;

    private Vector2 touchStartPos;
    private bool isSwiping;


    public Vector2Int LogicPos { get { return logicPos;}}
    public Vector2Int CurrentDir { get { return currentDir; }}

    [SerializeField]
    private Transform pacmanTexture;
    [SerializeField]
    private Animation movingAnimation;


    void Start()
    {
        // Khởi tạo từ vị trí hiện tại
        logicPos = GridManager.Instance.WorldToLogic(transform.position);

        currentDir = Vector2Int.zero;
        nextDir = Vector2Int.zero;

        transform.position = GridManager.Instance.LogicToWorld(logicPos);
        isMoving = false;
    }

    void Update()
    {
        ReadInput();
        ReadSwipeInput();

        if (!isMoving)
        {
            TryChangeDirection();
            TryMoveForward();
        }

        MoveTowardsTarget();
    }

    void ReadInput()    
    {
        if (Input.GetKeyDown(KeyCode.UpArrow))
            nextDir = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.DownArrow))
            nextDir = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
            nextDir = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.RightArrow))
            nextDir = Vector2Int.right;
    }

    void ReadSwipeInput()
    {
        if (Input.touchCount == 0)
            return;

        Touch touch = Input.GetTouch(0);

        switch (touch.phase)
        {
            case TouchPhase.Began:
                touchStartPos = touch.position;
                isSwiping = true;
                break;

            case TouchPhase.Ended:
                if (!isSwiping)
                    return;

                Vector2 delta = touch.position - touchStartPos;

                if (delta.magnitude < swipeThreshold)
                    return;

                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                {
                    // Horizontal
                    nextDir = delta.x > 0
                        ? Vector2Int.right
                        : Vector2Int.left;
                }
                else
                {
                    // Vertical
                    nextDir = delta.y > 0
                        ? Vector2Int.up
                        : Vector2Int.down;
                }

                isSwiping = false;
                break;
        }
    }


    void TryChangeDirection()
    {
        if (nextDir == Vector2Int.zero)
            return;

        if (GridManager.Instance.CanMove(logicPos, nextDir))
        {
            currentDir = nextDir;
            nextDir = Vector2Int.zero;
        }
    }

    void TryMoveForward()
    {
        if (currentDir == Vector2Int.zero)
            return;

        if (!GridManager.Instance.CanMove(logicPos, currentDir))
        {
            currentDir = Vector2Int.zero;
            return;
        }

        logicPos += currentDir;
        moveTargetWorld = GridManager.Instance.LogicToWorld(logicPos);
        isMoving = true;
    }


    void MoveTowardsTarget()
    {
        if (!isMoving)
        {
            movingAnimation.Stop();
            return;
        }

        transform.position = Vector2.MoveTowards(
            transform.position,
            moveTargetWorld,
            moveSpeed * Time.deltaTime
        );

        if (Vector2.Distance(transform.position, moveTargetWorld) < 0.001f)
        {
            transform.position = moveTargetWorld;
            isMoving = false;
        }

        if (movingAnimation.isPlaying == false)
            movingAnimation.Play();

        if (currentDir.IsRight()) pacmanTexture.localScale = pacmanTexture.localScale.WithX(-1 * Mathf.Abs(pacmanTexture.localScale.x));
        else if (currentDir.IsLeft()) pacmanTexture.localScale = pacmanTexture.localScale.WithX(Mathf.Abs(pacmanTexture.localScale.x));
    }

#if UNITY_EDITOR
    // =======================
    // DEBUG
    // =======================

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.1f);
    }
#endif
}
