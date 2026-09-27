using UnityEngine;
using ImperiosEnGuerra.Modelo;

namespace ImperiosEnGuerra.Vista
{
    public class UnidadView : MonoBehaviour
    {
        [Header("Referencias Visuales")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("Barra de Vida (Canvas_UI)")]
        [SerializeField] private GameObject barraVidaBase;   // Corresponde a 'BarraVida_Base'
        [SerializeField] private Transform barraVidaFill;     // Corresponde a 'BarraVida_Fill'

        [Header("Sprites (uno por tipo de unidad)")]
        [SerializeField] private Sprite spriteAldeano;
        [SerializeField] private Sprite spriteEspadachin;
        [SerializeField] private Sprite spritePiquero;
        [SerializeField] private Sprite spriteArquero;

        public Unidad UnidadModelo { get; private set; }

        // Nuevo parámetro 'esDeIA' para teñir la tropa enemiga
        public void Inicializar(Unidad unidad, bool esDeIA = false)
        {
            UnidadModelo = unidad;
            if (spriteRenderer != null) 
            {
                spriteRenderer.sprite = ElegirSprite(unidad);
                // Tinte rojizo suave si es del bando rival
                spriteRenderer.color = esDeIA ? new Color(1f, 0.65f, 0.65f) : Color.white;
            }
            transform.position = new Vector3(unidad.Posicion.X + 0.5f, unidad.Posicion.Y + 0.5f, 0f);
            ActualizarVisual();
        }

        public void ActualizarVisual()
        {
            if (UnidadModelo == null) return;

            // Verificar si la unidad murió
            if (UnidadModelo.EstaDestruido)
            {
                ManejarMuerte();
                return;
            }

            // Actualizar la barra de vida
            if (barraVidaBase != null)
            {
                bool mostrarBarra = UnidadModelo.VidaActual < UnidadModelo.VidaMaxima;
                barraVidaBase.SetActive(mostrarBarra);
            }

            if (barraVidaFill != null)
            {
                var escala = barraVidaFill.localScale;
                float porcentajeVida = (float)UnidadModelo.VidaActual / UnidadModelo.VidaMaxima;
                escala.x = Mathf.Clamp01(porcentajeVida);
                barraVidaFill.localScale = escala;
            }

            // Sincronizar posición actual en el mapa 2D
            transform.position = new Vector3(UnidadModelo.Posicion.X + 0.5f, UnidadModelo.Posicion.Y + 0.5f, 0f);
        }

        private void ManejarMuerte()
        {
            Destroy(gameObject);
        }

        private Sprite ElegirSprite(Unidad unidad) => unidad switch
        {
            Villager => spriteAldeano,
            Espadachin => spriteEspadachin,
            Piquero => spritePiquero,
            Arquero => spriteArquero,
            _ => null,
        };
    }
}