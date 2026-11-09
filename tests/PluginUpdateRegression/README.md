# 一键更新离线回归

共享真实更新核心源码，注入内存 HTTP transport；不访问公网、不加载宿主 DLL、不安装或发布产品。使用 `dist` 中已发布的 AutoCAD/ZWCAD 2.2.0.70 ZIP 作完整性与产品兼容性基线，所有写入限定到 `work/one-click-update-71/test-*`。

在项目根目录运行：

```powershell
dotnet run --project tests/PluginUpdateRegression -c Release -f net8.0-windows -- .
dotnet run --project tests/PluginUpdateRegression -c Release -f net48 -- .
powershell -NoProfile -File tests/PluginUpdateRegression/HandoffRegression.ps1
```

核心测试覆盖版本比较、产品清单、HTTP/HTML/重定向错误、截断及 SHA256 校验、取消清理、安全解压、双产品全部内核与逐文件哈希、缺内核及宿主 API 混包拦截、只读配置保护、长路径明确失败和丢弃下载。交接测试只加载内嵌 PowerShell 函数，验证临时目录联接回滚、手动修改保护、配置精确清单及等待期间包篡改；注册表函数使用内存模拟，真实注册表不写入。

2.2.0.83 起增加两张共享联系 PNG 的存在性、签名、IHDR 和非零尺寸检查；旧 2.2.0.70/82 包不要求外部图片。AutoCAD 2027 内核从 2.2.0.80 起才属于必需内核，保留旧 70 包基线。`powershell -NoProfile -File tests/shared-contact-resources-regression.ps1` 可隔离验证两个安装入口和自检脚本的相同规则，不执行安装或启动 CAD。

若旧包已从 `dist` 清理，可将旧基线目录作为第二个参数；第三个可选参数为包含双产品 2.2.0.83 ZIP 的目录，用于真实新包完整验证、原图哈希、PDB 排除及删失共享图片拦截：`dotnet run --project tests/PluginUpdateRegression -c Release -f net8.0-windows -- . work/public-downloads-2.2.0.70 dist`。

交接测试还完整执行 `Prepare-Transaction`：在测试进程内临时重定向 `APPDATA`（结束恢复），创建独立目录联接，使用真实旧包的 `PackageContents.xml`，模拟无注册产品键；覆盖升级、同版、拒绝降级、多来源数据合并及冲突。安装脚本只写惰性占位内容，从不执行。

这些检查不能代替实际 CAD 加载与退出后安装验收。PowerShell 2 仅检查已知不兼容语法/API，没有实际 PowerShell 2 运行环境证据。
