using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using FirebaseAdmin.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NotificationsPush.Server
{
    public class Function
    {
        private readonly ILogger<Function> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _tableConnectionString;

        public Function(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<Function>();

            string connString = Environment.GetEnvironmentVariable("MiStorageConnection")
                                ?? throw new InvalidOperationException("La variable de entorno 'MiStorageConnection' no está configurada.");

            _blobServiceClient = new BlobServiceClient(connString);
            _tableConnectionString = connString;
        }

        [Function("Function")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("Función combinada ejecutada.");

            string action = req.Query["action"];
            if (string.IsNullOrWhiteSpace(action))
            {
                return new BadRequestObjectResult("Debe proporcionar el parámetro 'action' (por ejemplo, 'sendnotifications', 'registerdevice' o 'login').");
            }

            switch (action.ToLower())
            {
                case "sendnotifications":
                    {
                        string user = req.Query["user"];
                        string messageText = req.Query["message"];
                        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(messageText))
                        {
                            return new BadRequestObjectResult("Para 'sendnotifications' debes proporcionar 'user' y 'message' en la query string.");
                        }

                        try
                        {
                            var tableClient = new TableClient(
                                connectionString: _tableConnectionString,
                                tableName: "Devices"
                            );
                            await tableClient.CreateIfNotExistsAsync();

                            string userKey = user;
                            string filter = $"PartitionKey eq '{userKey}' and Active eq true";

                            var activeEntities = tableClient.QueryAsync<TableEntity>(filter);

                            var tokens = new List<string>();
                            await foreach (var entity in activeEntities)
                            {
                                tokens.Add(entity.RowKey);
                            }

                            if (tokens.Count == 0)
                            {
                                return new NotFoundObjectResult($"No se encontraron tokens activos para el usuario '{userKey}'.");
                            }

                            int sentCount = 0;
                            var sendResults = new List<string>();
                            foreach (var token in tokens)
                            {
                                var message = new Message()
                                {
                                    Token = token,
                                    Notification = new Notification()
                                    {
                                        Title = "Notificación personalizada",
                                        Body = messageText
                                    }
                                };

                                try
                                {
                                    string result = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                                    sendResults.Add($"Token '{token}': {result}");
                                    sentCount++;
                                }
                                catch (Exception exToken)
                                {
                                    _logger.LogWarning($"Fallo al enviar a token '{token}': {exToken.Message}");
                                    sendResults.Add($"Token '{token}': ERROR ({exToken.Message})");
                                }
                            }

                            return new OkObjectResult(new
                            {
                                Message = $"Se intentaron enviar notificaciones a {tokens.Count} token(s), {sentCount} exitosas.",
                                Details = sendResults
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Ocurrió un error al enviar la notificación: {ex.Message}");
                            return new ObjectResult($"Error interno: {ex.Message}") { StatusCode = StatusCodes.Status500InternalServerError };
                        }
                    }

                case "registerdevice":
                    {
                        string user = req.Query["user"];
                        string mail = req.Query["mail"];
                        string password = req.Query["password"];
                        string deviceType = req.Query["devicetype"];
                        string fcmToken = req.Query["fcmtoken"];
                        string activeStr = req.Query["active"];

                        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(fcmToken))
                        {
                            return new BadRequestObjectResult("Para 'registerdevice', al menos debes proporcionar 'user' y 'fcmtoken' en la query string.");
                        }

                        bool active = false;
                        if (!string.IsNullOrWhiteSpace(activeStr))
                        {
                            bool.TryParse(activeStr, out active);
                        }

                        try
                        {
                            var tableClient = new TableClient(
                                connectionString: _tableConnectionString,
                                tableName: "Devices"
                            );
                            await tableClient.CreateIfNotExistsAsync();

                            string partitionKey = user;
                            string rowKey = fcmToken;

                            var entity = new TableEntity(partitionKey, rowKey)
                            {
                                { "Mail", mail ?? string.Empty },
                                { "Password", password ?? string.Empty },
                                { "DeviceType", deviceType ?? string.Empty },
                                { "Active", active }
                            };

                            await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
                            _logger.LogInformation($"Dispositivo registrado: PartitionKey='{partitionKey}', RowKey='{rowKey}'");

                            var resultObj = new
                            {
                                Message = "Dispositivo registrado correctamente (o actualizado si ya existía).",
                                PartitionKey = partitionKey,
                                RowKey = rowKey
                            };
                            return new CreatedResult(string.Empty, resultObj);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Ocurrió un error al registrar el dispositivo: {ex.Message}");
                            return new ObjectResult($"Error interno: {ex.Message}") { StatusCode = StatusCodes.Status500InternalServerError };
                        }
                    }

                case "login":
                    {
                        string username = req.Query["username"];
                        string password = req.Query["password"];
                        string fcmToken = req.Query["fcmtoken"];

                        if (string.IsNullOrWhiteSpace(username) ||
                            string.IsNullOrWhiteSpace(password) ||
                            string.IsNullOrWhiteSpace(fcmToken))
                        {
                            return new BadRequestObjectResult(
                                "Para 'login' debes proporcionar 'username', 'password' y 'fcmtoken' en la query string."
                            );
                        }

                        try
                        {
                            var tableClient = new TableClient(
                                connectionString: _tableConnectionString,
                                tableName: "Devices"
                            );
                            await tableClient.CreateIfNotExistsAsync();

                            string credentialFilter = $"PartitionKey eq '{username}' and Password eq '{password}'";
                            TableEntity entidadExistente = null;
                            await foreach (var entity in tableClient.QueryAsync<TableEntity>(credentialFilter))
                            {
                                entidadExistente = entity;
                                break; 
                            }

                            if (entidadExistente == null)
                            {
                                return new UnauthorizedResult();
                            }

                            TableEntity entidadToken = null;
                            try
                            {
                                var response = await tableClient.GetEntityAsync<TableEntity>(
                                    partitionKey: username,
                                    rowKey: fcmToken
                                );
                                entidadToken = response.Value;
                            }
                            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                            {
                                entidadToken = null;
                            }

                            if (entidadToken != null)
                            {
                                entidadToken["Active"] = true;
                                await tableClient.UpdateEntityAsync(
                                    entidadToken,
                                    ETag.All,
                                    TableUpdateMode.Merge
                                );
                            }
                            else
                            {
                                var nuevaEntidad = new TableEntity(username, fcmToken)
                                {
                                    ["Mail"] = entidadExistente.GetString("Mail") ?? string.Empty,
                                    ["Password"] = password,
                                    ["DeviceType"] = entidadExistente.GetString("DeviceType") ?? string.Empty,
                                    ["Active"] = true
                                };
                                await tableClient.AddEntityAsync(nuevaEntidad);
                            }

                            return new OkObjectResult(new
                            {
                                Message = $"Usuario '{username}' autenticado. Token '{fcmToken}' marcado/registrado como activo."
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error al autenticar usuario o actualizar Active: {ex.Message}");
                            return new ObjectResult($"Error interno: {ex.Message}")
                            { StatusCode = StatusCodes.Status500InternalServerError };
                        }
                    }

                case "logout":
                    {
                        string username = req.Query["username"];
                        string fcmToken = req.Query["fcmtoken"];

                        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fcmToken))
                        {
                            return new BadRequestObjectResult("Para 'logout' debes proporcionar 'username' y 'fcmtoken' en la query string.");
                        }

                        try
                        {
                            var tableClient = new TableClient(
                                connectionString: _tableConnectionString,
                                tableName: "Devices"
                            );
                            await tableClient.CreateIfNotExistsAsync();

                            // Filtramos por usuario, token (RowKey) y Active = true
                            string filter = $"PartitionKey eq '{username}' and RowKey eq '{fcmToken}' and Active eq true";
                            var queryResult = tableClient.QueryAsync<TableEntity>(filter);

                            var entidadesModificadas = new List<string>();
                            await foreach (var entidad in queryResult)
                            {
                                entidad["Active"] = false;
                                await tableClient.UpdateEntityAsync(entidad, ETag.All, TableUpdateMode.Merge);
                                entidadesModificadas.Add(entidad.RowKey);
                            }

                            if (entidadesModificadas.Count == 0)
                            {
                                return new NotFoundObjectResult(
                                    $"No se encontró ningún dispositivo activo con token '{fcmToken}' para el usuario '{username}'."
                                );
                            }

                            return new OkObjectResult(new
                            {
                                Message = $"Se ha desactivado el dispositivo con token '{fcmToken}' para el usuario '{username}'.",
                                Devices = entidadesModificadas
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error al hacer logout de usuario o actualizar Active: {ex.Message}");
                            return new ObjectResult($"Error interno: {ex.Message}") { StatusCode = StatusCodes.Status500InternalServerError };
                        }
                    }


                default:
                    return new BadRequestObjectResult("Acción no válida. Use 'sendnotifications', 'registerdevice' o 'login'.");
            }
        }
    }
}
