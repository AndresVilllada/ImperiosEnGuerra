using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImperiosEnGuerra.Vista
{
    public class MenuController : MonoBehaviour
    {
        // Método dinámico: recibe el nombre desde el Inspector de Unity
        public void CambiarEscena(string nombreEscena)
        {
            SceneManager.LoadScene(nombreEscena);
        }

        // Método para el botón de salir
        public void SalirDelJuego()
        {
            Debug.Log("Saliendo del juego...");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}