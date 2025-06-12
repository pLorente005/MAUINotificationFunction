# MauiFirebaseFunctionApp

## Descripción general

Este backend expone un conjunto de funciones HTTP que permiten gestionar el ciclo completo de notificaciones push personalizadas para dispositivos móviles conectados a una aplicación cliente desarrollada en .NET MAUI.

La arquitectura evita el uso de Azure Notification Hubs por motivos de coste, fiabilidad y falta de flexibilidad, ofreciendo en su lugar una solución directa basada en:

- **Azure Functions (HTTP Trigger)**
- **Azure Table Storage**
- **Firebase Admin SDK**

De esta forma se logra un control total sobre el registro de dispositivos, el envío de notificaciones y la gestión de tokens FCM.

---

## Servicios principales expuestos

### 1. `login`
Valida las credenciales del usuario y registra o actualiza el token FCM del dispositivo, marcándolo como activo.

**Parámetros requeridos:**
- `username` (obligatorio)
- `password` (obligatorio)
- `fcmtoken` (obligatorio)

**Comportamiento:**
- Valida si existe el usuario y contraseña.
- Si el token FCM ya existía, lo marca como activo.
- Si el token es nuevo, lo registra asociado al usuario.

---

### 2. `registerdevice`
Registra un dispositivo en el sistema. Esta operación puede realizarse previamente al login completo.

**Parámetros requeridos:**
- `user` (obligatorio)
- `fcmtoken` (obligatorio)

**Parámetros adicionales:**
- `mail`
- `password`
- `devicetype`
- `active` (booleano)

**Comportamiento:**
- Inserta o actualiza el dispositivo del usuario.
- Los tokens quedan almacenados en Azure Table Storage bajo la tabla `Devices`.

---

### 3. `sendnotifications`
Envía una notificación personalizada a todos los dispositivos activos de un usuario.

**Parámetros requeridos:**
- `user` (obligatorio)
- `message` (obligatorio)

**Comportamiento:**
- Recupera todos los tokens FCM activos asociados al usuario.
- Envía la notificación a cada token mediante Firebase Admin SDK.

---

### 4. `logout`
Desactiva el token FCM de un dispositivo específico al cerrar la sesión.

**Parámetros requeridos:**
- `username` (obligatorio)
- `fcmtoken` (obligatorio)

**Comportamiento:**
- Marca como inactivo el token FCM correspondiente al usuario.

---

## Infraestructura utilizada

- **Azure Functions (HTTP Trigger):** lógica de negocio.
- **Azure Table Storage:** persistencia de usuarios, dispositivos y tokens FCM.
- **Azure Blob Storage:** reservado para ampliaciones futuras.
- **Firebase Admin SDK:** gestión del envío de notificaciones push a los tokens FCM.

---

## Variables de entorno requeridas

Para su correcto funcionamiento, deben configurarse las siguientes variables de entorno:

- `MiStorageConnection`: Connection string de Azure Storage (se utiliza tanto para Table Storage como para Blob Storage).

**Nota:**  
Firebase Admin SDK debe estar previamente inicializado en la aplicación, cargando la clave privada de servicio desde el archivo JSON descargado de la consola de Firebase.

---

## Ventajas de la arquitectura elegida

- Coste muy inferior respecto a Azure Notification Hubs.
- Escalabilidad automática con Azure Functions.
- Control absoluto sobre cada token y cada dispositivo.
- Mayor trazabilidad y seguridad.
- Mayor facilidad de mantenimiento y ampliación futura.

---

## Flujo general del sistema

1. La aplicación MAUI obtiene el token FCM del dispositivo.
2. El cliente llama al backend (Azure Functions) para registrar el dispositivo y autenticar al usuario.
3. El backend registra el token FCM asociado al usuario en Azure Table Storage.
4. Cuando se requiere enviar una notificación, el backend consulta los tokens activos del usuario y envía la notificación personalizada mediante Firebase Admin SDK.
5. Al cerrar sesión, el token queda desactivado.

---

## Estado del proyecto

Este backend está diseñado para trabajar en conjunto con la aplicación cliente .NET MAUI disponible aquí:

[MAUINotificationApp](https://github.com/pLorente005/MAUINotificationApp)
