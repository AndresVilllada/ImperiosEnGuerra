using System;
using System.Collections.Generic;
 
namespace ImperiosEnGuerra.Modelo
{
    // ---------------------------------------------------------------------
    // EDIFICIO: clase base abstracta. Igual que Unidad, no se puede
    // instanciar directamente — solo sus hijas (TownCenter, House, etc.).
    // ---------------------------------------------------------------------
    public abstract class Edificio : IObjetivoAtacable
    {
        public Guid Id { get; }
        public string Nombre { get; protected set; }
        public Posicion Posicion { get; }
        public int VidaMaxima { get; protected set; }
        public int VidaActual { get; private set; }
        public bool EstaConstruido { get; private set; }
        public int ProgresoConstruccion { get; private set; } // 0-100
        public bool EstaDestruido => VidaActual <= 0;
        public IReadOnlyDictionary<TipoRecurso, int> Costo { get; protected set; }
 
        // Candado propio de esta instancia: protege VidaActual y ProgresoConstruccion,
        // que se pueden tocar desde el hilo de construcción Y desde un ataque
        // al mismo tiempo (dos hilos distintos, mismo objeto).
        private readonly object candado = new object();
 
        protected Edificio(string nombre, Posicion posicion, int vidaMaxima, IReadOnlyDictionary<TipoRecurso, int> costo, bool yaConstruido = false)
        {
            if (vidaMaxima <= 0) throw new ArgumentException("VidaMaxima debe ser mayor que cero.", nameof(vidaMaxima));
 
            Id = Guid.NewGuid();
            Nombre = nombre;
            Posicion = posicion;
            VidaMaxima = vidaMaxima;
            VidaActual = vidaMaxima;
            Costo = costo ?? new Dictionary<TipoRecurso, int>();
 
            EstaConstruido = yaConstruido;
            ProgresoConstruccion = yaConstruido ? 100 : 0;
        }
 
        public void RecibirDanio(int cantidad)
        {
            if (cantidad < 0) throw new ArgumentException("El daño no puede ser negativo.", nameof(cantidad));
            lock (candado)
            {
                VidaActual = Math.Max(0, VidaActual - cantidad);
            }
        }
 
        // La llama el hilo de construcción, un poquito cada vez (ver
        // Jugador.ConstruirEdificio), hasta llegar a 100%.
        public void AvanzarConstruccion(int incrementoPorcentaje)
        {
            if (incrementoPorcentaje < 0) return;
            lock (candado)
            {
                if (EstaConstruido) return;
                ProgresoConstruccion = Math.Min(100, ProgresoConstruccion + incrementoPorcentaje);
                if (ProgresoConstruccion >= 100) EstaConstruido = true;
            }
        }
 
        public override string ToString()
        {
            string estado = EstaDestruido ? "destruido"
                : !EstaConstruido ? $"en construcción {ProgresoConstruccion}%"
                : $"{VidaActual}/{VidaMaxima} HP";
            return $"{Nombre} [{estado}] en {Posicion}";
        }
    }
 
    // Centro Urbano: existe desde el arranque (no se "construye"). Si se
    // destruye, ese jugador pierde la partida (ver Partida.EstaDerrotado).
    public class TownCenter : Edificio
    {
        public TownCenter(Posicion posicion)
            : base("Centro Urbano", posicion, vidaMaxima: 500, costo: new Dictionary<TipoRecurso, int>(), yaConstruido: true)
        {
        }
    }
 
    // Casa: sube la población máxima disponible. Se paga con Madera porque
    // es la construcción "básica", igual que en el AoE real.
    public class House : Edificio
    {
        public int PoblacionQueOtorga { get; }
 
        public House(Posicion posicion, int poblacionQueOtorga = 5)
            : base("Casa", posicion, vidaMaxima: 100, costo: new Dictionary<TipoRecurso, int> { { TipoRecurso.Madera, 80 } })
        {
            if (poblacionQueOtorga <= 0) throw new ArgumentException("PoblacionQueOtorga debe ser mayor que cero.");
            PoblacionQueOtorga = poblacionQueOtorga;
        }
    }
 
    // Taller: aquí se entrena el ejército (Jugador.EntrenarLote). También
    // Madera, como House, porque es infraestructura básica.
    public class Taller : Edificio
    {
        public Taller(Posicion posicion)
            : base("Taller", posicion, vidaMaxima: 200, costo: new Dictionary<TipoRecurso, int> { { TipoRecurso.Madera, 120 } })
        {
        }
    }
 
    // Torre/muralla defensiva: se construye en Piedra (no Madera, a
    // diferencia de los demás) y puede atacar sola a quien entre en su rango.
    public class Defensa : Edificio
    {
        public int DanioAtaque { get; }
        public int RangoAtaque { get; }
 
        public Defensa(Posicion posicion, int danioAtaque = 8, int rangoAtaque = 3)
            : base("Defensa", posicion, vidaMaxima: 250, costo: new Dictionary<TipoRecurso, int> { { TipoRecurso.Piedra, 120 } })
        {
            if (danioAtaque < 0) throw new ArgumentException("DanioAtaque no puede ser negativo.");
            if (rangoAtaque <= 0) throw new ArgumentException("RangoAtaque debe ser mayor que cero.");
            DanioAtaque = danioAtaque;
            RangoAtaque = rangoAtaque;
        }
 
        // Igual patrón que Tropa.Atacar: valida vida propia, que ya esté
        // construida, que el objetivo exista y esté en rango.
        public bool Atacar(IObjetivoAtacable objetivo)
        {
            if (EstaDestruido || !EstaConstruido || objetivo == null || objetivo.EstaDestruido) return false;
            if (Posicion.DistanciaManhattanHasta(objetivo.Posicion) > RangoAtaque) return false;
 
            objetivo.RecibirDanio(DanioAtaque);
            return true;
        }
    }
}