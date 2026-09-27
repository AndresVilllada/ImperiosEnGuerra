using UnityEngine;
using ImperiosEnGuerra.Modelo;
 
namespace ImperiosEnGuerra.Vista
{
    public class EdificioView : MonoBehaviour
    {
        [Header("Referencias Visuales")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        
        [Header("Barra de UI (FondoBarra y RellenoSalud)")]
        [SerializeField] private GameObject fondoBarra;       // Corresponde a 'FondoBarra' en tu Canvas_UI
        [SerializeField] private Transform rellenoSalud;       // Corresponde a 'RellenoSalud' en tu Canvas_UI
 
        [Header("Sprites (uno por tipo de edificio)")]
        [SerializeField] private Sprite spriteTownCenter; // Edificio_Castillo
        [SerializeField] private Sprite spriteHouse;      // Edificio_Casa
        [SerializeField] private Sprite spriteTaller;     // Edificio_Cuartel
        [SerializeField] private Sprite spriteDefensa;    // Edificio_Torre
 
        public Edificio EdificioModelo { get; private set; }
 
        // Nuevo parámetro 'esDeIA' para teñir el edificio si es del enemigo
        public void Inicializar(Edificio edificio, bool esDeIA = false)
        {
            EdificioModelo = edificio;
            if (spriteRenderer != null) 
            {
                spriteRenderer.sprite = ElegirSprite(edificio);
                // Tinte rojizo suave para la IA, color original (blanco) para el humano
                spriteRenderer.color = esDeIA ? new Color(1f, 0.65f, 0.65f) : Color.white;
            }
            transform.position = new Vector3(edificio.Posicion.X + 0.5f, edificio.Posicion.Y + 0.5f, 0f);
            ActualizarVisual();
        }
 
        public void ActualizarVisual()
        {
            if (EdificioModelo == null) return;

            // Verificar si el edificio fue destruido
            if (EdificioModelo.EstaDestruido)
            {
                ManejarDestruccion();
                return;
            }
 
            bool listo = EdificioModelo.EstaConstruido;
 
            // Manejo de la barra flotante
            if (fondoBarra != null) 
            {
                bool mostrarBarra = !listo || (EdificioModelo.VidaActual < EdificioModelo.VidaMaxima);
                fondoBarra.SetActive(mostrarBarra);
            }

            if (rellenoSalud != null)
            {
                var escala = rellenoSalud.localScale;

                if (!listo)
                {
                    escala.x = Mathf.Clamp01(EdificioModelo.ProgresoConstruccion / 100f);
                }
                else
                {
                    float porcentajeVida = (float)EdificioModelo.VidaActual / EdificioModelo.VidaMaxima;
                    escala.x = Mathf.Clamp01(porcentajeVida);
                }

                rellenoSalud.localScale = escala;
            }
 
            // Ajustar transparencia si aún se está construyendo
            if (spriteRenderer != null)
            {
                var color = spriteRenderer.color;
                // Mantiene el canal alfa pero preserva el tinte del bando
                color.a = listo ? 1f : 0.6f;
                spriteRenderer.color = color;
            }
        }

        private void ManejarDestruccion()
        {
            Destroy(gameObject);
        }
 
        private Sprite ElegirSprite(Edificio edificio) => edificio switch
        {
            TownCenter => spriteTownCenter,
            House => spriteHouse,
            Taller => spriteTaller,
            Defensa => spriteDefensa,
            _ => null,
        };
    }
}