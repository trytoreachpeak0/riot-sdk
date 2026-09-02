# ADR Index

Architecture Decision Records for this repository. Cite as `ADR-sdk-<NNNN>` (e.g. `ADR-sdk-0001`).

这些 ADR 2026-09-02 从 `8005---AGV` 随 `rcs/riot-sdk/` 一起迁到本仓库。跨子系统的决策（`ADR-cross-*`）留在 `8005-agv-program/docs/adr/cross/`。

## sdk — RIoT SDK

| ID | Title |
|---|---|
| [ADR-sdk-0001](0001-default-auth-callapikey.md) | 第一版默认鉴权使用 CallApiKey |
| [ADR-sdk-0002](0002-v1-seam-session-named-methods-raw-escape.md) | RiotSession 具名方法为主，Raw 为逃逸舱 |
| [ADR-sdk-0003](0003-v1-facade-method-scope.md) | 第一版必须封装的 Facade 能力范围 |
| [ADR-sdk-0004](0004-v1-csharp-python-parity.md) | 第一版 C# 与 Python 同步交付 |
| [ADR-sdk-0005](0005-facade-throws-on-business-failure.md) | 具名 Facade 统一将业务失败转为 RiotApiException |
| [ADR-sdk-0006](0006-imap-via-kiota.md) | Map/Station 经 imap OpenAPI 纳入 Kiota 生成 |
| [ADR-sdk-0007](0007-ready-helpers-no-blocking-wait.md) | 可再派判定辅助，不内置阻塞 Wait |
| [ADR-sdk-0008](0008-v1-explicit-non-goals.md) | 第一版明确排除的能力 |
