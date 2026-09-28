using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ImperiosEnGuerra.Modelo
{
    // ---------------------------------------------------------------------
    // POSICION: una coordenada (x, y) dentro del mapa 20x20.
    // ---------------------------------------------------------------------
    public struct Posicion : IEquatable<Posicion>
    {
        public int X { get; }
        public int Y { get; }

        public Posicion(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int DistanciaManhattanHasta(Posicion otra) => Math.Abs(X - otra.X) + Math.Abs(Y - otra.Y);

        public bool Equals(Posicion otra) => X == otra.X && Y == otra.Y;
        public override bool Equals(object obj) => obj is Posicion otra && Equals(otra);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(Posicion a, Posicion b) => a.Equals(b);
        public static bool operator !=(Posicion a, Posicion b) => !a.Equals(b);
    }

    // ---------------------------------------------------------------------
    // INTERFAZ: cualquier cosa "atacable" (Unidad o Edificio).
    // ---------------------------------------------------------------------
    public interface IObjetivoAtacable
    {
        Posicion Posicion { get; }
        bool EstaDestruido { get; }
        void RecibirDanio(int cantidad);
    }

    // ---------------------------------------------------------------------
    // UNIDAD: clase base abstracta para aldeano y militares.
    // ---------------------------------------------------------------------
    public abstract class Unidad : IObjetivoAtacable
    {
        public Guid Id { get; }
        public string Nombre { get; protected set; }
        public Posicion Posicion { get; private set; }
        public int VidaMaxima { get; protected set; }
        public int VidaActual { get; private set; }
        public bool EstaViva => VidaActual > 0;
        public bool EstaDestruido => !EstaViva;

        // Vida actual como fraccion entre 0.0 y 1.0. La calcula el MODELO (y no
        // la Vista) porque cualquier interfaz que muestre la vida la necesita:
        // la Vista solo aplica el numero. Siempre esta entre 0 y 1 porque
        // RecibirDanio no baja de 0 y Curar no pasa de VidaMaxima.
        public double PorcentajeVida => (double)VidaActual / VidaMaxima;
        public IReadOnlyDictionary<TipoRecurso, int> Costo { get; protected set; }

        // Aviso de "esta unidad acaba de morir". Se dispara UNA sola vez, en
        // el momento exacto en que su vida pasa de mayor que 0 a 0. Lo usa el
        // Mapa para liberar la celda de la unidad muerta (asi los cadaveres
        // no siguen bloqueando celdas). La unidad no conoce al Mapa: solo
        // avisa, y quien quiera enterarse se suscribe.
        public event Action<Unidad> Murio;

        private readonly object candado = new object();

        protected Unidad(string nombre, Posicion posicion, int vidaMaxima, IReadOnlyDictionary<TipoRecurso, int> costo)
        {
            if (vidaMaxima <= 0) throw new ArgumentException("VidaMaxima debe ser mayor que cero.", nameof(vidaMaxima));

            Id = Guid.NewGuid();
            Nombre = nombre;
            Posicion = posicion;
            VidaMaxima = vidaMaxima;
            VidaActual = vidaMaxima;
            Costo = costo ?? new Dictionary<TipoRecurso, int>();
        }

        public void RecibirDanio(int cantidad)
        {
            if (cantidad < 0) throw new ArgumentException("El daño no puede ser negativo.", nameof(cantidad));

            bool acabaDeMorir = false;
            lock (candado)
            {
                bool estabaViva = VidaActual > 0;
                VidaActual = Math.Max(0, VidaActual - cantidad);
                acabaDeMorir = estabaViva && VidaActual == 0;
            }

            // El evento se dispara FUERA del lock: quien lo escucha (el Mapa)
            // toma sus propios candados, y hacerlo con el candado de esta
            // unidad todavia tomado podria causar un interbloqueo (deadlock)
            // con Mapa.MoverUnidad, que toma los candados en orden contrario.
            if (acabaDeMorir)
            {
                Murio?.Invoke(this);
            }
        }

        public void Curar(int cantidad)
        {
            if (cantidad < 0) throw new ArgumentException("La curación no puede ser negativa.", nameof(cantidad));
            lock (candado)
            {
                if (!EstaViva) return;
                VidaActual = Math.Min(VidaMaxima, VidaActual + cantidad);
            }
        }

        public virtual void MoverA(Posicion nuevaPosicion)
        {
            lock (candado)
            {
                if (!EstaViva) return;
                Posicion = nuevaPosicion;
            }
        }

        // Camina hacia "destino" en su propio hilo, una celda cada
        // "intervaloMs". Espera con Task.Delay (no Thread.Sleep): mientras
        // espera NO ocupa un hilo del pool. "token" permite detener el
        // movimiento desde afuera (Jugador.DetenerHilos); si no se pasa,
        // el hilo termina solo al llegar, quedar bloqueado o morir la unidad.
        public void MoverHacia(Mapa mapa, Posicion destino, ConcurrentQueue<EventoJuego> eventos, int intervaloMs = 300, CancellationToken token = default)
        {
            if (mapa == null) return;

            Task.Run(async () =>
            {
                try
                {
                    while (EstaViva && Posicion != destino)
                    {
                        var actual = Posicion;
                        int dx = Math.Sign(destino.X - actual.X);
                        int dy = Math.Sign(destino.Y - actual.Y);

                        var siguiente = dx != 0
                            ? new Posicion(actual.X + dx, actual.Y)
                            : new Posicion(actual.X, actual.Y + dy);

                        bool avanzo = mapa.MoverUnidad(this, actual, siguiente);

                        if (!avanzo)
                        {
                            eventos?.Enqueue(new EventoJuego("Movimiento", $"{Nombre} bloqueado, no puede avanzar a {siguiente}."));
                            break;
                        }

                        await Task.Delay(intervaloMs, token);
                    }

                    if (Posicion == destino)
                    {
                        eventos?.Enqueue(new EventoJuego("Movimiento", $"{Nombre} llegó a {destino}."));
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancelacion normal (fin de la partida): el hilo termina.
                }
            });
        }

        public override string ToString() => $"{Nombre} [{(EstaViva ? $"{VidaActual}/{VidaMaxima} HP" : "muerta")}] en {Posicion}";
    }

    // ---------------------------------------------------------------------
    // VILLAGER (Aldeano): recolecta, no ataca.
    // ---------------------------------------------------------------------
    public class Villager : Unidad
    {
        public int VelocidadRecoleccion { get; }

        public Villager(Posicion posicion, int velocidadRecoleccion = 5)
            : base("Aldeano", posicion, vidaMaxima: 25, costo: new Dictionary<TipoRecurso, int> { { TipoRecurso.Madera, 40 } })
        {
            if (velocidadRecoleccion <= 0) throw new ArgumentException("VelocidadRecoleccion debe ser mayor que cero.");
            VelocidadRecoleccion = velocidadRecoleccion;
        }
    }

    // ---------------------------------------------------------------------
    // TROPA: clase base abstracta para toda unidad militar.
    // ---------------------------------------------------------------------
    public abstract class Tropa : Unidad
    {
        public int DanioAtaque { get; protected set; }
        public int RangoAtaque { get; protected set; }

        protected Tropa(string nombre, Posicion posicion, int vidaMaxima, int danioAtaque, int rangoAtaque, IReadOnlyDictionary<TipoRecurso, int> costo)
            : base(nombre, posicion, vidaMaxima, costo)
        {
            if (danioAtaque < 0) throw new ArgumentException("DanioAtaque no puede ser negativo.");
            if (rangoAtaque <= 0) throw new ArgumentException("RangoAtaque debe ser mayor que cero.");
            DanioAtaque = danioAtaque;
            RangoAtaque = rangoAtaque;
        }

        public bool Atacar(IObjetivoAtacable objetivo)
        {
            if (!EstaViva || objetivo == null || objetivo.EstaDestruido) return false;
            if (Posicion.DistanciaManhattanHasta(objetivo.Posicion) > RangoAtaque) return false;

            objetivo.RecibirDanio(DanioAtaque);
            return true;
        }
    }

    // ---------------------------------------------------------------------
    // 3 tropas concretas, todas soldados humanos, cada una con su propio
    // rol. Se redujo de las 10 originales a estas 3 porque el pack de
    // sprites conseguido solo trae arte reconocible para estos tres tipos.
    // ---------------------------------------------------------------------

    // Cuerpo a cuerpo básico. Barato, rápido de sacar.
    public class Espadachin : Tropa
    {
        public Espadachin(Posicion posicion)
            : base("Espadachín", posicion, vidaMaxima: 50, danioAtaque: 10, rangoAtaque: 1,
                   costo: new Dictionary<TipoRecurso, int> { { TipoRecurso.Oro, 30 } })
        {
        }
    }

    // Cuerpo a cuerpo "tanque": mucha más vida que el Espadachín, pero pega
    // menos por golpe. Pensado para aguantar el frente. Cuesta Madera además
    // de Oro (la lanza larga se fabrica con madera).
    public class Piquero : Tropa
    {
        public Piquero(Posicion posicion)
            : base("Piquero", posicion, vidaMaxima: 70, danioAtaque: 7, rangoAtaque: 1,
                   costo: new Dictionary<TipoRecurso, int> { { TipoRecurso.Oro, 20 }, { TipoRecurso.Madera, 15 } })
        {
        }
    }

    // A distancia, barato: arco simple. Menos vida que las cuerpo a cuerpo,
    // pero pega antes de que lo alcancen. Buena opción temprana antes de
    // tener suficiente Oro para algo más pesado.
    public class Arquero : Tropa
    {
        public Arquero(Posicion posicion)
            : base("Arquero", posicion, vidaMaxima: 30, danioAtaque: 8, rangoAtaque: 3,
                   costo: new Dictionary<TipoRecurso, int> { { TipoRecurso.Oro, 20 } })
        {
        }
    }
}
