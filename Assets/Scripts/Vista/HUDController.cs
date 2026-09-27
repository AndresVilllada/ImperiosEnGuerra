using UnityEngine;
using TMPro;

namespace ImperiosEnGuerra.Vista
{
    public class HUDController : MonoBehaviour
    {
        [Header("Textos de Recursos (dentro de los Iconos)")]
        [SerializeField] private TMP_Text Texto_Madera;
        [SerializeField] private TMP_Text Texto_Oro;
        [SerializeField] private TMP_Text Texto_Metal;
        [SerializeField] private TMP_Text Texto_Piedra;
        [SerializeField] private TMP_Text Texto_Poblacion;

        [Header("Paneles de fin de juego (inician desactivados)")]
        [SerializeField] private GameObject Panel_Victoria;
        [SerializeField] private GameObject Panel_Derrota;

        private void Awake()
        {
            OcultarPaneles();
        }

        public void ActualizarMadera(int cantidad) => Texto_Madera.text = cantidad.ToString();
        public void ActualizarOro(int cantidad) => Texto_Oro.text = cantidad.ToString();
        public void ActualizarMetal(int cantidad) => Texto_Metal.text = cantidad.ToString();
        public void ActualizarPiedra(int cantidad) => Texto_Piedra.text = cantidad.ToString();

        public void ActualizarPoblacion(int actual, int maximo) => Texto_Poblacion.text = $"{actual}/{maximo}";

        public void MostrarVictoria()
        {
            Panel_Victoria.SetActive(true);
            Panel_Derrota.SetActive(false);
        }

        public void MostrarDerrota()
        {
            Panel_Derrota.SetActive(true);
            Panel_Victoria.SetActive(false);
        }

        public void OcultarPaneles()
        {
            Panel_Victoria.SetActive(false);
            Panel_Derrota.SetActive(false);
        }
    }
}