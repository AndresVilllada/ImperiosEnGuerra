using System.Collections.Concurrent; // ConcurrentDictionary: colecciones thread-safe
using System.Collections.Generic;    // IReadOnlyDictionary

namespace ImperiosEnGuerra.Modelo
{
    // Que hay ocupando cada celda de la matriz, en terminos generales (para
    // validaciones rapidas tipo "esta celda esta libre para moverme ahi?").
    public enum TipoCelda
    {
        Libre,
        Recurso,
        Edificio,
        Unidad
    }

    // ---------------------------------------------------------------------
    // MAPA: la matriz (por defecto 20x20) UNICA y COMPARTIDA entre Grecia y
    // la IA rival — ambos Jugador reciben la MISMA instancia de Mapa en su
    // constructor, por eso las acciones de uno afectan lo que ve el otro.
    //
    // Guarda la informacion de las celdas en DOS formas complementarias:
    //   1. Un array 2D de TipoCelda, para saber RAPIDO que tipo de cosa hay
    //      en una celda (sin tener que revisar los 3 diccionarios).
    //   2. Tres ConcurrentDictionary (uno por Recurso, Edificio y Unidad),
    //      para poder obtener la REFERENCIA REAL al objeto que esta en una
    //      posicion (por ejemplo, para llamar recurso.Extraer(...) sobre el
    //      deposito exacto que hay en esa celda).
    // ---------------------------------------------------------------------
    public class Mapa
    {
        public int Ancho { get; private set; }
        public int Alto { get; private set; }

        // El array 2D normal de .NET NO tiene version "thread-safe" nativa
        // (a diferencia de Dictionary, que si tiene ConcurrentDictionary),
        // por eso necesita este candado manual para protegerlo.
        private readonly TipoCelda[,] celdas;
        private readonly object candadoCeldas = new object();

        // Estos tres si son colecciones thread-safe de fabrica: varios
        // hilos pueden leer/escribir sin necesitar lock adicional aqui.
        private readonly ConcurrentDictionary<Posicion, Recurso> recursosEnMapa;
        private readonly ConcurrentDictionary<Posicion, Edificio> edificiosEnMapa;
        private readonly ConcurrentDictionary<Posicion, Unidad> unidadesEnMapa;

        // Expone los depositos de recurso solo para LECTURA. La Vista la
        // necesita para recorrer TODOS los depositos y dibujarlos
        // (MapaView.RenderizarRecursos), pero no debe poder agregar/quitar
        // directamente -- eso sigue siendo responsabilidad exclusiva de
        // ColocarRecurso/LiberarCelda.
        public IReadOnlyDictionary<Posicion, Recurso> RecursosEnMapa => recursosEnMapa;

        public Mapa(int ancho = 20, int alto = 20)
        {
            Ancho = ancho;
            Alto = alto;
            celdas = new TipoCelda[ancho, alto];
            recursosEnMapa = new ConcurrentDictionary<Posicion, Recurso>();
            edificiosEnMapa = new ConcurrentDictionary<Posicion, Edificio>();
            unidadesEnMapa = new ConcurrentDictionary<Posicion, Unidad>();
        }

        // Valida que una Posicion realmente caiga dentro de los limites de
        // la matriz (0 <= X < Ancho, 0 <= Y < Alto), para no intentar
        // acceder a un indice invalido del array en ningun otro metodo.
        public bool EstaDentroDelMapa(Posicion pos)
        {
            return pos.X >= 0 && pos.X < Ancho && pos.Y >= 0 && pos.Y < Alto;
        }

        // Consulta rapida: esta celda esta vacia y disponible para colocar
        // algo ahi (un edificio nuevo, moverse una unidad, etc)?
        public bool CeldaLibre(Posicion pos)
        {
            lock (candadoCeldas)
            {
                return EstaDentroDelMapa(pos) && celdas[pos.X, pos.Y] == TipoCelda.Libre;
            }
        }

