using UnityEngine;

public class Coin : MonoBehaviour
{
    public Vector2Int gridPos;

    void OnTriggerEnter2D(Collider2D other)
    {
        PacManMovement pacman = other.GetComponent<PacManMovement>();

        if (pacman == null)
            return;

        CoinManager.Instance.EatCoin(gridPos);
        Debug.Log("Eaten Coin" + gridPos.x + " " + gridPos.y);
    }
}
