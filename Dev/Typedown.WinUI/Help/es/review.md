# Revisión: comentarios y cambios

La función de revisión funciona como las herramientas de revisión de un procesador de texto, pero todo se escribe en el documento como texto simple, así que un colega, otro editor y un asistente de IA también pueden leerlo. Las marcas usan una convención pública llamada CriticMarkup:

| Lo que ves | Cómo se escribe en el archivo |
|---|---|
| texto agregado en verde | `{++added++}` |
| texto eliminado en rojo | `{--deleted--}` |
| un reemplazo | `{~~old~>new~~}` |
| un comentario amarillo | `{==the text==}{>>@Name 2026-10-07: the note<<}` |

Nada se guarda en otro lugar, y la revisión nunca empieza por sí sola: escribir y borrar no crea marcas.

## Agregar un comentario

Selecciona texto, haz clic derecho y elige **Agregar comentario** (o **Revisión > Agregar comentario**). Escribe la nota y revisa tu nombre (al principio es tu nombre de usuario de Windows). El texto se resalta en amarillo, con la nota al lado. Se escribe en el documento, así que **Deshacer** funciona.

## Comparar con otro archivo

**Revisión > Comparar con otro archivo...** pide una versión anterior del documento (por ejemplo, la copia que enviaste) y quién hizo los cambios. Caret escribe las diferencias entre esa versión y el documento en pantalla como una revisión *en una pestaña nueva*; tu documento no cambia. Úsalo para ver qué cambió un colega en el archivo que te devolvió. Si las pestañas están desactivadas (**Configuración > Pestañas y ventanas**), la revisión ocupa el lugar de tu documento en la ventana, así que guarda tu documento primero.

## Seguir los cambios mientras editas

**Revisión > Seguir los cambios** (también un botón en el panel Revisión) recuerda el documento tal como está ahora. Mientras editas, un momento después de que dejas de escribir, Caret muestra lo que cambiaste: el panel derecho de la vista **Dividido** dibuja el documento con lo agregado en verde y lo eliminado en rojo, cada cambio con tu nombre y el día, y el panel Revisión lista los cambios y los cuenta (también en la barra de estado). No se escribe nada en el archivo. **Dejar de seguir los cambios** olvida la versión de partida y deja el documento como está. Aceptar y rechazar cambios uno por uno, la vista Visual y escribir los cambios en el archivo como una revisión son los siguientes pasos; este tema los describirá cuando lleguen.

## Aceptar y rechazar

- *Haz clic derecho en un cambio* (verde, rojo o un reemplazo): **Aceptar el cambio** conserva lo que dice (lo agregado se queda, lo eliminado se va); **Rechazar el cambio** devuelve el texto anterior.
- *Haz clic derecho en un comentario:* **Eliminar el comentario**.
- **Revisión > Aceptar todos los cambios**, **Rechazar todos los cambios** y **Eliminar todos los comentarios** lo hacen en todo el documento, como un solo paso de Deshacer.

## Ocultar las marcas

**Configuración > Editor > Mostrar marcas de revisión** apaga los colores; las marcas se ven entonces como el texto simple que son.

## Conviene saber

- Las marcas son texto, así que un archivo con una revisión se puede guardar, enviar y abrir en cualquier editor.
- Un archivo devuelto con las marcas de un colega se lee del mismo modo: sus cambios se ven en color y tú los aceptas o los rechazas.