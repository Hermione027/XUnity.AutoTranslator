# 当前计划

目标：把仓库整理成中文化参考版，并提供一个可直接在 `AutoTranslatorConfig.ini` 里配置 `Url`、`ApiKey`、`Model`、`SystemPrompt`、`Temperature` 的 OpenAI 兼容翻译端点，同时保持后续能和官方仓库持续合并。

已确认的前提：
- 项目会自动发现 `Translators` 目录下的 `ITranslateEndpoint` 实现。
- 新增独立翻译器 DLL 后，会出现在现有 UI 的端点列表里。
- `CustomTranslate` 不是 OpenAI 兼容接口，不适合直接拿来对接 `/v1/chat/completions`。
- 敏感配置只放在本地 `AutoTranslatorConfig.ini`，不要写进仓库。

已完成：
- 新增 `OpenAICompatibleTranslate` 翻译端点。
- 已接入打包和安装流程。
- README 和配置示例已经开始中文化。
- 修复 `TranslationContext.Complete(string[])` 的索引校验问题。
- `HookingHelper` 已补上 `ExecutionEngineException` 的回退判断。
- 本地游戏配置已经改成 `Endpoint=OpenAICompatibleTranslate`，并去掉了错误的同名 fallback。

当前排查结论：
- 译文计数还是 `0`，不是接口没通，而是 hook 阶段没挂上。
- 日志里能看到 `System.ExecutionEngineException: String conversion error: Illegal byte sequence encountered in the input`。
- 错误点在 `UnityEngine.UI.Text` / `TMPro.TMP_Text` 的 hook 构造过程中。
- 这更像是当前游戏目录的日文长路径触发了 Mono / MonoMod 的编码问题。

当前实现状态：
- `OpenAICompatibleTranslate` 已改为支持可配置 `MaxConcurrency` 和 `MaxTranslationsPerRequest`。
- 同一个 endpoint 现在可以一次请求多条文本，并尝试按 JSON 数组或逐行文本解析返回值。
- `TranslationContext.Complete(string[])` 和 `Common.ExtProtocol.TranslationContext.Complete(string[])` 的索引校验已修正。
- README 已补上 OpenAI 兼容端点的并发示例配置。

下一步：
1. 用 VS2022 的 `MSBuild.exe` 重新编译 Release。
2. 把新 DLL 同步到游戏目录。
3. 在 `AutoTranslatorConfig.ini` 里把 `MaxConcurrency` / `MaxTranslationsPerRequest` 调到合适值，再跑单条和批量测试。
4. 如果翻译仍不进入队列，再细看 `TextGetterCompatibilityMode`、`EnableTextPathLogging` 和具体 hook 目标。

注意事项：
- 不要把 API key、私有网关地址写进仓库。
- 后续尽量只改翻译器扩展和中文文档，少碰核心逻辑，方便跟官方合并。
