using System.Collections;
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

        // Cuanto tarda (en segundos) el desvanecimiento al ser destruido.
        // Es solo visual: el Modelo ya considera destruido al edificio desde
        // el primer momento en que su vida llega a 0.
        [Header("Animacion de destruccion")]
        [SerializeField] private float duracionDesvanecimiento = 0.8f;
 
        public Edificio EdificioModelo { get; private set; }

        // Evita iniciar el desvanecimiento mas de una vez: el Controlador
        // llama ActualizarVisual() cada frame, y mientras dura la animacion
        // el edificio sigue existiendo en la escena.
        private bool destruyendose;

        // Escala con la que el PREFAB diseño el relleno de la barra. Antes
        // el codigo hacia escala.x = porcentaje (un valor entre 0 y 1), lo
        // que reemplazaba la escala original del prefab (0.1 en las barras
        // de las unidades) y dejaba la barra desproporcionada. Ahora se
        // guarda la escala original y el porcentaje se MULTIPLICA por ella.
        private Vector3 escalaInicialRelleno = Vector3.one;

        private void Awake()
        {
            if (rellenoSalud != null)
            {
                escalaInicialRelleno = rellenoSalud.localScale;
            }
        }
 
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
            // Si ya esta en pleno desvanecimiento no hay nada mas que
            // actualizar (ni barra, ni transparencia de construccion).
            if (EdificioModelo == null || destruyendose) return;

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
                // Se parte de la escala ORIGINAL del prefab y solo se
                // achica el eje X (progreso de construccion o vida). Los
                // numeros (0 a 1) ya vienen calculados por el Modelo
                // (FraccionConstruccion y PorcentajeVida): la Vista solo los
                // aplica, no hace divisiones.
                var escala = escalaInicialRelleno;

                double fraccion = listo ? EdificioModelo.PorcentajeVida : EdificioModelo.FraccionConstruccion;
                escala.x = escalaInicialRelleno.x * (float)fraccion;

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

        // Antes hacia Destroy(gameObject) de inmediato (el edificio
        // desaparecia de golpe). Ahora oculta la barra e inicia un
        // desvanecimiento; el Destroy ocurre al terminar.
        private void ManejarDestruccion()
        {
            destruyendose = true;

            if (fondoBarra != null) fondoBarra.SetActive(false);

            StartCoroutine(DesvanecerYDestruir());
        }

        // Baja el alfa del sprite de su valor actual hasta 0 durante
        // "duracionDesvanecimiento" segundos, y despues destruye el
        // GameObject. Se conserva el tinte del bando (solo cambia el alfa).
        private IEnumerator DesvanecerYDestruir()
        {
            float transcurrido = 0f;
            Color colorInicial = spriteRenderer != null ? spriteRenderer.color : Color.white;

            while (transcurrido < duracionDesvanecimiento)
            {
                // unscaledDeltaTime (y no deltaTime): FinPartidaView pone
                // Time.timeScale = 0 al mostrar victoria/derrota, y con deltaTime
                // este desvanecimiento se quedaria congelado a medias justo en
                // el momento en que cae el Centro Urbano.
                transcurrido += Time.unscaledDeltaTime;

                if (spriteRenderer != null)
                {
                    var color = colorInicial;
                    color.a = Mathf.Lerp(colorInicial.a, 0f, transcurrido / duracionDesvanecimiento);
                    spriteRenderer.color = color;
                }

                yield return null;
            }

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