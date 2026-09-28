using System.Collections;
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

        // Cuanto tarda (en segundos) el desvanecimiento al morir. Es solo
        // visual: el Modelo ya considera muerta a la unidad desde el primer
        // momento en que su vida llega a 0.
        [Header("Animacion de muerte")]
        [SerializeField] private float duracionDesvanecimiento = 0.6f;

        public Unidad UnidadModelo { get; private set; }

        // Evita iniciar el desvanecimiento mas de una vez: el Controlador
        // llama ActualizarVisual() cada frame, y mientras dura la animacion
        // la unidad sigue existiendo en la escena.
        private bool muriendo;

        // Escala con la que el PREFAB diseño el relleno de la barra (en este
        // proyecto es 0.1, no 1). Antes el codigo hacia escala.x = porcentaje
        // (un valor entre 0 y 1), lo que reemplazaba el 0.1 del prefab y
        // dejaba la barra hasta 10 veces mas ancha de lo diseñado. Ahora se
        // guarda la escala original y el porcentaje se MULTIPLICA por ella.
        private Vector3 escalaInicialFill = Vector3.one;

        private void Awake()
        {
            if (barraVidaFill != null)
            {
                escalaInicialFill = barraVidaFill.localScale;
            }
        }

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
            // Si ya esta en pleno desvanecimiento no hay nada mas que
            // actualizar (ni barra, ni posicion).
            if (UnidadModelo == null || muriendo) return;

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
                // Se parte de la escala ORIGINAL del prefab y solo se
                // achica el eje X segun el porcentaje de vida.
                var escala = escalaInicialFill;
                float porcentajeVida = (float)UnidadModelo.VidaActual / UnidadModelo.VidaMaxima;
                escala.x = escalaInicialFill.x * Mathf.Clamp01(porcentajeVida);
                barraVidaFill.localScale = escala;
            }

            // Sincronizar posición actual en el mapa 2D
            transform.position = new Vector3(UnidadModelo.Posicion.X + 0.5f, UnidadModelo.Posicion.Y + 0.5f, 0f);
        }

        // Antes hacia Destroy(gameObject) de inmediato (la unidad
        // desaparecia de golpe). Ahora oculta la barra de vida e inicia un
        // desvanecimiento; el Destroy ocurre al terminar.
        private void ManejarMuerte()
        {
            muriendo = true;

            if (barraVidaBase != null) barraVidaBase.SetActive(false);

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
                transcurrido += Time.deltaTime;

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