        // Devuelve QUE TIPO de cosa hay en una celda (sin la referencia al
        // objeto en si, solo la categoria general).
        public TipoCelda ObtenerTipoCelda(Posicion pos)
        {
            lock (candadoCeldas)
            {
                return EstaDentroDelMapa(pos) ? celdas[pos.X, pos.Y] : TipoCelda.Libre;
            }
        }

        // Metodo privado auxiliar: actualiza el "estado general" de una
        // celda en el array. Lo usan todos los metodos publicos de abajo
        // (ColocarRecurso, ColocarEdificio, etc) para mantener el array
        // sincronizado con lo que hay realmente en los diccionarios.
        private void MarcarCelda(Posicion pos, TipoCelda tipo)
        {
            lock (candadoCeldas)
            {
                if (EstaDentroDelMapa(pos))
                {
                    celdas[pos.X, pos.Y] = tipo;
                }
            }
        }

        // ---------- Recursos ----------

        // Ubica un deposito de Recurso en una celda: lo guarda en el
        // diccionario Y actualiza el array de celdas a la vez, para que
        // ambas fuentes de informacion queden consistentes entre si.
        public void ColocarRecurso(Posicion pos, Recurso recurso)
        {
            if (!EstaDentroDelMapa(pos)) return;
            recursosEnMapa[pos] = recurso;
            MarcarCelda(pos, TipoCelda.Recurso);
        }

        // Devuelve la REFERENCIA real al Recurso en esa posicion (para
        // poder llamar .Extraer(...) sobre el), o null si no hay ninguno ahi.
        public Recurso ObtenerRecurso(Posicion pos)
        {
            recursosEnMapa.TryGetValue(pos, out var recurso);
            return recurso;
        }

        // Vacia por completo una celda: quita cualquier Recurso, Edificio o
        // Unidad que estuviera registrada ahi (por si acaso mas de uno
        // quedo asociado por error) y la marca como Libre otra vez. Se usa,
        // por ejemplo, cuando un deposito de recurso se agota del todo.
        public void LiberarCelda(Posicion pos)
        {
            if (!EstaDentroDelMapa(pos)) return;
            recursosEnMapa.TryRemove(pos, out _);
            edificiosEnMapa.TryRemove(pos, out _);
            unidadesEnMapa.TryRemove(pos, out _);
            MarcarCelda(pos, TipoCelda.Libre);
        }

        // ---------- Edificios ----------

        public void ColocarEdificio(Posicion pos, Edificio edificio)
        {
            if (!EstaDentroDelMapa(pos)) return;
            edificiosEnMapa[pos] = edificio;
            MarcarCelda(pos, TipoCelda.Edificio);
        }

        public Edificio ObtenerEdificio(Posicion pos)
        {
            edificiosEnMapa.TryGetValue(pos, out var edificio);
            return edificio;
        }

        // ---------- Unidades ----------

        public void ColocarUnidad(Posicion pos, Unidad unidad)
        {
            if (!EstaDentroDelMapa(pos)) return;
            unidadesEnMapa[pos] = unidad;
            MarcarCelda(pos, TipoCelda.Unidad);
        }

        public Unidad ObtenerUnidad(Posicion pos)
        {
            unidadesEnMapa.TryGetValue(pos, out var unidad);
            return unidad;
        }

        // Mueve una Unidad de "origen" a "destino": primero valida que el
        // destino este libre (si no, devuelve false y no hace nada), luego
        // libera la celda de origen, ocupa la de destino, y finalmente le
        // avisa a la propia Unidad que actualice su posicion interna
        // (unidad.MoverA). Esta coordinacion vive aqui, en el Mapa, y no en
        // Unidad.MoverHacia, porque el Mapa es el unico que conoce el
        // estado de TODAS las celdas a la vez.
        public bool MoverUnidad(Unidad unidad, Posicion origen, Posicion destino)
        {
            if (!EstaDentroDelMapa(destino) || !CeldaLibre(destino)) return false;

            unidadesEnMapa.TryRemove(origen, out _);
            MarcarCelda(origen, TipoCelda.Libre);

            unidadesEnMapa[destino] = unidad;
            MarcarCelda(destino, TipoCelda.Unidad);

            unidad.MoverA(destino);
            return true;
        }
    }
}