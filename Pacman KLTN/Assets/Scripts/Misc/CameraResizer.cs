using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CameraResizer : MonoBehaviour
{
    public float targetWidth = 1080f;      // thiết kế gốc: 1080x1920
    public float targetHeight = 1920f;
    public float baseOrthographicSize = 23.43f; // size tương ứng với 1920x1080

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Callback khi scene được load và active.
    /// </summary>
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Delay 1 frame để chắc chắn camera đã spawn
        StartCoroutine(ResizeAfterFrame(scene));
    }

    IEnumerator ResizeAfterFrame(Scene scene)
    {
        yield return null; // chờ 1 frame sau khi scene active

        Camera cam = Camera.main;
        if (cam == null)
        {
            // Nếu không tìm thấy camera nào có tag MainCamera
            yield break;
        }

        ResizeCamera(cam);
    }

    void ResizeCamera(Camera cam)
    {
        float targetAspect = targetWidth / targetHeight;  // 1080 / 1920 = 0.5625
        float currentAspect = (float)Screen.width / Screen.height;

        if (currentAspect >= targetAspect)
        {
            // Màn hình rộng hơn hoặc bằng => giữ nguyên chiều cao
            cam.orthographicSize = baseOrthographicSize;
        }
        else
        {
            // Màn hình hẹp hơn => tăng chiều cao để vừa chiều ngang
            float scaleFactor = targetAspect / currentAspect;
            cam.orthographicSize = baseOrthographicSize * scaleFactor;
        }
    }
}
