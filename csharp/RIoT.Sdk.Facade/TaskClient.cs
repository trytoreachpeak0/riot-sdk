using System.Text.Json;
using Microsoft.Kiota.Abstractions.Serialization;
using RIoT.Sdk.Core;
using RIoT.Sdk.Generated.TaskApi.Models;

namespace RIoT.Sdk.Facade;

/// <summary>
/// Thin task-module facade. Expose additional endpoints as needed.
/// </summary>
public sealed class TaskClient
{
    private readonly RiotSession _session;

    internal TaskClient(RiotSession session) => _session = session;

    /// <summary>
    /// GET /api/task/vehicles/getAllVehicleSimpleInfo — list DispatchableVehicle rows (BC-VEH-002).
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    public async Task<IReadOnlyList<DispatchableVehicle>> GetDispatchableVehiclesAsync(
        CancellationToken cancellationToken = default)
    {
        var client = _session.CreateGeneratedTaskClient();
        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Task.Vehicles.GetAllVehicleSimpleInfo
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            "getAllVehicleSimpleInfo");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);

        var result = response.Result ?? [];
        return result
            .Where(v => !string.IsNullOrWhiteSpace(v.DeviceKey) && !string.IsNullOrWhiteSpace(v.DeviceName))
            .Select(v => new DispatchableVehicle(v.DeviceKey!, v.DeviceName!))
            .ToList();
    }

    /// <summary>
    /// Exact deviceName → deviceKey via getAllVehicleSimpleInfo (BC-VEH-002).
    /// </summary>
    public async Task<string> ResolveDeviceKeyAsync(
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var vehicles = await GetDispatchableVehiclesAsync(cancellationToken).ConfigureAwait(false);
        return DispatchableVehicleLookup.ResolveDeviceKey(vehicles, deviceName);
    }

    /// <summary>
    /// GET /api/task/vehicles/getVehicleInfoByDeviceKey?key={deviceKey}.
    /// Returns stable vehicle-card facts and rejects a mismatched response identity.
    /// </summary>
    public async Task<VehicleCard> GetVehicleCardAsync(
        string deviceKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        var client = _session.CreateGeneratedTaskClient();
        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Task.Vehicles.GetVehicleInfoByDeviceKey.GetAsync(config =>
            {
                config.QueryParameters.Key = deviceKey;
            }, cancellationToken).ConfigureAwait(false),
            "getVehicleInfoByDeviceKey");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);
        var result = response.Result ?? throw new RiotApiException(
            "getVehicleInfoByDeviceKey returned null result.",
            businessCode: "vehicle-card-missing");

        if (!string.Equals(result.DeviceKey, deviceKey, StringComparison.Ordinal))
        {
            throw new RiotApiException(
                "getVehicleInfoByDeviceKey returned a mismatched deviceKey.",
                businessCode: "vehicle-key-mismatch");
        }

        return new VehicleCard(
            deviceKey,
            result.Enable,
            result.Status,
            result.ProcState,
            result.CurrentMap,
            result.CurrentPosition,
            result.Battery,
            result.BatteryState,
            result.Speed,
            result.LockStatus,
            result.OrderTaskId);
    }

    /// <summary>
    /// GET /api/task/v1/task/getVehicleInfo/{deviceKey}.
    /// Returns the diagnostic vehicle and task halves as one identity-checked snapshot.
    /// </summary>
    /// <remarks>
    /// One request, read twice from the same bytes: the generated model for every field as before, and the raw
    /// <c>vehicle.mode</c> string for <see cref="VehicleExecutionFacts.Mode"/> (see <see cref="ExactMode"/>). Sent without an
    /// error mapping, exactly as the generated <c>GetAsync</c> sends it, so a failed request throws what it always threw.
    /// </remarks>
    public async Task<VehicleExecutionFacts> GetVehicleExecutionFactsAsync(
        string deviceKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        var client = _session.CreateGeneratedTaskClient();
        var requestInfo = client.Api.Task.V1.Task.GetVehicleInfo[deviceKey].ToGetRequestInformation();
        var stream = await _session.Adapter
            .SendPrimitiveAsync<Stream>(requestInfo, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        byte[] body;
        if (stream is null)
        {
            body = [];
        }
        else
        {
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                body = buffer.ToArray();
            }
        }

        VehicleAllInfo? response = null;
        string? mode = null;
        if (body.Length > 0)
        {
            using (var modelStream = new MemoryStream(body, writable: false))
            {
                var rootNode = await ParseNodeFactoryRegistry.DefaultInstance
                    .GetRootParseNodeAsync("application/json", modelStream, cancellationToken)
                    .ConfigureAwait(false);
                response = rootNode.GetObjectValue(VehicleAllInfo.CreateFromDiscriminatorValue);
            }

            mode = ExactMode(body);
        }

        if (response?.Vehicle is null || response.VehicleTaskInfo is null)
        {
            throw new RiotApiException(
                "getVehicleInfo returned a missing vehicle or vehicleTaskInfo.",
                businessCode: "vehicle-execution-facts-missing");
        }

        if (!string.Equals(response.VehicleTaskInfo.Key, deviceKey, StringComparison.Ordinal))
        {
            throw new RiotApiException(
                "getVehicleInfo returned a mismatched vehicleTaskInfo key.",
                businessCode: "vehicle-key-mismatch");
        }

        return new VehicleExecutionFacts(
            deviceKey,
            response.Vehicle.MovementState?.ToString(),
            response.Vehicle.ControlState?.ToString(),
            response.Vehicle.EmergencyState?.ToString(),
            response.Vehicle.BreakSwitchState?.ToString(),
            response.Vehicle.LocationState?.ToString(),
            response.Vehicle.Speed,
            response.VehicleTaskInfo.ProcState?.ToString(),
            response.VehicleTaskInfo.ProcessingOrder,
            response.VehicleTaskInfo.Enable,
            response.VehicleTaskInfo.IntegrationLevel?.ToString(),
            mode);
    }

    /// <summary>The three <c>mode</c> values RIoT's task API declares.</summary>
    private static readonly string[] KnownModes = ["MODE_AUTO", "MODE_MANUAL", "UNKNOWN"];

    /// <summary>
    /// The raw <c>vehicle.mode</c> when it is exactly one of <see cref="KnownModes"/>, otherwise null.
    /// </summary>
    /// <remarks>
    /// Not the generated enum: Kiota's <c>EnumHelpers.GetEnumValue</c> ends in a case-insensitive <c>Enum.TryParse</c> that also
    /// takes numbers, so <c>"0"</c>, <c>" 0"</c>, <c>"mode_auto"</c> and <c>" MODE_AUTO"</c> came out as MODE_AUTO, <c>"3"</c> as
    /// <c>"3"</c> and <c>"MODE_AUTO,MODE_MANUAL"</c> as MODE_MANUAL (riot-sdk#3 review S1). Python's reader is by member name and
    /// gave None for all of them. A consumer proves standstill on MODE_AUTO (control-server#639), so a value that is not spelled
    /// exactly as declared is no mode at all, the same in both languages.
    /// </remarks>
    private static string? ExactMode(byte[] body)
    {
        try
        {
            using var stream = new MemoryStream(body, writable: false);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("vehicle", out var vehicle) &&
                vehicle.ValueKind == JsonValueKind.Object &&
                vehicle.TryGetProperty("mode", out var mode) &&
                mode.ValueKind == JsonValueKind.String)
            {
                var text = mode.GetString();
                return Array.Find(KnownModes, known => string.Equals(known, text, StringComparison.Ordinal));
            }
        }
        catch (JsonException)
        {
            // The generated read above already parsed these bytes; a document it accepted and this one does not is no mode.
        }

        return null;
    }

    /// <summary>
    /// POST /api/task/v1/route/getRouteCostsBy — RouteCost for one vehicle to a station (BC-ROUTE-001).
    /// <c>CostsMm = -1</c> means unreachable and is returned, not thrown (ADR-sdk-0005).
    /// </summary>
    public async Task<RouteCost> GetRouteCostAsync(
        int mapId,
        int stationId,
        string deviceKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        var client = _session.CreateGeneratedTaskClient();
        var body = new RIoT.Sdk.Generated.TaskApi.Models.U7533u8bf7u8f66u8f86u5217u8868u5230u8fbeu7ad9u70b9u4ee3u4ef7
        {
            MapId = mapId,
            StationId = stationId,
            DeviceKeys = [deviceKey],
        };

        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Task.V1.Route.GetRouteCostsBy
                .PostAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            "getRouteCostsBy");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);

        var entry = response.Result?.DeviceCostsList?
            .FirstOrDefault(c => string.Equals(c.DeviceKey, deviceKey, StringComparison.Ordinal));

        if (entry?.Costs is null)
        {
            throw new RiotApiException(
                $"getRouteCostsBy returned no costs for deviceKey={deviceKey}.",
                businessCode: "route-cost-missing");
        }

        return new RouteCost(entry.Costs.Value);
    }

    /// <summary>
    /// POST /api/task/v1/route/queryNearEnd — nearest end Station among candidates (NearStationQuery).
    /// Returns stationId only (not path geometry). Throws on business failure (ADR-sdk-0005).
    /// </summary>
    public async Task<int> QueryNearestEndAsync(
        int mapId,
        int startStationId,
        IReadOnlyList<int> endStationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endStationIds);
        if (endStationIds.Count == 0)
        {
            throw new ArgumentException("endStationIds must not be empty.", nameof(endStationIds));
        }

        var client = _session.CreateGeneratedTaskClient();
        var body = new RIoT.Sdk.Generated.TaskApi.Models.U627eu6700u8fd1u7684u7ec8u70b9u53c2u6570Object
        {
            MapId = mapId,
            StartStationId = startStationId,
            EndStationIds = endStationIds.Select(id => (int?)id).ToList(),
        };

        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Task.V1.Route.QueryNearEnd
                .PostAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            "queryNearEnd");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);

        return RiotBusinessResponse.RequireResult(
            response.Result,
            "queryNearEnd",
            "near-end-missing");
    }

    /// <summary>
    /// POST /api/task/v1/route/queryNearestStart — nearest start Station among candidates (NearStationQuery).
    /// Returns stationId only (not path geometry). Throws on business failure (ADR-sdk-0005).
    /// </summary>
    public async Task<int> QueryNearestStartAsync(
        int mapId,
        int endStationId,
        IReadOnlyList<int> startStationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startStationIds);
        if (startStationIds.Count == 0)
        {
            throw new ArgumentException("startStationIds must not be empty.", nameof(startStationIds));
        }

        var client = _session.CreateGeneratedTaskClient();
        var body = new RIoT.Sdk.Generated.TaskApi.Models.U627eu6700u8fd1u7684u8d77u70b9u53c2u6570Object
        {
            MapId = mapId,
            EndStationId = endStationId,
            StartStationIds = startStationIds.Select(id => (int?)id).ToList(),
        };

        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Task.V1.Route.QueryNearestStart
                .PostAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            "queryNearestStart");

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);

        return RiotBusinessResponse.RequireResult(
            response.Result,
            "queryNearestStart",
            "near-start-missing");
    }

    /// <summary>
    /// GET /api/task/v1/route/ — whether RIoT currently holds any dynamic route cost.
    /// Throws <see cref="RiotApiException"/> on business failure (ADR-sdk-0005).
    /// </summary>
    /// <remarks>
    /// Reports presence and a count, not the values: every observation of this endpoint has
    /// answered <c>"result":{}</c>, so its populated shape has never been seen and there is
    /// nothing honest to deserialize into. The body is read here rather than through the generated
    /// model for the same reason (ADR-sdk-0009 covers the same choice for the imap endpoints).
    /// </remarks>
    public async Task<DynamicRouteCostPresence> ReadDynamicRouteCostPresenceAsync(
        CancellationToken cancellationToken = default)
    {
        const string operation = "route/";
        var client = _session.CreateGeneratedTaskClient();
        var request = client.Api.Task.V1.Route.EmptyPathSegment.ToGetRequestInformation();

        return await RiotWire.ReadAsync(
            _session.Adapter,
            request,
            operation,
            result => result.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Object =>
                    Presence(System.Linq.Enumerable.Count(result.EnumerateObject())),
                System.Text.Json.JsonValueKind.Array => Presence(result.GetArrayLength()),
                // Undefined or null: the field was absent, which is the same "nothing here" as an
                // empty object. Anything else is a shape violation and fails closed.
                System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null =>
                    Presence(0),
                _ => throw new RiotApiException(
                    $"{operation} returned result of kind {result.ValueKind}, expected an object."),
            },
            cancellationToken).ConfigureAwait(false);

        static DynamicRouteCostPresence Presence(int count) => new(count > 0, count);
    }

    /// <summary>
    /// POST /api/task/v1/order/command/{orderId} with CMD_ORDER_CANCEL (BC-ORDER-003).
    /// Uses string orderId; does not disable the vehicle. Throws on business failure (ADR-sdk-0005).
    /// </summary>
    public Task CancelOrderAsync(
        string orderId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => PostOrderCommandAsync(
            orderId,
            RIoT.Sdk.Generated.TaskApi.Models.OrderCommandDTOObject_commandType.CMD_ORDER_CANCEL,
            reason,
            cancellationToken);

    /// <summary>
    /// POST /api/task/v1/order/command/{orderId} with CMD_ORDER_HELD (BC-ORDER-006).
    /// Pauses an executing move order (orderState → HELD). Pair with OrderContinue.
    /// </summary>
    public Task OrderHoldAsync(
        string orderId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => PostOrderCommandAsync(
            orderId,
            RIoT.Sdk.Generated.TaskApi.Models.OrderCommandDTOObject_commandType.CMD_ORDER_HELD,
            reason,
            cancellationToken);

    /// <summary>
    /// POST /api/task/v1/order/command/{orderId} with CMD_ORDER_CONTINUE_FROM_HELD (BC-ORDER-006).
    /// Resumes a HELD move order. Do not use for OrderHang (use HangContinue).
    /// </summary>
    public Task OrderContinueAsync(
        string orderId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => PostOrderCommandAsync(
            orderId,
            RIoT.Sdk.Generated.TaskApi.Models.OrderCommandDTOObject_commandType.CMD_ORDER_CONTINUE_FROM_HELD,
            reason,
            cancellationToken);

    /// <summary>
    /// POST /api/task/v1/order/command/{orderId} with CMD_ORDER_CONTINUE_FROM_HANG (BC-ORDER-015).
    /// Attempts to pull an OrderHang back to EXECUTING. Do not use for HELD (use OrderContinue).
    /// HTTP/business success does not prove the order left HANG — confirm via orderState.
    /// </summary>
    public Task HangContinueAsync(
        string orderId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => PostOrderCommandAsync(
            orderId,
            RIoT.Sdk.Generated.TaskApi.Models.OrderCommandDTOObject_commandType.CMD_ORDER_CONTINUE_FROM_HANG,
            reason,
            cancellationToken);

    /// <summary>
    /// POST /api/task/vehicles/updateVehicleIntegrationLevel with serviceId=enable (BC-VEH-003).
    /// Brings the vehicle into dispatch (ON_LINE). Never pass ON_LINE as serviceId.
    /// </summary>
    public Task DispatchEnableAsync(
        string deviceKey,
        CancellationToken cancellationToken = default)
        => UpdateIntegrationLevelAsync(deviceKey, enable: true, cancellationToken);

    /// <summary>
    /// POST /api/task/vehicles/updateVehicleIntegrationLevel with serviceId=disable (BC-VEH-003).
    /// Removes the vehicle from dispatch (OFF_LINE). Does not cancel an executing order.
    /// </summary>
    public Task DispatchDisableAsync(
        string deviceKey,
        CancellationToken cancellationToken = default)
        => UpdateIntegrationLevelAsync(deviceKey, enable: false, cancellationToken);

    /// <summary>
    /// Access the underlying Kiota client for endpoints not yet wrapped.
    /// Prefer adding explicit facade methods for commonly used calls.
    /// </summary>
    public RIoT.Sdk.Generated.TaskApi.TaskClient Raw => _session.CreateGeneratedTaskClient();

    private async Task PostOrderCommandAsync(
        string orderId,
        RIoT.Sdk.Generated.TaskApi.Models.OrderCommandDTOObject_commandType commandType,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var client = _session.CreateGeneratedTaskClient();
        var body = new RIoT.Sdk.Generated.TaskApi.Models.OrderCommandDTOObject
        {
            CommandType = commandType,
            DisableVehicle = false,
            Reason = reason,
        };

        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Task.V1.Order.Command[orderId]
                .PostAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            commandType.ToString());

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);
    }

    private async Task UpdateIntegrationLevelAsync(
        string deviceKey,
        bool enable,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        var serviceId = enable ? "enable" : "disable";
        var operation = enable ? "DispatchEnable" : "DispatchDisable";
        var client = _session.CreateGeneratedTaskClient();
        var body = new RIoT.Sdk.Generated.TaskApi.Models.BatchVehicleOperation
        {
            DeviceKeys = [deviceKey],
            ServiceId = serviceId,
        };

        var response = RiotBusinessResponse.RequireResponse(
            await client.Api.Task.Vehicles.UpdateVehicleIntegrationLevel
                .PostAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            operation);

        RiotBusinessResponse.EnsureSuccess(response.Code, response.Message);
    }
}
