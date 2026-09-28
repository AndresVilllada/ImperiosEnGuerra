using System;                        // Math.Abs / Math.Max (usados en la busqueda de celdas libres)
using System.Collections.Concurrent; // ConcurrentDictionary: colecciones thread-safe
using System.Collections.Generic;    // IReadOnlyDictionary
using System.Linq;                   // OrderBy (IntentarPasoHacia)

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

        // Busca la celda libre mas cercana a "origen", revisando anillos
        // cada vez mas grandes alrededor (radio 1, luego 2, etc.) hasta
        // encontrar una que CeldaLibre() acepte. Antes esta busqueda vivia
        // en el GameController (PosicionLibreCercana), pero es LOGICA sobre
        // el estado del mapa, asi que le corresponde al Modelo — el
        // Controlador solo debe pedirla, no calcularla.
        //
        // No hace falta validar los limites aparte: CeldaLibre ya devuelve
        // false para cualquier posicion fuera del mapa.
        public Posicion BuscarCeldaLibreCercana(Posicion origen, int radioMaximo = 5)
        {
            for (int radio = 1; radio <= radioMaximo; radio++)
            {
                for (int dx = -radio; dx <= radio; dx++)
                {
                    for (int dy = -radio; dy <= radio; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radio) continue; // solo el borde del anillo

                        var candidata = new Posicion(origen.X + dx, origen.Y + dy);
                        if (CeldaLibre(candidata)) return candidata;
                    }
                }
            }
            return origen; // fallback improbable: no habia ninguna celda libre cerca
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

        // Busca el deposito NO agotado de ese tipo mas cercano a "origen"
        // (distancia Manhattan, la misma que usa el resto del Modelo). Lo usan
        // los aldeanos para saber a donde ir a recolectar. Devuelve false si
        // ya no queda ningun deposito de ese tipo.
        //
        // Recorre el ConcurrentDictionary directamente: es seguro aunque otro
        // hilo retire un deposito agotado al mismo tiempo (si eso pasa, el
        // aldeano lo descubre al llegar y busca otro).
        public bool TryBuscarRecursoCercano(TipoRecurso tipo, Posicion origen, out Posicion posicion, out Recurso recurso)
        {
            posicion = default;
            recurso = null;
            int menorDistancia = int.MaxValue;

            foreach (var par in recursosEnMapa)
            {
                if (par.Value.Tipo != tipo || par.Value.EstaAgotado()) continue;

                int distancia = origen.DistanciaManhattanHasta(par.Key);
                if (distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    posicion = par.Key;
                    recurso = par.Value;
                }
            }

            return recurso != null;
        }

        // Retira del mapa un deposito que ya se agoto, de forma ATOMICA y
        // UNA SOLA VEZ: devuelve true solo para el hilo que realmente lo
        // retiro. Cuando dos aldeanos rivales agotan el mismo deposito casi
        // al mismo tiempo, ambos llaman esto, pero solo uno "gana" y anuncia
        // el agotamiento (antes el aviso salia duplicado).
        public bool TryRetirarRecursoAgotado(Posicion pos, Recurso recurso)
        {
            lock (candadoCeldas)
            {
                if (!recursosEnMapa.TryGetValue(pos, out var enCelda)
                    || !ReferenceEquals(enCelda, recurso)
                    || !recurso.EstaAgotado())
                {
                    return false;
                }

                recursosEnMapa.TryRemove(pos, out _);
                MarcarCelda(pos, TipoCelda.Libre);
                return true;
            }
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
            SuscribirDestruccion(edificio);
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
            SuscribirMuerte(unidad);
        }

        // Coloca una Unidad en la celda libre mas cercana a "origen" (o en el
        // propio "origen" si esta libre) de forma ATOMICA: buscar la celda y
        // ocuparla ocurre dentro del mismo lock, asi dos hilos que entrenan
        // tropas al mismo tiempo (EntrenarLote lanza 5 a la vez) nunca eligen
        // la misma celda. Antes las 5 tropas de un lote se colocaban todas en
        // la MISMA celda: quedaban apiladas y parecia que una sola tropa
        // tenia varias barras de vida. Tambien actualiza la posicion interna
        // de la unidad (unidad.MoverA). Devuelve false si no hay ninguna
        // celda libre dentro del radio.
        public bool TryColocarUnidadCerca(Posicion origen, Unidad unidad, out Posicion colocada, int radioMaximo = 5)
        {
            lock (candadoCeldas)
            {
                for (int radio = 0; radio <= radioMaximo; radio++)
                {
                    for (int dx = -radio; dx <= radio; dx++)
                    {
                        for (int dy = -radio; dy <= radio; dy++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radio) continue; // solo el borde del anillo (radio 0 = el propio origen)

                            var candidata = new Posicion(origen.X + dx, origen.Y + dy);
                            if (!CeldaLibre(candidata)) continue; // el lock es reentrante: mismo hilo, no hay deadlock

                            unidadesEnMapa[candidata] = unidad;
                            MarcarCelda(candidata, TipoCelda.Unidad);
                            unidad.MoverA(candidata);
                            SuscribirMuerte(unidad);

                            colocada = candidata;
                            return true;
                        }
                    }
                }
            }

            colocada = origen;
            return false;
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
        //
        // Ahora TODO el metodo va dentro de un lock (antes eran pasos
        // sueltos): asi una unidad que muere justo mientras se mueve no
        // puede "resucitar" su celda, y dos unidades no pueden reclamar el
        // mismo destino a la vez.
        public bool MoverUnidad(Unidad unidad, Posicion origen, Posicion destino)
        {
            lock (candadoCeldas)
            {
                // Una unidad muerta ya no se mueve: su celda fue liberada al
                // morir (ver LiberarPorMuerte) y no debe volver a ocuparse.
                if (!unidad.EstaViva) return false;

                if (!EstaDentroDelMapa(destino) || !CeldaLibre(destino)) return false;

                unidadesEnMapa.TryRemove(origen, out _);
                MarcarCelda(origen, TipoCelda.Libre);

                unidadesEnMapa[destino] = unidad;
                MarcarCelda(destino, TipoCelda.Unidad);

                unidad.MoverA(destino);
                return true;
            }
        }

        // Da UN paso de la unidad hacia "destino". Prueba primero las celdas
        // vecinas que ACERCAN (ordenadas de la mas a la menos cercana). Si
        // todas estan ocupadas (otra unidad, un edificio, un recurso), a
        // veces prueba tambien una que no acerque, para poder rodear el
        // obstaculo en vez de quedarse trabada para siempre. Devuelve true si
        // la unidad avanzo. MoverUnidad valida y ejecuta el paso de forma
        // atomica.
        //
        // "aleatorio" es una funcion que devuelve un entero entre 0 y (max-1);
        // se recibe de afuera porque System.Random no es thread-safe y cada
        // llamador (aldeanos, tropas de la IA) ya tiene el suyo protegido.
        // Antes este codigo era privado de JugadorIA; ahora lo comparten los
        // aldeanos que caminan hacia los depositos y las tropas de la IA.
        public bool IntentarPasoHacia(Unidad unidad, Posicion destino, Func<int, int> aleatorio)
        {
            var actual = unidad.Posicion;
            int distanciaActual = actual.DistanciaManhattanHasta(destino);

            var vecinos = new[]
            {
                new Posicion(actual.X + 1, actual.Y),
                new Posicion(actual.X - 1, actual.Y),
                new Posicion(actual.X, actual.Y + 1),
                new Posicion(actual.X, actual.Y - 1),
            }.OrderBy(p => p.DistanciaManhattanHasta(destino));

            foreach (var siguiente in vecinos)
            {
                bool acerca = siguiente.DistanciaManhattanHasta(destino) < distanciaActual;

                // Un paso que aleja solo se intenta 1 de cada 3 veces: asi
                // rodea obstaculos sin ponerse a oscilar todo el tiempo.
                if (!acerca && aleatorio(3) != 0) continue;

                if (MoverUnidad(unidad, actual, siguiente)) return true;
            }

            return false;
        }

        // ---------- Muerte de unidades ----------

        // Se suscribe al aviso "Murio" de la unidad. Se quita antes de
        // agregar para que, si por alguna razon se llama dos veces con la
        // misma unidad, el aviso no quede duplicado.
        private void SuscribirMuerte(Unidad unidad)
        {
            unidad.Murio -= LiberarPorMuerte;
            unidad.Murio += LiberarPorMuerte;
        }

        // Cuando una unidad muere, su celda queda libre otra vez. La unidad
        // SIGUE en Jugador.Unidades (la condicion de victoria necesita
        // saber que alguna vez tuvo tropas); lo unico que se libera es el
        // espacio fisico del mapa. Solo se libera si la celda sigue
        // registrada a nombre de ESTA unidad (evita borrar a otra que ya
        // se haya movido ahi).
        private void LiberarPorMuerte(Unidad unidad)
        {
            lock (candadoCeldas)
            {
                var pos = unidad.Posicion;
                if (unidadesEnMapa.TryGetValue(pos, out var enCelda) && ReferenceEquals(enCelda, unidad))
                {
                    unidadesEnMapa.TryRemove(pos, out _);
                    MarcarCelda(pos, TipoCelda.Libre);
                }
            }
        }

        // ---------- Destruccion de edificios ----------

        // Mismo patron que SuscribirMuerte, pero para el aviso "Destruido"
        // de un Edificio.
        private void SuscribirDestruccion(Edificio edificio)
        {
            edificio.Destruido -= LiberarPorDestruccion;
            edificio.Destruido += LiberarPorDestruccion;
        }

        // Cuando un edificio es destruido, su celda queda libre otra vez
        // (ya se puede construir o caminar por ahi). El edificio SIGUE en
        // Jugador.Edificios: Jugador.CentroUrbanoDestruido() lo necesita
        // para saber si el Centro Urbano cayo y definir al ganador; lo
        // unico que se libera es el espacio fisico del mapa.
        private void LiberarPorDestruccion(Edificio edificio)
        {
            lock (candadoCeldas)
            {
                var pos = edificio.Posicion;
                if (edificiosEnMapa.TryGetValue(pos, out var enCelda) && ReferenceEquals(enCelda, edificio))
                {
                    edificiosEnMapa.TryRemove(pos, out _);
                    MarcarCelda(pos, TipoCelda.Libre);
                }
            }
        }
    }
}