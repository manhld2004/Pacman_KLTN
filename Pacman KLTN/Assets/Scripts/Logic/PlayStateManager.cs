using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PlayStateManager : MonoBehaviour
{
    public static PlayStateManager Instance;

    [SerializeField]
    private PacManMovement pacman;
    [SerializeField]
    private GhostMovement[] ghosts;
    [SerializeField]
    private GhostMovement blinkyGhost;

    public PacManMovement Pacman => pacman;
    public GhostMovement[] Ghosts => ghosts;
    public GhostMovement BlinkyGhost => blinkyGhost;

    public enum PlayState
    {
        Playing,
        Paused,
        GameOver,
        Victory
    }

    private PlayState currentState = PlayState.Playing;

    public PlayState CurrentState => currentState;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    
}
