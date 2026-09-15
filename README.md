# Imperios en Guerra — Esqueleto MVC

## Cómo usarlo
1. Crea tu proyecto en Unity (3D, plataforma PC/Mac/Linux Standalone).
2. Copia la carpeta `Scripts/` completa dentro de `Assets/` de tu proyecto.
3. Ábrelo con VS Code (Unity ya genera el `.sln`/`.csproj`).
4. En el Editor de Unity, crea una escena con un GameObject vacío llamado
   `GameManager` y agrégale el componente `GameController`. Crea también los
   GameObjects/UI para `MapaView`, `RecursosView` y `MensajesView`, y
   arrástralos a los campos `[SerializeField]` del `GameController` en el
   Inspector.

## Estructura

```
Scripts/
├── Modelo/          (namespace ImperiosEnGuerra.Modelo)
│   ├── Recurso.cs      -> depósitos de oro/madera/comida en el mapa
│   ├── Unidad.cs       -> aldeanos y unidades militares
│   ├── Edificio.cs     -> Centro Urbano, cuartel, etc.
│   ├── Mapa.cs         -> matriz 15x15 del jugador
│   ├── Jugador.cs      -> nombre, mapa, recursos, unidades, edificios
│   └── Partida.cs      -> estado global, verificación de ganador
│
├── Vista/           (namespace ImperiosEnGuerra.Vista)
│   ├── MapaView.cs      -> renderiza el mapa (solo lee el Modelo)
│   ├── RecursosView.cs  -> HUD de recursos
│   └── MensajesView.cs  -> mensajes de estado y anuncio de ganador
│
└── Controlador/     (namespace ImperiosEnGuerra.Controlador)
    ├── GameController.cs  -> orquesta Modelo <-> Vista
    └── InputController.cs -> captura clics/raycasts del usuario
```

## Regla de oro de la separación
- El **Modelo** no importa `UnityEngine` en ningún archivo.
- La **Vista** solo lee el Modelo (nunca lo modifica) y expone métodos
  públicos simples (`RenderizarMapa`, `ActualizarRecursos`, `MostrarMensaje`).
- El **Controlador** es el único que crea/modifica instancias del Modelo y
  el único que llama a los métodos de la Vista.

## Qué falta (fases siguientes, no incluidas aún en este esqueleto)
- **Concurrencia**: recolección de recursos, construcción, entrenamiento y
  movimiento con `Task`/`Thread`, más una cola thread-safe (o
  `SynchronizationContext`) para devolver resultados al hilo principal de
  Unity dentro de `GameController.Update()`.
- **Sincronización**: `lock` / `System.Collections.Concurrent` sobre los
  recursos y estructuras compartidas.
- **Red**: sockets/WebSockets/REST/SOAP para comunicar acciones entre las
  dos instancias del juego, en un hilo separado que no bloquee la UI.
- **Archivos**: `configuracion.txt`, `log_partida.txt`, `resultado_final.txt`
  vía `System.IO`.

Te recomiendo abordar esto en ese mismo orden: primero dejar el MVC
compilando y mostrando algo en pantalla, luego meter concurrencia local
(recolección/construcción), y al final la comunicación en red — así cada
capa se prueba por separado antes de integrarlas.
