using UnityEngine;

public class UIManager : MonoBehaviour
{
    public GameObject winPanel;
    public GameObject losePanel;

    void Start()
    {
        winPanel.SetActive(false);
        losePanel.SetActive(false);
    }

    public void ShowWin()
    {
        winPanel.SetActive(true);
    }

    public void ShowLose()
    {
        losePanel.SetActive(true);
    }
}
