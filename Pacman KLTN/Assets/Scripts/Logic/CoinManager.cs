using UnityEngine;
using System.Collections.Generic;

public class CoinManager : MonoBehaviour
{
    public static CoinManager Instance;

    [Header("Coin")]
    public Coin coinPrefab;
    public Transform coinParent;

    private Dictionary<Vector2Int, Coin> coins =
        new Dictionary<Vector2Int, Coin>();
    
    void Awake()
    {
        if (Instance == null) 
            Instance = this;
        else 
            Destroy(gameObject);
    }

    void Start()
    {
        SpawnCoins();
    }

    void SpawnCoins()
    {
        GridManager grid = GridManager.Instance;

        for (int x = 0; x < grid.GridSize.x; x++)
        {
            for (int y = 0; y < grid.GridSize.y; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);

                if (!grid.IsWalkable(pos))
                    continue;

                SpawnCoinAt(pos);
            }
        }
    }

    void SpawnCoinAt(Vector2Int gridPos)
    {
        Vector3 worldPos =
            GridManager.Instance.GridToWorld(gridPos);

        var coin = Instantiate(
            coinPrefab,
            worldPos,
            Quaternion.identity,
            coinParent
        );

        coins.Add(gridPos, coin);
        coin.gridPos = gridPos;
    }

    public void EatCoin(Vector2Int gridPos)
    {
        if (!coins.TryGetValue(gridPos, out Coin coin))
            return;

        Destroy(coin.gameObject);
        coins.Remove(gridPos);

        // TODO: cộng điểm
        // TODO: check win condition
        if (HasAnyCoinLeft() == false)
        {
            Debug.Log("You win!");
            GameManager.Instance.WinGame();
        }
    }

    public bool HasAnyCoinLeft()
    {
        return coins.Count > 0;
    }
}
