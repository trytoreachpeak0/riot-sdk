# imap 路网五端点自定义反序列化，URL 与鉴权仍走 Kiota

`edges`／`stations`／`removedEdge`／`removedStation`／`mapEdgeGroup/all` 五个只读端点的响应体由 SDK 自己解析（`ImapWire` / `imap_wire`），不经 Kiota 生成模型；请求 URL 与鉴权仍由生成层的 `ToGetRequestInformation()` 构造，body 用 `SendPrimitiveAsync<Stream>`／`send_primitive_async(..., "bytes", ...)` 取原始字节。解析结果是 `RIoT.Sdk.Core` 里的具名领域类型，线格式不外溢到产品代码。这**收窄了 ADR-sdk-0006**「不采用长期手写 imap HTTP 旁路」的适用范围：手写的只是这五个端点的反序列化，不是 HTTP 旁路，Map/Station 的既有 `ListMapsAsync`／`ListStationsAsync` 一字未动，仍走生成模型。

理由是实测：Round 43 在生产 RIoT（`172.19.206.222:8888`，mapId 25）逐条比对，`riot_swagger/imap.json` 与实际线格式对不上，**且不是改 spec 能补齐的**。四处：

1. **`edges` 是 snake_case，且复合词拼法与 spec 不同**——现场 `s_node`／`e_node`，生成模型读 `snode`／`enode`；还有 `is_back_edge`、`limit_v`、`robot_direction`、`e_facing`。直接用生成的 `Edge` 会得到一片 null。
2. **`stations` 的键名字面带点**——`pos.x`、`check_pos.yaw`、`pgv_offset.x`，不是嵌套对象。
3. **`mapEdgeGroup/all` 的 result 是 `map<组名, list>`**，Kiota 为它生成的 `_result` 类是个空壳，只有 `AdditionalData`；且该端点是 camelCase，与同一服务的 `edges`／`stations` 不同。
4. **`mapEdgeGroup` 的时间戳 `"2024-06-26 10:57:58"` 不是 ISO 8601**，生成模型的 `GetDateTimeOffsetValue()` 解析不了。

第 3、4 条决定了结论：改 `specs/imap.json` 让生成器产出正确键名能解决第 1、2 条，但解决不了 map 外层与非 ISO 时间戳；而改 `specs/` 是双端共享契约，会把两端的生成层一起卷进来。手写反序列化的范围反而更小。

**代价，如实记：**线格式的知识现在有两处——`specs/imap.json`（spec 的说法）与这五个 reader（现场的说法），两者不一致且不会自动对齐。RIoT 升级改了键名时，编译不会失败，测试也不会红，除非有人重跑一轮 Round 43 式的实采。缓解措施是三条：reader 对必需字段 **fail-closed**（读不到就抛 `RiotApiException`，不静默返回空图或跳过元素——静默会让调用方在一张缺边的图上派车）；两个 spelling 都接受（现场的与 spec 的）；测试夹具是 Round 43 的**逐字响应体**，不是手编的样例。

**`removedEdge`／`removedStation` 的形状未经实测。**Round 43 在 map25 上两者都返回 `result: []`，元素形状取自 spec 的 `MapRemovedEdge对象`／`MapRemovedStation对象` 组件，从未见过非空。`mapEdgeGroup/all` 的空形态同样未见——map25 一个边组都没有，全 RIoT 的边组都属于别的 Map，所以空数组与空对象都按「没有」处理，非空数组则 fail-closed。

**Status**: accepted

**Considered Options**: 改 `specs/imap.json` 让 Kiota 生成正确键名（解决不了 map 外层与非 ISO 时间戳，且卷入双端生成层）；产品代码走 `.Raw` 自己解析（`REQ-0309` 要求具名 Facade，且线格式会外溢）；手写完整 HTTP 旁路（重复鉴权与 URL 构造，正是 ADR-sdk-0006 要避免的）；只手写反序列化、URL 与鉴权仍走生成层（采纳）。
