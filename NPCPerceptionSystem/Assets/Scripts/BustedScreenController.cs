using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class BustedScreenController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject bustedRoot;

    [Header("Cursor")]
    [SerializeField] private bool unlockCursorOnBusted = true;

    private bool isShown;

    private void Awake()
    {
        if (bustedRoot)
        {
            bustedRoot.SetActive(false);
        }
    }

    public void ShowBusted()
    {
        if (isShown)
        {
            return;
        }

        isShown = true;

        if (bustedRoot)
        {
            bustedRoot.SetActive(true);
        }

        Time.timeScale = 0f;

        if (unlockCursorOnBusted)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void RestartLevel()
    {
        Time.timeScale = 1f;
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.buildIndex);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
