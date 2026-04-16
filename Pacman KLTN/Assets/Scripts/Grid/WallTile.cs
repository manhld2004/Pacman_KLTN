using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;


[CreateAssetMenu(menuName = "Pacman/Wall Tile")]
public class WallTile : Tile
{
    public bool isBlock = false;
    public bool bTop = false;
    public bool bBottom = false;
    public bool bLeft = false;
    public bool bRight = false;
}
