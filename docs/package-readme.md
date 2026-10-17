# RIoT SDK

RIoT（斯坦德 RCS）接口的 C# 客户端库，源码在 [trytoreachpeak0/riot-sdk](https://github.com/trytoreachpeak0/riot-sdk)。三个包版本号始终相同：

| 包 | 内容 |
| --- | --- |
| `RIoT.Sdk.Facade` | 具名方法（建单、查单、急停、路网读取等），一般只引用这一个 |
| `RIoT.Sdk.Core` | 鉴权、业务响应判定、领域数据结构 |
| `RIoT.Sdk.Generated` | 由 RIoT 的 OpenAPI 文档用 Kiota 生成的客户端，不要手改 |

目标框架 `net8.0`，更高版本的 .NET 也能用。许可证 MIT。

## 从 GitHub Packages 还原

包发布在 `https://nuget.pkg.github.com/trytoreachpeak0/index.json`，是公开的。GitHub 的 NuGet 源连读取公开包也要令牌，任何 GitHub 账号的令牌都行。

1. 在消费方仓库的 `nuget.config` 里加这个源，并用包来源映射让 `RIoT.Sdk.*` 只从它拿：

   ```xml
   <packageSources>
     <add key="github" value="https://nuget.pkg.github.com/trytoreachpeak0/index.json" />
   </packageSources>
   <packageSourceMapping>
     <packageSource key="github"><package pattern="RIoT.Sdk.*" /></packageSource>
   </packageSourceMapping>
   ```

2. 开发机：建一个只勾 `read:packages` 的 classic 令牌，存进用户级 NuGet 配置（Windows 上加密保存），不要放进仓库：

   ```
   dotnet nuget add source https://nuget.pkg.github.com/trytoreachpeak0/index.json --name github --username <GitHub 用户名> --password <令牌> --configfile "$env:APPDATA/NuGet/NuGet.Config"
   ```

3. GitHub Actions：给工作流 `permissions: packages: read`，在还原步骤上设环境变量 `NuGetPackageSourceCredentials_github`，值是用分号隔开的 `Username` 与 `Password` 两项，`Password` 一项取 `${{ github.token }}`（写法见 trytoreachpeak0/simple 的 `.github/workflows/ci.yml`）。包是公开的，任何仓库的工作流都能读，不用在包设置里授权。

然后照常引用：

```xml
<PackageReference Include="RIoT.Sdk.Facade" Version="<版本>" />
```
