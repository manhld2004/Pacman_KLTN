using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public enum GameState
{
    Playing,
    Win,
    Lose
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameState State { get; private set; }

    [Header("References")]
    public PacManMovement pacman;
    public List<GhostMovement> ghosts;
    public CoinManager coinManager;
    public UIManager ui;
    public GameObject visualizersParent;

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
        State = GameState.Playing;
    }

    void Update()
    {
        if (State != GameState.Playing)
            return;

        if (Input.GetKeyDown(KeyCode.V))
        {
            visualizersParent.SetActive(!visualizersParent.activeInHierarchy);
        }
    }

    void Start()
    {
        if (visualizersParent.activeInHierarchy) 
            visualizersParent.SetActive(false);
    }

    public void WinGame()
    {
        State = GameState.Win;
        PauseGame();
        ui.ShowWin();
    }

    public void LoseGame()
    {
        State = GameState.Lose;
        PauseGame();
        ui.ShowLose();
    }

    void PauseGame()
    {
        Time.timeScale = 0f;
    }

    public void Replay()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex
        );
    }
}
